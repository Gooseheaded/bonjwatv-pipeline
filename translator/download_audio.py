import logging  # Keep logging for internal use
import os

import yt_dlp


def _apply_cookie_options(opts: dict) -> None:
    """Augment yt-dlp options with cookies if configured via env.

    - YTDLP_COOKIES: path to cookies.txt (Netscape/Chrome export)
    - YTDLP_COOKIES_BROWSER: browser name for automatic cookie pickup (e.g., 'chrome', 'firefox')
    """
    cookiefile = os.getenv("YTDLP_COOKIES", "").strip()
    if cookiefile and os.path.exists(cookiefile):
        opts["cookiefile"] = cookiefile
        return
    browser = os.getenv("YTDLP_COOKIES_BROWSER", "").strip()
    if browser:
        # yt-dlp accepts a tuple for cookiesfrombrowser: (browser[, profile, keyring])
        opts["cookiesfrombrowser"] = (browser,)


def _summarize_formats(info: dict) -> str:
    try:
        fmts = info.get("formats") or []
        rows = []
        for f in fmts:
            rows.append(
                f"id={f.get('format_id')} ext={f.get('ext')} "
                f"v={f.get('vcodec')} a={f.get('acodec')} "
                f"abr={f.get('abr') or ''} tbr={f.get('tbr') or ''}"
            )
        return "; ".join(rows[:40])
    except Exception:
        return "(no formats)"


def _pick_best_audio_format(info: dict) -> str | None:
    """Return a yt-dlp format_id for the best audio-only format, or None if none exist."""
    fmts = info.get("formats") or []
    best_id = None
    best_score = -1.0
    for f in fmts:
        if f.get("vcodec") not in (None, "none"):
            continue  # not audio-only
        ext = (f.get("ext") or "").lower()
        ac = (f.get("acodec") or "").lower()
        abr = f.get("abr") or f.get("tbr") or 0
        score = 0.0
        # Preference: AAC/M4A highest, then Opus/WebM, then other audio
        if ext == "m4a" or "aac" in ac or "mp4a" in ac:
            score += 1000
        elif ext == "webm" or "opus" in ac:
            score += 800
        else:
            score += 500
        try:
            score += float(abr)
        except Exception:
            pass
        if score > best_score:
            best_score = score
            best_id = f.get("format_id")
    return best_id


def _base_ydlp_opts(output_dir: str, video_id: str) -> dict:
    return {
        "outtmpl": os.path.join(output_dir, f"{video_id}.%(ext)s"),
        "postprocessors": [
            {
                "key": "FFmpegExtractAudio",
                "preferredcodec": "mp3",
                "preferredquality": "192",
            }
        ],
        # Harden against bot detection and transient errors
        "quiet": True,
        "no_warnings": True,
        "retries": 3,
        "fragment_retries": 3,
        "http_headers": {  # Mobile UA tends to be less challenged
            "User-Agent": (
                "Mozilla/5.0 (Linux; Android 12; SM-G998B) "
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36"
            )
        },
        # Avoid parallel fragment downloads which sometimes trigger throttling
        "concurrent_fragment_downloads": 1,
    }


def run_download_audio(url: str, video_id: str, output_dir: str = "audio") -> bool:
    """Download audio from a URL and save it to the output directory.

    Supports yt-dlp cookies via env for painless auth when YouTube blocks unauthenticated requests:
    - Set `YTDLP_COOKIES=/path/to/cookies.txt` (exported from your browser)
    - Or set `YTDLP_COOKIES_BROWSER=chrome` (or firefox, brave, edge) for auto pickup
    """
    os.makedirs(output_dir, exist_ok=True)
    # Final expected path after extraction
    final_path = os.path.join(output_dir, f"{video_id}.mp3")
    if os.path.exists(final_path):
        logging.info(f"{final_path} already exists, skipping download")
        return True

    # Adaptive probing and selection
    def attempt(android_client: bool) -> tuple[bool, dict | None]:
        opts = _base_ydlp_opts(output_dir, video_id)
        if android_client:
            opts["extractor_args"] = {"youtube": {"player_client": ["android"]}}
        _apply_cookie_options(opts)
        info: dict | None = None
        try:
            ydl_probe = yt_dlp.YoutubeDL({k: v for k, v in opts.items() if k != "postprocessors"})
            info = ydl_probe.extract_info(url, download=False)
        except Exception as ex:
            logging.warning("yt-dlp probe failed (%s); continuing with fallback selection", ex)
        # Choose format
        fmt = None
        if isinstance(info, dict):
            fmt = _pick_best_audio_format(info)
        # If we didn't find audio-only, let yt-dlp pick best muxed and extract
        format_selector = fmt if fmt else "best"
        try:
            opts_dl = dict(opts)
            opts_dl["format"] = format_selector
            ydl = yt_dlp.YoutubeDL(opts_dl)
            ydl.download([url])
            if os.path.exists(final_path):
                logging.info(f"Downloaded audio to {final_path}")
                return True, info
            # In rare cases, file may have different name; still treat as success if any mp3 for id exists
            guessed = os.path.join(output_dir, f"{video_id}.mp3")
            return (os.path.exists(guessed), info)
        except Exception as e:
            # Log a concise formats table to aid debugging
            if isinstance(info, dict):
                logging.error(
                    "yt-dlp error for %s: %s. Available formats: %s",
                    video_id,
                    e,
                    _summarize_formats(info),
                )
            else:
                logging.error("Error downloading audio for %s: %s", video_id, e)
            return False, info

    ok, info = attempt(android_client=True)
    if ok:
        return True
    # Retry without Android client preference
    ok2, info2 = attempt(android_client=False)
    if ok2:
        return True
    # Final guidance for auth issues
    # Try to detect auth-type errors in either attempt context:
    logging.info(
        "If YouTube blocks the request, set YTDLP_COOKIES=/path/to/cookies.txt or YTDLP_COOKIES_BROWSER=chrome to use browser cookies."
    )
    return False

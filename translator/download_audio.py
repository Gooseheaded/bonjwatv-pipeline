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

    # Use a template without a fixed extension; the postprocessor will write .mp3
    ydl_opts = {
        "format": "bestaudio",
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
        # Prefer Android client where supported (yt-dlp extractor arg)
        "extractor_args": {"youtube": {"player_client": ["android"]}},
        # Avoid parallel fragment downloads which sometimes trigger throttling
        "concurrent_fragment_downloads": 1,
    }
    _apply_cookie_options(ydl_opts)

    try:
        ydl = yt_dlp.YoutubeDL(ydl_opts)
        ydl.download([url])
        logging.info(f"Downloaded audio to {final_path}")
        return True
    except Exception as e:
        msg = str(e)
        if "403" in msg:
            logging.error(
                "Error downloading audio for %s: %s. If YouTube blocks the request, set "
                "YTDLP_COOKIES=/path/to/cookies.txt or YTDLP_COOKIES_BROWSER=chrome to use browser cookies.",
                video_id,
                e,
            )
        else:
            logging.error(f"Error downloading audio for {video_id}: {e}")
        return False

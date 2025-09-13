#!/usr/bin/env python3
"""Submit processed videos + subtitles to the Catalog API.

Inputs:
- --catalog-base: Base URL of the catalog API (e.g., http://localhost:5002)
- --api-key: Ingest token (matches server allowlist API_INGEST_TOKENS)
- --videos-json: Path to videos list (prefer enriched JSON if available)
- --subtitles-dir: Path to directory containing en_{videoId}.srt files

The script uploads each English SRT to /api/uploads/subtitles and then submits
the video to /api/submissions/videos with the returned storage_key.
"""

import argparse
import json
import os
import sys
from typing import Any, Dict, List, Optional

import urllib.request
import urllib.error
import re
import hashlib


def _is_trivial_srt(file_path: str) -> bool:
    """Return True if the SRT looks like a stub or too small to be meaningful.

    Heuristics:
    - File size < 128 bytes
    - Fewer than 3 subtitle blocks
    - Only a single short line like "Hello!"
    """
    try:
        size = os.path.getsize(file_path)
        if size < 128:
            return True
        text = open(file_path, encoding="utf-8", errors="ignore").read()
        # Count blocks
        blocks = re.findall(
            r"\n?\d+\s+\d{2}:\d{2}:\d{2},\d{3} --> \d{2}:\d{2}:\d{2},\d{3}\s+[\s\S]*?(?=\n\n|\Z)",
            text,
            flags=re.MULTILINE,
        )
        if len(blocks) < 3:
            return True
        # Extremely short total content
        payload = re.sub(r"\d+\s+\d{2}:\d{2}:\d{2},\d{3} --> .*", "", text)
        payload = re.sub(r"\s+", " ", payload).strip()
        if len(payload) < 64:
            return True
        return False
    except Exception:
        return True


def _print(msg: str) -> None:
    print(msg, flush=True)


def http_post_json(url: str, body: Dict[str, Any], headers: Optional[Dict[str, str]] = None) -> Dict[str, Any]:
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as he:
        try:
            detail = he.read().decode("utf-8")
        except Exception:
            detail = ""
        raise urllib.error.HTTPError(he.url, he.code, f"{he.reason}: {detail}", he.hdrs, None)


def http_post_multipart(
    url: str,
    fields: Dict[str, str],
    file_field: str,
    file_path: str,
    content_type: str = "text/plain",
    headers: Optional[Dict[str, str]] = None,
) -> Dict[str, Any]:
    # Minimal multipart builder (no external deps)
    boundary = "----bwktboundary"
    lines: List[bytes] = []
    for k, v in fields.items():
        lines.append(f"--{boundary}\r\n".encode("utf-8"))
        lines.append(f"Content-Disposition: form-data; name=\"{k}\"\r\n\r\n".encode("utf-8"))
        lines.append(v.encode("utf-8") + b"\r\n")
    fname = os.path.basename(file_path)
    lines.append(f"--{boundary}\r\n".encode("utf-8"))
    lines.append(
        f"Content-Disposition: form-data; name=\"{file_field}\"; filename=\"{fname}\"\r\n".encode("utf-8")
    )
    lines.append(f"Content-Type: {content_type}\r\n\r\n".encode("utf-8"))
    with open(file_path, "rb") as f:
        lines.append(f.read())
    lines.append(b"\r\n")
    lines.append(f"--{boundary}--\r\n".encode("utf-8"))
    body = b"".join(lines)
    merged_headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    if headers:
        merged_headers.update(headers)
    req = urllib.request.Request(url, data=body, headers=merged_headers)
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _load_cached_title_en(base_dir: str, vid: str) -> Optional[str]:
    """Try to load title_en from a local cache next to the provided videos_json.

    This makes submit robust even if a non-enriched videos.json is passed.
    """
    try:
        cache_file = os.path.join(base_dir, ".cache", f"title_{vid}.json")
        if os.path.exists(cache_file):
            data = json.load(open(cache_file, encoding="utf-8"))
            t = (data.get("title_en") or "").strip()
            return t or None
    except Exception:
        pass
    return None


def choose_title(item: Dict[str, Any], base_dir: Optional[str], vid: str) -> str:
    """Return a translated English title if available; otherwise empty.

    Accept only explicit English fields (EN Title/title_en) or cached title_en.
    Do NOT fall back to the original 'title' to avoid stub/foreign-language titles.
    """
    t = item.get("EN Title") or item.get("title_en") or ""
    if t:
        return t
    if base_dir:
        cached = _load_cached_title_en(base_dir, vid)
        if cached:
            return cached
    return ""


def _reconstruct_en_from_cache(cache_dir: str, vid: str, subtitles_dir: str) -> Optional[str]:
    """Attempt to reconstruct en_{vid}.srt from cached translated chunks.

    Looks for files like {cache_dir}/kr_{vid}_chunk{N}.json with a 'translation' field
    containing SRT text. Parses and concatenates in chunk order while renumbering.
    Returns the output file path if reconstruction succeeds, else None.
    """
    try:
        base = f"kr_{vid}"
        if not os.path.isdir(cache_dir):
            return None
        # Collect chunk files in order
        chunk_files = []
        for name in os.listdir(cache_dir):
            m = re.match(rf"{re.escape(base)}_chunk(\d+)\.json$", name)
            if m:
                chunk_files.append((int(m.group(1)), os.path.join(cache_dir, name)))
        if not chunk_files:
            return None
        chunk_files.sort(key=lambda x: x[0])

        # Helper to parse SRT blocks
        pat = re.compile(r"(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)", re.MULTILINE)
        merged_blocks: List[str] = []
        for idx, path in chunk_files:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
            text = (data.get("translation") or "").strip()
            if not text:
                continue
            # Extract SRT blocks as raw text segments to keep simple
            for m in pat.finditer(text):
                block = f"{m.group(2)} --> {m.group(3)}\n{m.group(4).strip()}\n"
                merged_blocks.append(block)
        if not merged_blocks:
            return None
        # Renumber and write
        os.makedirs(subtitles_dir, exist_ok=True)
        out_path = os.path.join(subtitles_dir, f"en_{vid}.srt")
        with open(out_path, "w", encoding="utf-8") as out:
            for i, blk in enumerate(merged_blocks, start=1):
                out.write(str(i) + "\n")
                out.write(blk)
                out.write("\n")
        return out_path
    except Exception:
        return None


def run(catalog_base: str, api_key: str, videos_json: str, subtitles_dir: str) -> bool:
    def _fetch_hashes(ids: list[str]) -> dict[str, Optional[str]]:
        try:
            if not ids:
                return {}
            # Build query: support repeated ids
            from urllib.parse import urlencode
            query = urlencode([("ids", vid) for vid in ids])
            url = f"{catalog_base.rstrip('/')}/api/subtitles/hashes?{query}"
            with urllib.request.urlopen(url) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                # Ensure keys exist for all ids
                out: dict[str, Optional[str]] = {}
                for vid in ids:
                    val = data.get(vid)
                    out[vid] = val if isinstance(val, str) else (None if val is None else None)
                return out
        except Exception:
            return {}

    def _sha256_file(path: str) -> Optional[str]:
        try:
            with open(path, "rb") as f:
                h = hashlib.sha256()
                for chunk in iter(lambda: f.read(8192), b""):
                    h.update(chunk)
                return h.hexdigest()
        except Exception:
            return None
    try:
        with open(videos_json, encoding="utf-8") as f:
            items = json.load(f)
    except Exception as e:
        _print(f"ERROR: failed to read {videos_json}: {e}")
        return False

    headers = {"X-Api-Key": api_key}
    ok_all = True
    skipped_identical = 0
    skipped_invalid = 0
    submitted_count = 0
    base_dir = os.path.dirname(os.path.abspath(videos_json))
    # Preflight: fetch remote hashes for all candidate IDs up front
    video_ids: list[str] = []
    for it in items:
        vid = it.get("v") or it.get("videoId") or it.get("id")
        if vid:
            video_ids.append(str(vid))
    remote_hashes = _fetch_hashes(video_ids)

    for item in items:
        vid = item.get("v") or item.get("videoId") or item.get("id")
        if not vid:
            continue
        en_srt = os.path.join(subtitles_dir, f"en_{vid}.srt")
        if not os.path.exists(en_srt):
            # Try to reconstruct from cached chunks first
            reconstructed = None
            try:
                reconstructed = _reconstruct_en_from_cache(
                    os.path.join(base_dir, ".cache"), vid, subtitles_dir
                )
            except Exception:
                reconstructed = None
            if reconstructed:
                _print(
                    f"INFO: reconstructed English SRT from cache for {vid}: {reconstructed}"
                )
                en_srt = reconstructed
            else:
                _print(
                    f"WARN: English subtitles not found for {vid}; skipping submission (no stub created)."
                )
                ok_all = False
                continue
        # Preflight: skip upload if hash matches existing
        try:
            local_hash = _sha256_file(en_srt)
            remote_hash = remote_hashes.get(str(vid)) if remote_hashes else None
            if local_hash and isinstance(remote_hash, str) and local_hash.lower() == remote_hash.lower():
                _print(f"INFO: skipping {vid} — English SRT identical to server (hash match)")
                skipped_identical += 1
                continue
        except Exception:
            pass

        # Validate non-trivial subtitles
        if _is_trivial_srt(en_srt):
            # Try to replace with reconstruction if possible
            reconstructed = _reconstruct_en_from_cache(
                os.path.join(base_dir, ".cache"), vid, subtitles_dir
            )
            if reconstructed and not _is_trivial_srt(reconstructed):
                en_srt = reconstructed
                _print(
                    f"INFO: replaced trivial English SRT with reconstructed for {vid}: {reconstructed}"
                )
            else:
                _print(
                    f"WARN: English subtitles for {vid} appear invalid/trivial; skipping submission."
                )
                skipped_invalid += 1
                ok_all = False
                continue

        # 1) Upload SRT
        try:
            up_url = f"{catalog_base.rstrip('/')}/api/uploads/subtitles"
            upload_resp = http_post_multipart(
                up_url,
                {"videoId": vid, "version": "1"},
                "file",
                en_srt,
                headers=headers,
            )
            storage_key = upload_resp.get("storage_key")
            if not storage_key:
                _print(f"ERROR: upload response missing storage_key for {vid}")
                ok_all = False
                continue
        except urllib.error.HTTPError as he:
            if he.code == 403:
                _print(
                    "ERROR: upload rejected with 403 Forbidden — the API requires an ingest token in X-Api-Key.\n"
                    "Ensure you passed --api-key and that the server's API_INGEST_TOKENS includes this token.\n"
                    "For local dev, set API_INGEST_TOKENS in .env (e.g., DEV123), rebuild docker compose, and pass --api-key DEV123."
                )
            else:
                _print(f"ERROR: upload failed for {vid}: {he}")
            ok_all = False
            continue

        # 2) Submit video proposal
        try:
            sub_url = f"{catalog_base.rstrip('/')}/api/submissions/videos"
            title = choose_title(item, base_dir=base_dir, vid=vid)
            if not title:
                _print(
                    f"WARN: No translated English title found for {vid}; skipping submission."
                )
                ok_all = False
                continue
            # Coerce tags to list[str]
            raw_tags = item.get("tags") or []
            tags: List[str] = []
            if isinstance(raw_tags, list):
                tags = [str(x) for x in raw_tags if isinstance(x, (str, int))]
            payload = {
                "youtube_id": vid,
                "title": title,
                "creator": item.get("Creator") or item.get("creator") or "",
                "description": item.get("description") or "",
                "tags": tags,
                "subtitle_storage_key": storage_key,
            }
            resp = http_post_json(sub_url, payload, headers=headers)
            sid = resp.get("submission_id")
            status = resp.get("status")
            if sid:
                submitted_count += 1
            _print(f"Submitted {vid}: submission_id={sid} status={status}")
        except urllib.error.HTTPError as he:
            if he.code == 403:
                _print(
                    "ERROR: submission rejected with 403 Forbidden — your ingest token was not accepted by bonjwa.tv.\n"
                    "Please check the Ingest token and bonjwa.tv URL in the GUI settings and try again."
                )
            else:
                _print(f"ERROR: submission failed for {vid}: {he}")
            ok_all = False
            continue

    _print(
        f"SUMMARY: submitted={submitted_count} skipped_identical={skipped_identical} skipped_invalid={skipped_invalid}"
    )
    return ok_all


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--catalog-base", required=True)
    ap.add_argument("--api-key", required=True)
    ap.add_argument("--videos-json", required=True)
    ap.add_argument("--subtitles-dir", required=True)
    args = ap.parse_args()
    ok = run(args.catalog_base, args.api_key, args.videos_json, args.subtitles_dir)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

import json
import logging
import os
import re
from urllib.parse import parse_qs, urlparse


def extract_video_id(url: str) -> str:
    """Extract a YouTube video ID from a variety of URL formats."""
    try:
        p = urlparse(url.strip())
    except Exception:
        return None
    if not p.scheme:
        return None
    host = p.netloc.lower()
    path = p.path
    if "youtube.com" in host:
        if path == "/watch":
            vid = parse_qs(p.query).get("v", [None])[0]
            return vid
        # shorts or other /<type>/<id>
        m = re.match(r"^/(?:shorts|live)/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
        # sometimes /embed/<id>
        m = re.match(r"^/embed/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
    if "youtu.be" in host:
        m = re.match(r"^/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
    return None


def run_read_youtube_urls(urls_file: str, output: str) -> bool:
    """Parse a URLs .txt file and write a minimal videos.json with IDs and URLs."""
    try:
        items = []
        seen = set()
        with open(urls_file, encoding="utf-8") as f:
            for raw_line in f:
                line = raw_line.strip()
                if not line or line.startswith("#"):
                    continue
                vid = extract_video_id(line)
                if not vid:
                    logging.warning(
                        "Could not extract video ID from URL: %s", line
                    )
                    continue
                if vid in seen:
                    logging.warning("Duplicate video ID found: %s", vid)
                    continue
                seen.add(vid)
                items.append({"v": vid, "youtube_url": line})
        os.makedirs(os.path.dirname(output), exist_ok=True)
        with open(output, "w", encoding="utf-8") as f:
            json.dump(items, f, ensure_ascii=False, indent=2)
        return True
    except Exception as e:
        logging.error("Error in read_youtube_urls: %s", e)
        return False

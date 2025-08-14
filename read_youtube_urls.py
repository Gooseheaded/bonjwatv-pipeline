#!/usr/bin/env python3
import argparse
import json
import os
import re
from urllib.parse import urlparse, parse_qs


def extract_video_id(url: str) -> str:
    try:
        p = urlparse(url.strip())
    except Exception:
        return None
    if not p.scheme:
        return None
    host = p.netloc.lower()
    path = p.path
    if 'youtube.com' in host:
        if path == '/watch':
            vid = parse_qs(p.query).get('v', [None])[0]
            return vid
        # shorts or other /<type>/<id>
        m = re.match(r"^/(?:shorts|live)/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
        # sometimes /embed/<id>
        m = re.match(r"^/embed/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
    if 'youtu.be' in host:
        m = re.match(r"^/([A-Za-z0-9_-]{6,})", path)
        if m:
            return m.group(1)
    return None


def read_youtube_urls(urls_file: str, output: str) -> None:
    items = []
    seen = set()
    with open(urls_file, encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            vid = extract_video_id(line)
            if not vid:
                continue
            if vid in seen:
                continue
            seen.add(vid)
            items.append({'v': vid, 'youtube_url': line})
    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, 'w', encoding='utf-8') as f:
        json.dump(items, f, ensure_ascii=False, indent=2)


def main():
    p = argparse.ArgumentParser(description='Parse a text file of YouTube URLs into videos.json')
    p.add_argument('--urls-file', required=True)
    p.add_argument('--output', required=True)
    args = p.parse_args()
    read_youtube_urls(args.urls_file, args.output)


if __name__ == '__main__':
    main()


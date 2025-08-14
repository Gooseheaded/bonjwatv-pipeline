#!/usr/bin/env python3
import argparse
import json
import os


def read_details(metadata_dir: str, vid: str) -> dict:
    path = os.path.join(metadata_dir, f"{vid}.json")
    if os.path.exists(path):
        return json.load(open(path, encoding='utf-8'))
    return {}


def read_title_cache(cache_dir: str, vid: str) -> dict:
    path = os.path.join(cache_dir, f"title_{vid}.json")
    if os.path.exists(path):
        return json.load(open(path, encoding='utf-8'))
    return {}


def build_videos_json(video_list_file: str, metadata_dir: str, cache_dir: str, output: str = None) -> None:
    output = output or video_list_file
    items = json.load(open(video_list_file, encoding='utf-8'))
    enriched = []
    for item in items:
        vid = item.get('v')
        if not vid:
            continue
        details = read_details(metadata_dir, vid)
        title_cache = read_title_cache(cache_dir, vid)
        # derive fields
        creator = details.get('uploader') or details.get('channel') or details.get('creator') or ''
        en_title = item.get('EN Title') or item.get('title_en') or title_cache.get('title_en') or details.get('title') or ''
        # merge
        merged = dict(item)
        if en_title:
            merged['EN Title'] = en_title
        if creator:
            merged['Creator'] = creator
        enriched.append(merged)

    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, 'w', encoding='utf-8') as f:
        json.dump(enriched, f, ensure_ascii=False, indent=2)


def main():
    p = argparse.ArgumentParser(description='Build or enrich videos.json from metadata and caches')
    p.add_argument('--video-list-file', required=True)
    p.add_argument('--metadata-dir', required=True)
    p.add_argument('--cache-dir', default='.cache')
    p.add_argument('--output')
    args = p.parse_args()
    build_videos_json(args.video_list_file, args.metadata_dir, args.cache_dir, output=args.output)


if __name__ == '__main__':
    main()


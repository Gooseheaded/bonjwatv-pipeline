#!/usr/bin/env python3
import os
import json
import argparse


def build_manifest(video_list_file: str, subtitles_dir: str, details_dir: str, output_file: str) -> None:
    # Load metadata
    with open(video_list_file, encoding='utf-8') as f:
        videos = json.load(f)
    video_map = {v['v']: v for v in videos}

    # Find translated subtitles
    entries = []
    for fname in sorted(os.listdir(subtitles_dir)):
        if not fname.startswith('en_') or not fname.endswith('.srt'):
            continue
        vid = fname[len('en_'):-len('.srt')]
        meta = video_map.get(vid)
        if not meta:
            continue

        # Load detailed metadata
        details_file = os.path.join(details_dir, f"{vid}.json")
        details = {}
        if os.path.exists(details_file):
            with open(details_file, encoding='utf-8') as f:
                details = json.load(f)

        entries.append({
            'v': vid,
            # support sheet-exported keys "EN Title" and "Creator"
            'title': meta.get('EN Title', meta.get('title_en', '')),
            'description': meta.get('description', meta.get('Description', '')),
            'creator': meta.get('Creator', meta.get('creator', '')),
            # support sheet-exported key "EN Subtitles"
            'subtitleUrl': meta.get('EN Subtitles', meta.get('subtitleUrl', '')),
            'releaseDate': details.get('upload_date'),
            'tags': meta.get('tags', meta.get('Tags', [])),
        })

    os.makedirs(os.path.dirname(output_file), exist_ok=True)
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(entries, f, ensure_ascii=False, indent=2)


def main():
    p = argparse.ArgumentParser(description='Build subtitles.json manifest for bonjwa.tv')
    p.add_argument('--video-list-file', required=True, help='Path to metadata/videos.json')
    p.add_argument('--subtitles-dir', required=True, help='Path to translated subtitles directory')
    p.add_argument('--details-dir', required=True, help='Path to detailed video metadata directory')
    p.add_argument('--output-file', required=True, help='Path to write subtitles.json')
    args = p.parse_args()

    build_manifest(args.video_list_file, args.subtitles_dir, args.details_dir, args.output_file)


if __name__ == '__main__':
    main()
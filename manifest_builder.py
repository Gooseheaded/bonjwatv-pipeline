#!/usr/bin/env python3
import os
import json
import argparse


def build_manifest(metadata_file: str, subtitles_dir: str, output_file: str) -> None:
    # Load metadata
    with open(metadata_file, encoding='utf-8') as f:
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
        entries.append({
            'v': vid,
            'title': meta.get('title_en', ''),
            'description': meta.get('description', ''),
            'creator': meta.get('creator', ''),
            'subtitleUrl': meta.get('subtitleUrl', ''),
            'tags': meta.get('tags', []),
        })

    os.makedirs(os.path.dirname(output_file), exist_ok=True)
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(entries, f, ensure_ascii=False, indent=2)


def main():
    p = argparse.ArgumentParser(description='Build subtitles.json manifest for bonjwa.tv')
    p.add_argument('--metadata-file', required=True, help='Path to metadata/videos.json')
    p.add_argument('--subtitles-dir', required=True, help='Path to translated subtitles directory')
    p.add_argument('--output-file', required=True, help='Path to write subtitles.json')
    args = p.parse_args()

    build_manifest(args.metadata_file, args.subtitles_dir, args.output_file)


if __name__ == '__main__':
    main()
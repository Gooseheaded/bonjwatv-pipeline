#!/usr/bin/env python3
"""Scaffold a quick test submission for the translator GUI or submit script.

Creates:
- <base>/urls.txt with one YouTube URL for the given video ID
- <base>/urls/subtitles/en_<videoId>.srt with sample content
- <base>/urls/videos_enriched.json with a minimal entry

Usage:
  # Single video quick start
  python translator/scaffold_test_submission.py \
    --video-id abc123 \
    --title "Test Video" \
    --base-dir metadata

  # Or generate for all IDs in an existing urls.txt
  python translator/scaffold_test_submission.py \
    --from-urls metadata/urls.txt \
    --base-dir metadata
"""

import argparse
import json
import os
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--video-id", help="YouTube video ID (single)")
    ap.add_argument("--title", default="Test Video", help="Video title (single)")
    ap.add_argument("--base-dir", default="metadata", help="Base directory for scaffold")
    ap.add_argument("--from-urls", help="Path to urls.txt; scaffold SRTs/enriched for all IDs found")
    args = ap.parse_args()

    base = Path(args.base_dir).resolve()
    base.mkdir(parents=True, exist_ok=True)

    # Helper: extract ID from typical youtube URL forms
    def extract_id(url: str) -> str | None:
        url = url.strip()
        if not url:
            return None
        if "watch?v=" in url:
            part = url.split("watch?v=", 1)[1]
            return part.split("&", 1)[0]
        if "/shorts/" in url:
            part = url.split("/shorts/", 1)[1]
            return part.split("?", 1)[0]
        if "youtu.be/" in url:
            part = url.split("youtu.be/", 1)[1]
            return part.split("?", 1)[0]
        # Fallback: assume it's already an ID
        if len(url) <= 20 and all(c.isalnum() or c in ("-", "_") for c in url):
            return url
        return None

    # Determine IDs
    ids: list[str] = []
    urls_txt = base / "urls.txt"
    if args.from_urls:
        urls_path = Path(args.from_urls)
        lines = urls_path.read_text(encoding="utf-8").splitlines()
        for ln in lines:
            vid = extract_id(ln)
            if vid:
                ids.append(vid)
        # Ensure the GUI sees the file in the base dir
        if urls_path.resolve() != urls_txt:
            urls_txt.write_text("\n".join(lines) + "\n", encoding="utf-8")
    elif args.video_id:
        ids = [args.video_id]
        urls_txt.write_text(f"https://www.youtube.com/watch?v={args.video_id}\n", encoding="utf-8")
    else:
        # Default to abc123 if nothing provided
        ids = ["abc123"]
        urls_txt.write_text("https://www.youtube.com/watch?v=abc123\n", encoding="utf-8")

    run_root = base / "urls"
    subs_dir = run_root / "subtitles"
    subs_dir.mkdir(parents=True, exist_ok=True)

    # Create SRT stubs for each ID if missing
    srt_body = "1\n00:00:01,000 --> 00:00:02,000\nHello!\n"
    for vid in ids:
        (subs_dir / f"en_{vid}.srt").write_text(srt_body, encoding="utf-8")

    # Merge/update enriched videos list
    enriched = run_root / "videos_enriched.json"
    existing: list[dict] = []
    if enriched.exists():
        try:
            existing = json.loads(enriched.read_text(encoding="utf-8"))
        except Exception:
            existing = []
    by_id: dict[str, dict] = {str(x.get("v")): x for x in existing if isinstance(x, dict) and "v" in x}
    for idx, vid in enumerate(ids):
        title = args.title if (len(ids) == 1 and args.title) else f"Test Video {vid}"
        by_id[vid] = {"v": vid, "title": title, "tags": ["z"]}
    enriched.write_text(json.dumps(list(by_id.values()), ensure_ascii=False, indent=2), encoding="utf-8")

    print("Scaffold complete:\n")
    print(f"- URLs file:        {urls_txt}")
    print(f"- Run root:         {run_root}")
    print(f"- Subtitles dir:    {subs_dir}")
    print(f"- Enriched videos:  {enriched}")
    print("\nUse with GUI:")
    print(f"  python -m translator.gui.app  # then select: {urls_txt}")
    print("\nOr submit directly:")
    print(
        "  python translator/submit_to_catalog.py --catalog-base http://localhost:5002 "
        "--api-key <TOKEN> --videos-json {enriched} --subtitles-dir {subs_dir}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

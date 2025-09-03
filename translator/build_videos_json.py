import json
import logging  # Added for logging
import os
from typing import Optional


def read_details(metadata_dir: str, vid: str) -> dict:
    """Read cached video details JSON for a given video ID.

    Returns an empty dict if the file does not exist.
    """
    path = os.path.join(metadata_dir, f"{vid}.json")
    if os.path.exists(path):
        return json.load(open(path, encoding="utf-8"))
    return {}


def read_title_cache(cache_dir: str, vid: str) -> dict:
    """Read cached English title JSON for a given video ID.

    Returns an empty dict if the file does not exist.
    """
    path = os.path.join(cache_dir, f"title_{vid}.json")
    if os.path.exists(path):
        return json.load(open(path, encoding="utf-8"))
    return {}


def run_build_videos_json(
    video_list_file: str,
    metadata_dir: str,
    cache_dir: str,
    output: Optional[str] = None,
) -> bool:
    """Build an enriched videos.json from minimal inputs and caches.

    - Reads the minimal list from ``video_list_file`` containing at least ``{"v": id}``.
    - Enriches with Creator (from metadata) and EN Title (from cache/metadata).
    - Writes to ``output`` or ``videos_enriched.json`` alongside the input file.
    """
    try:
        # By default, write an enriched file separate from the source list
        if output is None:
            base_dir = os.path.dirname(os.path.abspath(video_list_file))
            output = os.path.join(base_dir, "videos_enriched.json")

        if not os.path.exists(video_list_file):
            logging.error(f"Video list file not found: {video_list_file}")
            return False

        items = json.load(open(video_list_file, encoding="utf-8"))
        enriched = []
        for item in items:
            vid = item.get("v")
            if not vid:
                continue
            details = read_details(metadata_dir, vid)
            title_cache = read_title_cache(cache_dir, vid)
            # derive fields
            creator = (
                details.get("uploader")
                or details.get("channel")
                or details.get("creator")
                or ""
            )
            en_title = (
                item.get("EN Title")
                or item.get("title_en")
                or title_cache.get("title_en")
                or details.get("title")
                or ""
            )
            # merge
            merged = dict(item)
            if en_title:
                merged["EN Title"] = en_title
            if creator:
                merged["Creator"] = creator
            enriched.append(merged)

        os.makedirs(os.path.dirname(output), exist_ok=True)
        with open(output, "w", encoding="utf-8") as f:
            json.dump(enriched, f, ensure_ascii=False, indent=2)
        logging.info(f"Videos JSON built and saved to {output}")
        return True
    except Exception as e:
        logging.error(f"Failed to build videos JSON: {e}")
        return False

import json
import logging  # Added for logging
import os


def run_manifest_builder(
    video_list_file: str, subtitles_dir: str, details_dir: str, output_file: str
) -> bool:
    """Build the website manifest (subtitles.json) from available EN SRTs.

    Includes only videos that have translated English subtitles.
    """
    try:
        # Load metadata
        if not os.path.exists(video_list_file):
            logging.error(
                f"Video list file not found for manifest builder: {video_list_file}"
            )
            return False
        with open(video_list_file, encoding="utf-8") as f:
            videos = json.load(f)
        video_map = {v["v"]: v for v in videos}

        # Find translated subtitles
        entries = []
        if not os.path.exists(subtitles_dir):
            logging.warning(
                f"Subtitles directory not found: {subtitles_dir}. Skipping manifest build."
            )
            # This is not a fatal error, so we return True
            return True

        for fname in sorted(os.listdir(subtitles_dir)):
            if not fname.startswith("en_") or not fname.endswith(".srt"):
                continue
            vid = fname[len("en_") : -len(".srt")]
            meta = video_map.get(vid)
            if not meta:
                continue

            # Load detailed metadata
            details_file = os.path.join(details_dir, f"{vid}.json")
            details = {}
            if os.path.exists(details_file):
                with open(details_file, encoding="utf-8") as f:
                    details = json.load(f)

            entries.append(
                {
                    "v": vid,
                    # support sheet-exported keys "EN Title" and "Creator"
                    "title": meta.get("EN Title", meta.get("title_en", "")),
                    "description": meta.get("description", meta.get("Description", "")),
                    "creator": meta.get("Creator", meta.get("creator", "")),
                    # support sheet-exported key "EN Subtitles"
                    "subtitleUrl": meta.get(
                        "EN Subtitles", meta.get("subtitleUrl", "")
                    ),
                    "releaseDate": details.get("upload_date"),
                    "tags": meta.get("tags", meta.get("Tags", [])),
                }
            )

        os.makedirs(os.path.dirname(output_file), exist_ok=True)
        with open(output_file, "w", encoding="utf-8") as f:
            json.dump(entries, f, ensure_ascii=False, indent=2)
        logging.info(f"Manifest built and saved to {output_file}")
        return True
    except Exception as e:
        logging.error(f"Manifest build failed: {e}")
        return False

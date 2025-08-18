import json
import logging
import os

import yt_dlp


def run_fetch_video_metadata(video_id: str, output_dir: str) -> bool:
    """Fetch video metadata from YouTube and save it to a JSON file."""
    output_file = os.path.join(output_dir, f"{video_id}.json")

    if os.path.exists(output_file):
        logging.info(f"Metadata for {video_id} already exists. Skipping.")
        return True

    ydl_opts = {
        "quiet": True,
        "no_warnings": True,
        "skip_download": True,
    }

    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info = ydl.extract_info(
                f"https://www.youtube.com/watch?v={video_id}", download=False
            )

            # Ensure the output directory exists
            os.makedirs(output_dir, exist_ok=True)

            with open(output_file, "w") as f:
                json.dump(info, f)

            logging.info(f"Successfully fetched and saved metadata for {video_id}")
            return True
    except Exception as e:
        logging.error(f"Error fetching metadata for {video_id}: {e}")
        return False

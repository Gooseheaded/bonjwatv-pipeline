
import yt_dlp
import json
import os
import argparse

def fetch_video_metadata(video_id, output_dir):
    """Fetches video metadata from YouTube and saves it to a JSON file."""
    output_file = os.path.join(output_dir, f"{video_id}.json")

    if os.path.exists(output_file):
        print(f"Metadata for {video_id} already exists. Skipping.")
        return

    ydl_opts = {
        'quiet': True,
        'no_warnings': True,
        'skip_download': True,
    }

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        info = ydl.extract_info(f"https://www.youtube.com/watch?v={video_id}", download=False)
        
        # Ensure the output directory exists
        os.makedirs(output_dir, exist_ok=True)

        with open(output_file, 'w') as f:
            json.dump(info, f)

        print(f"Successfully fetched and saved metadata for {video_id}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Fetch video metadata from YouTube.")
    parser.add_argument("--video-id", required=True, help="The YouTube video ID.")
    # Accept both for backward compatibility; prefer --output-dir to match orchestrator and docs
    parser.add_argument("--output-dir", dest="output_dir", required=False, help="Directory to save the metadata JSON.")
    parser.add_argument("--video-metadata-dir", dest="output_dir", required=False, help="(Deprecated) Same as --output-dir.")
    args = parser.parse_args()

    if not args.output_dir:
        parser.error("--output-dir is required")

    fetch_video_metadata(args.video_id, args.output_dir)

import logging  # Keep logging for internal use
import os

import yt_dlp


def run_download_audio(url: str, video_id: str, output_dir: str = "audio") -> bool:
    """Download audio from a URL and save it to the output directory."""
    os.makedirs(output_dir, exist_ok=True)
    # Final expected path after extraction
    final_path = os.path.join(output_dir, f"{video_id}.mp3")
    if os.path.exists(final_path):
        logging.info(f"{final_path} already exists, skipping download")
        return True

    # Use a template without a fixed extension; the postprocessor will write .mp3
    ydl_opts = {
        "format": "bestaudio",
        "outtmpl": os.path.join(output_dir, f"{video_id}.%(ext)s"),
        "postprocessors": [
            {
                "key": "FFmpegExtractAudio",
                "preferredcodec": "mp3",
                "preferredquality": "192",
            }
        ],
        "quiet": True,  # Suppress console output from yt-dlp
        "no_warnings": True,
    }
    try:
        ydl = yt_dlp.YoutubeDL(ydl_opts)
        ydl.download([url])

        logging.info(f"Downloaded audio to {final_path}")
        return True
    except Exception as e:
        logging.error(f"Error downloading audio for {video_id}: {e}")
        return False

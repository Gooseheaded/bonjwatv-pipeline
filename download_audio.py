#!/usr/bin/env python3
import os
import argparse
import logging

import yt_dlp


def download_audio(url: str, video_id: str, output_dir: str = 'audio') -> str:
    os.makedirs(output_dir, exist_ok=True)
    output_path = os.path.join(output_dir, f"{video_id}.mp3")
    if os.path.exists(output_path):
        logging.info(f"{output_path} already exists, skipping download")
        return output_path

    ydl_opts = {
        'format': 'bestaudio',
        'outtmpl': output_path,
        'postprocessors': [{
            'key': 'FFmpegExtractAudio',
            'preferredcodec': 'mp3',
            'preferredquality': '192',
        }],
    }
    ydl = yt_dlp.YoutubeDL(ydl_opts)
    ydl.download([url])

    logging.info(f"Downloaded audio to {output_path}")
    return output_path


def setup_logging():
    os.makedirs('logs', exist_ok=True)
    handler = logging.FileHandler('logs/download_audio.log', encoding='utf-8')
    fmt = logging.Formatter('%(asctime)s %(levelname)s %(message)s')
    handler.setFormatter(fmt)
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)
    console = logging.StreamHandler()
    console.setFormatter(fmt)
    root.addHandler(console)
    return root


def main():
    log = setup_logging()
    p = argparse.ArgumentParser(description='Download YouTube audio as MP3')
    p.add_argument('--url', required=True, help='YouTube video URL')
    p.add_argument('--video-id', required=True, help='Video identifier for output filename')
    p.add_argument('--output-dir', default='audio', help='Directory to save audio files')
    args = p.parse_args()

    try:
        download_audio(args.url, args.video_id, args.output_dir)
    except Exception as e:
        log.error(f"Audio download failed: {e}")
        exit(1)


if __name__ == '__main__':
    main()
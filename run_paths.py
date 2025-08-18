#!/usr/bin/env python3
import os


def compute_run_paths(urls_file: str) -> dict:
    """Compute standard run directories from a URLs file path.

    Derives a per-run root directory named after the URLs file (without extension)
    alongside the .txt, and computes subdirectories used by the pipeline.

    Returns a dict with keys:
      - run_root
      - video_list_file (run_root/videos.json)
      - audio_dir (run_root/audio)
      - vocals_dir (run_root/vocals)
      - subtitles_dir (run_root/subtitles)
      - cache_dir (run_root/.cache)
    """
    urls_dir = os.path.dirname(os.path.abspath(urls_file))
    stem = os.path.splitext(os.path.basename(urls_file))[0]
    run_root = os.path.join(urls_dir, stem)
    return {
        "run_root": run_root,
        "video_list_file": os.path.join(run_root, "videos.json"),
        "audio_dir": os.path.join(run_root, "audio"),
        "vocals_dir": os.path.join(run_root, "vocals"),
        "subtitles_dir": os.path.join(run_root, "subtitles"),
        "cache_dir": os.path.join(run_root, ".cache"),
    }

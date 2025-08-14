import json
import os
import subprocess
import sys


def test_orchestrator_mutually_exclusive_sources(tmp_path):
    cfg = {
        "video_list_file": str(tmp_path / 'videos.json'),
        "video_metadata_dir": str(tmp_path / 'metadata'),
        "audio_dir": str(tmp_path / 'audio'),
        "vocals_dir": str(tmp_path / 'vocals'),
        "subtitles_dir": str(tmp_path / 'subtitles'),
        "cache_dir": str(tmp_path / '.cache'),
        "slang_file": str(tmp_path / 'slang.txt'),
        "website_dir": str(tmp_path / 'website'),
        "urls_file": str(tmp_path / 'urls.txt'),
        "steps": [
            "read_youtube_urls",
            "google_sheet_read",
            "manifest_builder"
        ],
        "spreadsheet": "X",
        "worksheet": "Y",
        "sheet_column": "Pastebin URL",
        "service_account_file": "path/to/sa.json",
    }
    (tmp_path / 'urls.txt').write_text('https://youtu.be/abc', encoding='utf-8')
    cfg_path = tmp_path / 'config.json'
    cfg_path.write_text(json.dumps(cfg), encoding='utf-8')

    proc = subprocess.run([sys.executable, 'pipeline_orchestrator.py', '--config', str(cfg_path)])
    assert proc.returncode != 0


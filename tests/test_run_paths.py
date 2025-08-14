import os
import sys

sys.path.insert(0, os.getcwd())

from run_paths import compute_run_paths


def test_compute_run_paths(tmp_path):
    urls = tmp_path / 'mybatch.txt'
    urls.write_text('https://youtu.be/abc', encoding='utf-8')
    paths = compute_run_paths(str(urls))
    assert os.path.basename(paths['run_root']) == 'mybatch'
    assert paths['video_list_file'].endswith(os.path.join('mybatch', 'videos.json'))
    assert paths['audio_dir'].endswith(os.path.join('mybatch', 'audio'))
    assert paths['vocals_dir'].endswith(os.path.join('mybatch', 'vocals'))
    assert paths['subtitles_dir'].endswith(os.path.join('mybatch', 'subtitles'))
    assert paths['cache_dir'].endswith(os.path.join('mybatch', '.cache'))


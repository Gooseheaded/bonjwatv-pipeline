import os
import sys
import json

sys.path.insert(0, os.getcwd())

import pytest

from upload_subtitles import upload_subtitles


def test_upload_subtitles(tmp_path, monkeypatch, caplog):
    # Prepare a dummy translated SRT file
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('line1\nline2', encoding='utf-8')
    cache_dir = tmp_path / '.cache'

    # Simulate Pastebin API response
    class DummyResponse:
        status_code = 200
        text = 'ABC123'

    def fake_post(url, data):
        # Validate API endpoint and parameters
        assert url == 'https://pastebin.com/api/api_post.php'
        assert data['api_dev_key'] == 'test-key'
        assert data['api_option'] == 'paste'
        assert data['api_paste_code'] == input_srt.read_text(encoding='utf-8')
        assert data['api_paste_name'] == 'vid'
        assert data['api_paste_private'] == '1'
        assert data['api_paste_expire_date'] == 'N'
        assert data['api_paste_format'] == ''
        assert data['api_paste_folder'] == 'BWKT'
        return DummyResponse()

    monkeypatch.setenv('PASTEBIN_API_KEY', 'test-key')
    monkeypatch.setenv('PASTEBIN_FOLDER', 'BWKT')
    monkeypatch.setattr('upload_subtitles.requests.post', fake_post)
    caplog.set_level('INFO')

    url = upload_subtitles(str(input_srt), str(cache_dir))
    # Expect raw paste URL
    assert url == 'https://pastebin.com/raw/ABC123'

    # Cache file should have been written
    cache_file = cache_dir / 'pastebin_vid.json'
    assert cache_file.exists()
    data = json.loads(cache_file.read_text(encoding='utf-8'))
    assert data['paste_id'] == 'ABC123'
    assert data['url'] == url


def test_skip_cached(tmp_path, caplog):
    # Prepare dummy SRT and existing cache
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('x', encoding='utf-8')
    cache_dir = tmp_path / '.cache'
    cache_dir.mkdir()
    cache_file = cache_dir / 'pastebin_vid.json'
    cache_file.write_text(json.dumps({'paste_id': 'XYZ', 'url': 'http://cached'}), encoding='utf-8')

    caplog.set_level('INFO')
    url = upload_subtitles(str(input_srt), str(cache_dir))
    assert url == 'http://cached'
    assert 'Using cached Pastebin URL' in caplog.text
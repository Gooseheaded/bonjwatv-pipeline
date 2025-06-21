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

    # Prevent accidental login via .env credentials
    monkeypatch.delenv('PASTEBIN_USER_KEY', raising=False)
    monkeypatch.delenv('PASTEBIN_USERNAME', raising=False)
    monkeypatch.delenv('PASTEBIN_PASSWORD', raising=False)
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

def test_user_key_env(tmp_path, monkeypatch, caplog):
    # If PASTEBIN_USER_KEY is set, no login should occur and user_key is passed through
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('x', encoding='utf-8')
    cache_dir = tmp_path / '.cache'

    # Prevent accidental login via .env credentials
    monkeypatch.delenv('PASTEBIN_USERNAME', raising=False)
    monkeypatch.delenv('PASTEBIN_PASSWORD', raising=False)
    monkeypatch.setenv('PASTEBIN_API_KEY', 'test-key')
    monkeypatch.setenv('PASTEBIN_FOLDER', 'BWKT')
    monkeypatch.setenv('PASTEBIN_USER_KEY', 'USERKEY123')

    class DummyResponse:
        def __init__(self, status_code, text):
            self.status_code = status_code
            self.text = text

    def fake_post(url, data):
        # login endpoint must not be called when user_key is provided
        if url.endswith('/api_login.php'):
            pytest.fail('Login should not be called when PASTEBIN_USER_KEY is set')
        # paste creation endpoint should receive api_user_key
        assert url == 'https://pastebin.com/api/api_post.php'
        assert data.get('api_user_key') == 'USERKEY123'
        return DummyResponse(200, 'ID999')

    monkeypatch.setattr('upload_subtitles.requests.post', fake_post)
    caplog.set_level('INFO')

    url = upload_subtitles(str(input_srt), str(cache_dir))
    assert url == 'https://pastebin.com/raw/ID999'
    # user-key cache file should not be created (env key is authoritative)
    assert not (cache_dir / 'pastebin_user_key.json').exists()

def test_login_and_cache_user_key(tmp_path, monkeypatch, caplog):
    # If credentials are provided, perform login, cache user_key, and reuse it
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('y', encoding='utf-8')
    cache_dir = tmp_path / '.cache'

    # Force login via credentials, clear any cached user key
    monkeypatch.delenv('PASTEBIN_USER_KEY', raising=False)
    monkeypatch.setenv('PASTEBIN_API_KEY', 'devkey')
    monkeypatch.setenv('PASTEBIN_USERNAME', 'user1')
    monkeypatch.setenv('PASTEBIN_PASSWORD', 'pass1')

    calls = []
    class DummyResponse:
        def __init__(self, status_code, text):
            self.status_code = status_code
            self.text = text

    def fake_post(url, data):
        if url.endswith('/api_login.php'):
            # login call
            assert data['api_dev_key'] == 'devkey'
            assert data['api_user_name'] == 'user1'
            assert data['api_user_password'] == 'pass1'
            calls.append('login')
            return DummyResponse(200, 'LOGINKEY')
        if url.endswith('/api_post.php'):
            # paste call should follow login
            assert calls == ['login']
            assert data.get('api_user_key') == 'LOGINKEY'
            calls.append('paste')
            return DummyResponse(200, 'PASTEID')
        pytest.fail(f'Unexpected URL: {url}')

    monkeypatch.setenv('PASTEBIN_FOLDER', '')
    monkeypatch.setattr('upload_subtitles.requests.post', fake_post)
    caplog.set_level('INFO')

    url = upload_subtitles(str(input_srt), str(cache_dir))
    assert url == 'https://pastebin.com/raw/PASTEID'
    # Ensure user_key got cached
    key_file = cache_dir / 'pastebin_user_key.json'
    assert key_file.exists()
    data = json.loads(key_file.read_text(encoding='utf-8'))
    assert data['user_key'] == 'LOGINKEY'

    # Second call should use the per-video cache (no additional HTTP requests)
    calls.clear()
    url2 = upload_subtitles(str(input_srt), str(cache_dir))
    assert url2 == 'https://pastebin.com/raw/PASTEID'
    assert calls == []
    assert 'Using cached Pastebin URL' in caplog.text

def test_api_returns_full_url(tmp_path, monkeypatch, caplog):
    # Simulate API returning a full Pastebin page URL, not just the ID
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('code', encoding='utf-8')
    cache_dir = tmp_path / '.cache'

    class DummyResponse:
        def __init__(self, text):
            self.status_code = 200
            self.text = text

    def fake_post(url, data):
        return DummyResponse('https://pastebin.com/RZRNVxU5')

    # Prevent accidental login via .env credentials
    monkeypatch.delenv('PASTEBIN_USER_KEY', raising=False)
    monkeypatch.delenv('PASTEBIN_USERNAME', raising=False)
    monkeypatch.delenv('PASTEBIN_PASSWORD', raising=False)
    monkeypatch.setenv('PASTEBIN_API_KEY', 'key')
    monkeypatch.setenv('PASTEBIN_FOLDER', '')
    monkeypatch.setattr('upload_subtitles.requests.post', fake_post)
    caplog.set_level('INFO')

    url = upload_subtitles(str(input_srt), str(cache_dir))
    # Should convert to raw URL using the ID from the full URL
    assert url == 'https://pastebin.com/raw/RZRNVxU5'
    data = json.loads((cache_dir / 'pastebin_vid.json').read_text(encoding='utf-8'))
    assert data['paste_id'] == 'RZRNVxU5'

def test_api_returns_raw_url(tmp_path, monkeypatch, caplog):
    # Simulate API returning a raw URL directly
    input_srt = tmp_path / 'en_vid.srt'
    input_srt.write_text('code', encoding='utf-8')
    cache_dir = tmp_path / '.cache'

    class DummyResponse:
        def __init__(self, text):
            self.status_code = 200
            self.text = text

    def fake_post(url, data):
        return DummyResponse('https://pastebin.com/raw/XYZ123')

    # Prevent accidental login via .env credentials
    monkeypatch.delenv('PASTEBIN_USER_KEY', raising=False)
    monkeypatch.delenv('PASTEBIN_USERNAME', raising=False)
    monkeypatch.delenv('PASTEBIN_PASSWORD', raising=False)
    monkeypatch.setenv('PASTEBIN_API_KEY', 'key')
    monkeypatch.setenv('PASTEBIN_FOLDER', '')
    monkeypatch.setattr('upload_subtitles.requests.post', fake_post)
    caplog.set_level('INFO')

    url = upload_subtitles(str(input_srt), str(cache_dir))
    assert url == 'https://pastebin.com/raw/XYZ123'
    data = json.loads((cache_dir / 'pastebin_vid.json').read_text(encoding='utf-8'))
    assert data['paste_id'] == 'XYZ123'
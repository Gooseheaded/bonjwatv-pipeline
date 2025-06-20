import os
import sys

sys.path.insert(0, os.getcwd())

import pytest

from download_audio import download_audio


def test_skip_existing(tmp_path, caplog):
    audio_dir = tmp_path / 'audio'
    audio_dir.mkdir()
    existing = audio_dir / 'vid123.mp3'
    existing.write_text('', encoding='utf-8')
    caplog.set_level('INFO')

    out = download_audio(
        url='http://example.com',
        video_id='vid123',
        output_dir=str(audio_dir)
    )
    assert out == str(existing)
    assert 'already exists' in caplog.text


def test_download_creates_file(tmp_path, monkeypatch):
    audio_dir = tmp_path / 'audio'
    audio_dir.mkdir()

    calls = []
    class DummyYDL:
        def __init__(self, opts):
            pass
        def download(self, urls):
            calls.append(urls)
            # simulate writing mp3 file
            path = audio_dir / 'vid123.mp3'
            path.write_text('dummy', encoding='utf-8')

    monkeypatch.setattr('download_audio.yt_dlp.YoutubeDL', DummyYDL)

    out = download_audio(
        url='http://example.com',
        video_id='vid123',
        output_dir=str(audio_dir)
    )
    assert calls == [['http://example.com']]
    assert os.path.exists(out)
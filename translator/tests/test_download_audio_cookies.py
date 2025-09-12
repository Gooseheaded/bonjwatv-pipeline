import os
import sys

sys.path.insert(0, os.getcwd())


from download_audio import run_download_audio


def test_uses_cookiefile_env(tmp_path, monkeypatch):
    audio_dir = tmp_path / "audio"
    audio_dir.mkdir()
    cookies = tmp_path / "cookies.txt"
    cookies.write_text("# Netscape cookie file", encoding="utf-8")

    captured_opts = {}

    class DummyYDL:
        def __init__(self, opts):
            captured_opts.update(opts)

        def download(self, urls):
            (audio_dir / "vid123.mp3").write_text("ok", encoding="utf-8")

    monkeypatch.setenv("YTDLP_COOKIES", str(cookies))
    monkeypatch.setattr("download_audio.yt_dlp.YoutubeDL", DummyYDL)

    ok = run_download_audio("https://youtu.be/x", "vid123", str(audio_dir))
    assert ok is True
    assert captured_opts.get("cookiefile") == str(cookies)


def test_uses_cookiesfrombrowser_env(tmp_path, monkeypatch):
    audio_dir = tmp_path / "audio"
    audio_dir.mkdir()

    captured_opts = {}

    class DummyYDL:
        def __init__(self, opts):
            captured_opts.update(opts)

        def download(self, urls):
            (audio_dir / "vid123.mp3").write_text("ok", encoding="utf-8")

    monkeypatch.delenv("YTDLP_COOKIES", raising=False)
    monkeypatch.setenv("YTDLP_COOKIES_BROWSER", "chrome")
    monkeypatch.setattr("download_audio.yt_dlp.YoutubeDL", DummyYDL)

    ok = run_download_audio("https://youtu.be/x", "vid123", str(audio_dir))
    assert ok is True
    assert captured_opts.get("cookiesfrombrowser") == ("chrome",)


import json
import os
import sys
from types import SimpleNamespace

sys.path.insert(0, os.getcwd())

import translator.submit_to_catalog as sub


def test_skips_when_missing_and_no_stub(tmp_path, monkeypatch, capsys):
    vids = tmp_path / "videos.json"
    items = [{"v": "abc123", "title": "X", "tags": ["z"]}]
    vids.write_text(json.dumps(items), encoding="utf-8")
    subs_dir = tmp_path / "subs"

    calls = SimpleNamespace(upload=0, submit=0)

    monkeypatch.setattr(sub, "http_post_multipart", lambda *a, **k: {"storage_key": "k"})
    monkeypatch.setattr(sub, "http_post_json", lambda *a, **k: {"submission_id": "s"})

    ok = sub.run("http://api", "TOKEN", str(vids), str(subs_dir))
    captured = capsys.readouterr().out
    assert ok is False
    assert "skipping submission" in captured.lower()


def test_skips_trivial_subs(tmp_path, monkeypatch, capsys):
    vids = tmp_path / "videos.json"
    items = [{"v": "abc123", "title": "X", "tags": ["z"]}]
    vids.write_text(json.dumps(items), encoding="utf-8")
    subs_dir = tmp_path / "subs"
    subs_dir.mkdir()
    # Create trivial stub
    (subs_dir / "en_abc123.srt").write_text(
        "1\n00:00:01,000 --> 00:00:02,000\nHello!\n\n", encoding="utf-8"
    )

    monkeypatch.setattr(sub, "http_post_multipart", lambda *a, **k: {"storage_key": "k"})
    monkeypatch.setattr(sub, "http_post_json", lambda *a, **k: {"submission_id": "s"})

    ok = sub.run("http://api", "TOKEN", str(vids), str(subs_dir))
    captured = capsys.readouterr().out
    assert ok is False
    assert "invalid/trivial" in captured.lower()


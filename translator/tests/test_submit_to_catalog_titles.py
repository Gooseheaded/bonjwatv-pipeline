import json
import sys
import os

sys.path.insert(0, os.getcwd())

import translator.submit_to_catalog as sub


def test_skips_when_no_translated_title(tmp_path, monkeypatch, capsys):
    vids = tmp_path / "videos.json"
    # No EN Title or title_en; only original title
    items = [{"v": "abc123", "title": "원제목"}]
    vids.write_text(json.dumps(items), encoding="utf-8")
    subs_dir = tmp_path / "subs"
    subs_dir.mkdir()
    # Provide a valid English SRT so only title is the blocker
    # Provide a non-trivial SRT (>=3 blocks) so only missing title causes skip
    (subs_dir / "en_abc123.srt").write_text(
        "1\n00:00:01,000 --> 00:00:02,000\nHello world, these are meaningful words for testing.\n\n"
        "2\n00:00:02,000 --> 00:00:03,000\nMore text appears here to exceed size thresholds.\n\n"
        "3\n00:00:03,000 --> 00:00:04,000\nEven more lines to make content sufficiently long.\n\n",
        encoding="utf-8",
    )

    # Mock network calls (should not be reached due to skip)
    monkeypatch.setattr(sub, "http_post_multipart", lambda *a, **k: {"storage_key": "k"})
    monkeypatch.setattr(sub, "http_post_json", lambda *a, **k: {"submission_id": "s"})

    rc = sub.run("http://api", "TOKEN", str(vids), str(subs_dir))
    out = capsys.readouterr().out
    assert rc is False
    assert "no translated english title" in out.lower()

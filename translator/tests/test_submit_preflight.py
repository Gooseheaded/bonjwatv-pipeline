import json
import sys
from types import SimpleNamespace

sys.path.insert(0, '.')

import translator.submit_to_catalog as sub


def test_preflight_skips_identical(tmp_path, monkeypatch, capsys):
    vids = tmp_path / "videos.json"
    items = [
        {"v": "same1", "title": "X"},
        {"v": "diff1", "title": "Y"},
    ]
    vids.write_text(json.dumps(items), encoding="utf-8")
    subs = tmp_path / "subs"
    subs.mkdir()
    # same1: local content A
    a = (subs / "en_same1.srt")
    a.write_text("1\n00:00:01,000 --> 00:00:02,000\nHello A\n\n", encoding="utf-8")
    # diff1: local content B
    b = (subs / "en_diff1.srt")
    b.write_text("1\n00:00:01,000 --> 00:00:02,000\nHello B\n\n", encoding="utf-8")

    # Hash of A
    import hashlib
    hA = hashlib.sha256(a.read_bytes()).hexdigest()

    # Monkeypatch preflight and network posts
    monkeypatch.setattr(sub, "http_post_multipart", lambda *a, **k: {"storage_key": "k"})
    calls = SimpleNamespace(upload=0, submit=0)
    def fake_post_json(url, body, headers=None):
        calls.submit += 1
        return {"submission_id": "s"}
    monkeypatch.setattr(sub, "http_post_json", fake_post_json)

    # Force _fetch_hashes to return same for same1, null for diff1
    def fake_fetch(ids):
        return {"same1": hA, "diff1": None}
    monkeypatch.setattr(sub, "_reconstruct_en_from_cache", lambda *a, **k: None)
    # Monkeypatch the internal function via attribute injection on module
    # We can't directly set the nested function; simulate by providing remote_hashes through an outer layer by replacing run
    # Instead, mock urllib and rely on function implementation reading remote hashes before loop
    # Simpler: override module-level function via setattr
    # But _fetch_hashes is inner; so we will simulate by temporarily replacing urllib.request.urlopen to return our payload
    import urllib.request
    class FakeResp:
        def __init__(self, data): self._d = data
        def __enter__(self): return self
        def __exit__(self, *a): return False
        def read(self): return json.dumps(self._d).encode('utf-8')
    def fake_urlopen(url):
        return FakeResp({"same1": hA, "diff1": None})
    monkeypatch.setattr(urllib.request, "urlopen", fake_urlopen)

    ok = sub.run("http://api", "TOKEN", str(vids), str(subs))
    out = capsys.readouterr().out
    assert ok is True
    # Upload/submit should be called only once (for diff1)
    assert "skipping same1" in out.lower()


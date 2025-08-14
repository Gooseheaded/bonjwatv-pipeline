import os
import sys
import json

sys.path.insert(0, os.getcwd())

import pytest

import translate_title as tt


@pytest.fixture
def fake_openai(monkeypatch):
    def fake_call(title, model):
        return 'Translated: ' + title
    monkeypatch.setattr(tt, 'call_openai_translate', fake_call)


def test_translate_title_caches(tmp_path, monkeypatch, fake_openai):
    meta_dir = tmp_path / 'metadata'
    cache_dir = tmp_path / '.cache'
    meta_dir.mkdir()
    cache_dir.mkdir()
    vid = 'abc123'
    meta = {'title': '한글 제목'}
    (meta_dir / f'{vid}.json').write_text(json.dumps(meta), encoding='utf-8')

    # First call writes cache
    out = tt.translate_title(vid, str(meta_dir), str(cache_dir), model='dummy')
    assert out.startswith('Translated: ')
    cache = json.loads((cache_dir / f'title_{vid}.json').read_text(encoding='utf-8'))
    assert cache['title_en'] == out
    assert 'source_hash' in cache

    # Second call with same source uses cache, not calling API (same output)
    out2 = tt.translate_title(vid, str(meta_dir), str(cache_dir), model='dummy')
    assert out2 == out


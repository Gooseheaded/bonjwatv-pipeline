import os
import sys
import json

sys.path.insert(0, os.getcwd())

import pytest

from manifest_builder import build_manifest


def test_build_manifest(tmp_path):
    # Prepare metadata/videos.json
    metadata = [
        {"v": "vid1", "title_en": "T1", "description": "D1", "creator": "C1", "subtitleUrl": "url1", "tags": ["a"]},
        {"v": "vid2", "title_en": "T2", "description": "D2", "creator": "C2", "subtitleUrl": "url2", "tags": ["b"]},
    ]
    meta_dir = tmp_path / 'metadata'
    meta_dir.mkdir()
    videos_json = meta_dir / 'videos.json'
    videos_json.write_text(json.dumps(metadata), encoding='utf-8')

    # Create subtitles folder with only vid1 translated
    subs_dir = tmp_path / 'subtitles'
    subs_dir.mkdir()
    (subs_dir / 'en_vid1.srt').write_text('', encoding='utf-8')

    # Output manifest file
    out_dir = tmp_path / 'website'
    out_dir.mkdir()
    out_file = out_dir / 'subtitles.json'

    build_manifest(
        metadata_file=str(videos_json),
        subtitles_dir=str(subs_dir),
        output_file=str(out_file)
    )

    assert out_file.exists()
    data = json.loads(out_file.read_text(encoding='utf-8'))
    # Only vid1 should be included
    assert isinstance(data, list)
    assert len(data) == 1
    entry = data[0]
    assert entry['v'] == 'vid1'
    assert entry['title'] == 'T1'
    assert entry['subtitleUrl'] == 'url1'
    assert entry['tags'] == ['a']

def test_build_manifest_sheet_keys(tmp_path):
    # metadata with sheet-exported column names
    metadata = [
        {
            'v': 'vidX',
            'EN Title': 'TX',
            'Description': 'DescX',
            'Creator': 'CX',
            'EN Subtitles': 'urlX',
            'Tags': ['x'],
        }
    ]
    videos_json = tmp_path / 'videos2.json'
    videos_json.write_text(json.dumps(metadata), encoding='utf-8')

    subs_dir = tmp_path / 'subs'
    subs_dir.mkdir()
    (subs_dir / 'en_vidX.srt').write_text('', encoding='utf-8')

    out = tmp_path / 'out.json'
    build_manifest(str(videos_json), str(subs_dir), str(out))
    data2 = json.loads(out.read_text(encoding='utf-8'))
    assert len(data2) == 1
    e = data2[0]
    assert e['title'] == 'TX'
    assert e['description'] == 'DescX'
    assert e['creator'] == 'CX'
    assert e['subtitleUrl'] == 'urlX'
    assert e['tags'] == ['x']
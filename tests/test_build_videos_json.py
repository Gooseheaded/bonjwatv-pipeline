import os
import sys
import json

sys.path.insert(0, os.getcwd())

from build_videos_json import build_videos_json


def test_build_videos_json_enriches_fields(tmp_path):
    vids = [
        {'v': 'id1', 'youtube_url': 'https://youtu.be/id1'},
        {'v': 'id2', 'youtube_url': 'https://youtu.be/id2', 'EN Title': 'Preset EN'},
    ]
    video_list = tmp_path / 'videos.json'
    video_list.write_text(json.dumps(vids), encoding='utf-8')

    metadata_dir = tmp_path / 'metadata'
    cache_dir = tmp_path / '.cache'
    metadata_dir.mkdir()
    cache_dir.mkdir()

    # id1 has metadata and title cache
    (metadata_dir / 'id1.json').write_text(json.dumps({'title': '원제목', 'uploader': 'Creator1'}), encoding='utf-8')
    (cache_dir / 'title_id1.json').write_text(json.dumps({'title_en': 'Translated Title', 'source_hash': 'x'}), encoding='utf-8')
    # id2 has metadata but no title cache; EN Title preset should remain
    (metadata_dir / 'id2.json').write_text(json.dumps({'title': '다른 제목', 'uploader': 'Creator2'}), encoding='utf-8')

    build_videos_json(str(video_list), str(metadata_dir), str(cache_dir))
    data = json.loads(video_list.read_text(encoding='utf-8'))
    d1 = next(x for x in data if x['v'] == 'id1')
    d2 = next(x for x in data if x['v'] == 'id2')

    assert d1['EN Title'] == 'Translated Title'
    assert d1['Creator'] == 'Creator1'
    assert d2['EN Title'] == 'Preset EN'  # preserved
    assert d2['Creator'] == 'Creator2'


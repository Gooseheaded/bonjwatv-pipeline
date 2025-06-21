import os
import sys
import json

sys.path.insert(0, os.getcwd())

import pytest

from export_sheet_to_json import export_sheet_to_json


class DummyWS:
    def get_all_records(self):
        return [{'v': 'vid1', 'youtube_url': 'u1'}, {'v': 'vid2', 'youtube_url': 'u2'}]


class DummySP:
    def worksheet(self, name):
        assert name == 'Sheet1'
        return DummyWS()


class DummyGC:
    def open(self, name):
        assert name == 'MySheet'
        return DummySP()


@pytest.fixture(autouse=True)
def patch_gspread(monkeypatch):
    monkeypatch.setattr('export_sheet_to_json.gspread',
                        type('G', (), {'service_account': lambda filename=None: DummyGC()}))


def test_export_sheet_to_json(tmp_path):
    output = tmp_path / 'videos.json'
    export_sheet_to_json(spreadsheet='MySheet', worksheet='Sheet1', output=str(output))
    assert output.exists()
    data = json.loads(output.read_text(encoding='utf-8'))
    assert isinstance(data, list)
    assert data[0]['v'] == 'vid1'
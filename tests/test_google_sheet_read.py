import json
import os
import sys

sys.path.insert(0, os.getcwd())

import pytest

from google_sheet_read import run_google_sheet_read


class DummyWS:
    def get_all_records(self):
        return [{"v": "vid1", "youtube_url": "u1"}, {"v": "vid2", "youtube_url": "u2"}]


class DummySP:
    def worksheet(self, name):
        assert name == "Sheet1"
        return DummyWS()


class DummyGC:
    def open(self, name):
        assert name == "MySheet"
        return DummySP()


@pytest.fixture(autouse=True)
def patch_gspread(monkeypatch):
    monkeypatch.setattr(
        "google_sheet_read.gspread",
        type("G", (), {"service_account": lambda filename=None: DummyGC()}),
    )


def test_google_sheet_read(tmp_path):
    output = tmp_path / "videos.json"
    assert run_google_sheet_read(
        spreadsheet="MySheet", worksheet="Sheet1", output=str(output)
    )
    assert output.exists()
    data = json.loads(output.read_text(encoding="utf-8"))
    assert isinstance(data, list)
    assert data[0]["v"] == "vid1"

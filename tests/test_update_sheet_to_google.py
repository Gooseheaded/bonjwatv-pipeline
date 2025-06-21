import os
import sys

sys.path.insert(0, os.getcwd())

import json
import pytest

from update_sheet_to_google import update_sheet_to_google


class DummyWorksheet:
    def __init__(self):
        self._updates = []

    def find(self, cell_value):
        class Cell:
            def __init__(self, row, col):
                self.row = row
                self.col = col
        # data rows assume vid1 at row 2, vid2 at row 3
        if cell_value == 'vid1':
            return Cell(2, 1)
        if cell_value == 'vid2':
            return Cell(3, 1)
        raise ValueError

    def update_cell(self, row, col, value):
        self._updates.append((row, col, value))

    def cell(self, row, col):
        # Return a dummy cell object with a .value attribute (initially None)
        class Cell:
            def __init__(self):
                self.value = None
        return Cell()

    def row_values(self, row):
        # Simulate header row: first column 'v', second column the Pastebin URL column
        return ['v', 'Pastebin URL']

    @property
    def updates(self):
        return self._updates


class DummySP:
    def worksheet(self, name):
        return DummyWorksheet()


class DummyGC:
    def open(self, name):
        return DummySP()


@pytest.fixture(autouse=True)
def patch_gspread(monkeypatch):
    monkeypatch.setenv('PASTEBIN_API_KEY', 'irrelevant')
    monkeypatch.setenv('PASTEBIN_FOLDER', 'BWKT')
    # Provide a singleton DummyWorksheet instance
    dummy_ws = DummyWorksheet()
    def fake_service_account(filename=None):
        class GC:
            def open(self, name):
                class SP:
                    def worksheet(self, ws_name):
                        return dummy_ws
                return SP()
        return GC()
    import gspread
    monkeypatch.setattr(gspread, 'service_account', fake_service_account)
    return dummy_ws


def test_update_sheet_to_google(tmp_path, caplog, patch_gspread):
    # Prepare metadata and cache
    metadata = [{'v': 'vid1'}, {'v': 'vid2'}]
    meta_file = tmp_path / 'videos.json'
    meta_file.write_text(json.dumps(metadata), encoding='utf-8')

    cache_dir = tmp_path / '.cache'
    cache_dir.mkdir()
    # Only vid1 has a paste URL
    cache_file = cache_dir / 'pastebin_vid1.json'
    cache_file.write_text(json.dumps({'url': 'https://pastebin.com/raw/ABC'}), encoding='utf-8')

    caplog.set_level('INFO')
    # Run update
    update_sheet_to_google(
        metadata_file=str(meta_file),
        cache_dir=str(cache_dir),
        spreadsheet='MySheet',
        worksheet='Sheet1',
        column_name='Pastebin URL',
        service_account_file=None,
    )

    # Only vid1 should trigger an update
    assert 'Updated vid1 in row 2, col 2' in caplog.text
    # Verify that the worksheet recorded one update
    # The patched DummyWorksheet captured updates
    ws = patch_gspread
    assert ws.updates == [(2, 2, 'https://pastebin.com/raw/ABC')]
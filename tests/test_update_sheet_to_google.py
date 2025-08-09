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
        # Simulate transient APIError on first attempt, succeed thereafter
        if not hasattr(self, '_fail_once'):
            self._fail_once = True
            from gspread.exceptions import APIError
            # Construct a fake response object for APIError
            class FakeResp:
                def __init__(self, text):
                    self.text = text
                def json(self):
                    raise ValueError
            raise APIError(FakeResp('Temporary rate limit'))
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
    video_list_file = tmp_path / 'videos.json'
    video_list_file.write_text(json.dumps(metadata), encoding='utf-8')

    cache_dir = tmp_path / '.cache'
    cache_dir.mkdir()
    # Only vid1 has a paste URL
    cache_file = cache_dir / 'pastebin_vid1.json'
    cache_file.write_text(json.dumps({'url': 'https://pastebin.com/raw/ABC'}), encoding='utf-8')

    caplog.set_level('INFO')
    # Run update
    update_sheet_to_google(
        video_list_file=str(video_list_file),
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

def test_skip_missing_id(tmp_path, caplog, patch_gspread):
    # If a video ID is not found in the sheet, it should be skipped without error
    metadata = [{'v': 'nope'}]
    video_list_file = tmp_path / 'videos2.json'
    video_list_file.write_text(json.dumps(metadata), encoding='utf-8')

    cache_dir = tmp_path / '.cache2'
    cache_dir.mkdir()
    # Create a cache entry so update is attempted
    cache_file = cache_dir / 'pastebin_nope.json'
    cache_file.write_text(json.dumps({'url': 'https://pastebin.com/raw/XYZ'}), encoding='utf-8')

    caplog.set_level('WARNING')
    update_sheet_to_google(
        video_list_file=str(video_list_file),
        cache_dir=str(cache_dir),
        spreadsheet='MySheet',
        worksheet='Sheet1',
        column_name='Pastebin URL',
        service_account_file=None,
    )
    assert 'Video ID nope not found in sheet, skipping update' in caplog.text

def test_skip_google_cached(tmp_path, caplog, patch_gspread):
    # If we've already updated this video+URL, skip without any sheet calls
    metadata = [{'v': 'vid1'}]
    video_list_file = tmp_path / 'videos.json'
    video_list_file.write_text(json.dumps(metadata), encoding='utf-8')

    cache_dir = tmp_path / '.cache'
    cache_dir.mkdir()
    # Pastebin cache entry
    pb = cache_dir / 'pastebin_vid1.json'
    pb.write_text(json.dumps({'url': 'https://pastebin.com/raw/ABC'}), encoding='utf-8')
    # Google Sheet cache entry matching the same URL
    gs = cache_dir / 'google_vid1.json'
    gs.write_text(json.dumps({'url': 'https://pastebin.com/raw/ABC'}), encoding='utf-8')

    caplog.set_level('INFO')
    update_sheet_to_google(
        video_list_file=str(video_list_file),
        cache_dir=str(cache_dir),
        spreadsheet='MySheet',
        worksheet='Sheet1',
        column_name='Pastebin URL',
        service_account_file=None,
    )
    # Should log the cache skip and make no updates
    assert 'Skipping update for vid1 (cached Google Sheet)' in caplog.text
    assert patch_gspread.updates == []

def test_existing_sheet_sets_cache(tmp_path, monkeypatch, caplog, patch_gspread):
    # If sheet already has a URL (existing cell), we should cache and skip updating
    metadata = [{'v': 'vid1'}]
    video_list_file = tmp_path / 'videos.json'
    video_list_file.write_text(json.dumps(metadata), encoding='utf-8')

    cache_dir = tmp_path / '.cache'
    cache_dir.mkdir()
    # Pastebin cache entry with URL
    paste_cache = cache_dir / 'pastebin_vid1.json'
    paste_cache.write_text(json.dumps({'url': 'https://pastebin.com/raw/XYZ'}), encoding='utf-8')

    # Monkeypatch ws.cell to simulate existing sheet cell value
    existing_url = 'https://pastebin.com/raw/XYZ'
    def fake_cell(row, col):
        class C:
            value = existing_url
        return C()
    monkeypatch.setattr(patch_gspread, 'cell', fake_cell)

    caplog.set_level('INFO')
    update_sheet_to_google(
        video_list_file=str(video_list_file),
        cache_dir=str(cache_dir),
        spreadsheet='MySheet',
        worksheet='Sheet1',
        column_name='Pastebin URL',
        service_account_file=None,
    )

    # Should skip update and write google cache
    assert 'Skipping update for vid1 (already set)' in caplog.text
    gs_file = cache_dir / 'google_vid1.json'
    assert gs_file.exists()
    data = json.loads(gs_file.read_text(encoding='utf-8'))
    assert data['url'] == existing_url
    # No actual sheet updates
    assert patch_gspread.updates == []
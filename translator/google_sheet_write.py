import json
import logging
import os
import time
from typing import Any, Callable, Optional

import gspread
from dotenv import load_dotenv
from gspread.exceptions import APIError


def _retry_gspread_call(
    func: Callable, *args: Any, max_attempts: int = 5, **kwargs: Any
) -> Any:
    """Retry a gspread API call with exponential backoff."""
    for attempt in range(max_attempts):
        try:
            return func(*args, **kwargs)
        except APIError as e:
            delay = 2**attempt
            logging.warning(
                f"Google Sheets APIError: {e}; retrying in {delay}s (attempt {attempt + 1}/{max_attempts})"
            )
            time.sleep(delay)
    raise RuntimeError(f"Google Sheets API call failed after {max_attempts} attempts.")


def _authenticate(service_account_file: Optional[str]) -> gspread.client.Client:
    """Authenticate to Google Sheets, optionally with a specific service account file."""
    if service_account_file:
        return gspread.service_account(filename=service_account_file)
    return gspread.service_account()


def _get_column_index(ws: gspread.Worksheet, column_name: str) -> Optional[int]:
    """Return 1-based index of the given header name, or None if not found."""
    headers = ws.row_values(1)
    try:
        return headers.index(column_name) + 1
    except ValueError:
        return None


def _load_pastebin_url(cache_dir: str, vid: str) -> Optional[str]:
    """Load Pastebin URL for a given video ID from the cache directory."""
    cache_file = os.path.join(cache_dir, f"pastebin_{vid}.json")
    if not os.path.exists(cache_file):
        return None
    data = json.load(open(cache_file, encoding="utf-8"))
    return data.get("url")


def _is_cached_sheet_update(cache_dir: str, vid: str, url: str) -> bool:
    """Return True if this video ID and URL appear to have been written already."""
    sheet_cache = os.path.join(cache_dir, f"google_{vid}.json")
    if os.path.exists(sheet_cache):
        cached = json.load(open(sheet_cache, encoding="utf-8")).get("url")
        if cached == url:
            logging.info("Skipping update for %s (cached Google Sheet)", vid)
            return True
    return False


def _find_video_cell(ws: gspread.Worksheet, vid: str):
    """Find the cell containing the given video ID, handling not-found gracefully."""
    try:
        return ws.find(vid)
    except Exception:  # Not found or other error
        logging.warning("Video ID %s not found in sheet, skipping update", vid)
        return None


def _existing_value(ws: gspread.Worksheet, row: int, col: int) -> Optional[str]:
    """Read an existing cell value with retry/backoff."""
    try:
        return _retry_gspread_call(ws.cell, row, col).value
    except RuntimeError as e:
        logging.warning("Error reading existing value at row=%s col=%s: %s", row, col, e)
        return None


def _write_url_and_cache(
    ws: gspread.Worksheet, row: int, col: int, vid: str, url: str, cache_dir: str
) -> None:
    """Write URL to the sheet with retry/backoff and cache the update locally."""
    _retry_gspread_call(ws.update_cell, row, col, url)
    logging.info("Updated %s in row %d, col %d", vid, row, col)
    sheet_cache = os.path.join(cache_dir, f"google_{vid}.json")
    with open(sheet_cache, "w", encoding="utf-8") as f:
        json.dump({"url": url}, f, ensure_ascii=False, indent=2)


def run_google_sheet_write(
    video_list_file: str,
    cache_dir: str,
    spreadsheet: str,
    worksheet: str,
    column_name: str,
    service_account_file: Optional[str] = None,
) -> bool:
    """Update a Google Sheet by writing Pastebin URLs into the specified column."""
    try:
        load_dotenv()
        os.makedirs(cache_dir, exist_ok=True)

        gc = _authenticate(service_account_file)
        ws = gc.open(spreadsheet).worksheet(worksheet)

        col_idx = _get_column_index(ws, column_name)
        if not col_idx:
            logging.error("Column '%s' not found in header", column_name)
            return False

        videos = json.load(open(video_list_file, encoding="utf-8"))
        for v in videos:
            vid = v["v"]
            url = _load_pastebin_url(cache_dir, vid)
            if not url:
                continue

            if _is_cached_sheet_update(cache_dir, vid, url):
                continue

            cell = _find_video_cell(ws, vid)
            if not cell:
                continue

            existing = _existing_value(ws, cell.row, col_idx)
            if existing:
                logging.info("Skipping update for %s (already set)", vid)
                # Cache to avoid future redundant sheet calls
                with open(os.path.join(cache_dir, f"google_{vid}.json"), "w", encoding="utf-8") as f:
                    json.dump({"url": url}, f, ensure_ascii=False, indent=2)
                continue

            try:
                _write_url_and_cache(ws, cell.row, col_idx, vid, url, cache_dir)
            except RuntimeError as e:
                logging.error("Failed to update cell for %s: %s", vid, e)
                continue
        return True
    except Exception as e:
        logging.error("Update sheet failed: %s", e)
        return False

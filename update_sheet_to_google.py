#!/usr/bin/env python3
import os
import json
import argparse
import logging

import logging
import gspread
from dotenv import load_dotenv

import time
from gspread.exceptions import APIError

load_dotenv()


def update_sheet_to_google(metadata_file: str,
                           cache_dir: str,
                           spreadsheet: str,
                           worksheet: str,
                           column_name: str,
                           service_account_file: str = None) -> None:
    """
    Update a Google Sheet by writing Pastebin URLs back into a specified column.
    """
    # Authenticate to Google Sheets
    if service_account_file:
        gc = gspread.service_account(filename=service_account_file)
    else:
        gc = gspread.service_account()

    sh = gc.open(spreadsheet)
    ws = sh.worksheet(worksheet)

    # Determine column index by scanning header row (row 1)
    headers = ws.row_values(1)
    try:
        col_idx = headers.index(column_name) + 1
    except ValueError:
        raise RuntimeError(f"Column '{column_name}' not found in header")

    # Load metadata and process each video
    videos = json.load(open(metadata_file, encoding='utf-8'))
    for v in videos:
        vid = v['v']
        cache_file = os.path.join(cache_dir, f'pastebin_{vid}.json')
        if not os.path.exists(cache_file):
            continue

        data = json.load(open(cache_file, encoding='utf-8'))
        url = data.get('url')
        if not url:
            continue

        # Find the video ID cell with retry/backoff (skip if not found)
        try:
            cell = None
            for attempt in range(5):
                try:
                    cell = ws.find(vid)
                    break
                except APIError as e:
                    delay = 2 ** attempt
                    logging.warning(
                        "Google Sheets APIError on find(%s): %s; retrying in %ds",
                        vid, e, delay
                    )
                    time.sleep(delay)
            if not cell:
                logging.warning("Video ID %s not found in sheet after retries, skipping update", vid)
                continue
        except Exception as e:
            logging.warning("Video ID %s not found in sheet, skipping update (%s)", vid, e)
            continue

        # Check existing value in target column with retry/backoff
        try:
            existing = None
            for attempt in range(5):
                try:
                    existing = ws.cell(cell.row, col_idx).value
                    break
                except APIError as e:
                    delay = 2 ** attempt
                    logging.warning(
                        "Google Sheets APIError on cell(%d,%d): %s; retrying in %ds",
                        cell.row, col_idx, e, delay
                    )
                    time.sleep(delay)
            if existing:
                logging.info("Skipping update for %s (already set)", vid)
                continue
        except Exception as e:
            logging.warning("Error reading existing value for %s: %s", vid, e)
            continue

        # Write the Pastebin URL with retry/backoff
        for attempt in range(5):
            try:
                ws.update_cell(cell.row, col_idx, url)
                logging.info("Updated %s in row %d, col %d", vid, cell.row, col_idx)
                break
            except APIError as e:
                delay = 2 ** attempt
                logging.warning(
                    "Google Sheets APIError on update_cell(%d,%d): %s; retrying in %ds",
                    cell.row, col_idx, e, delay
                )
                time.sleep(delay)


def setup_logging():
    os.makedirs('logs', exist_ok=True)
    handler = logging.FileHandler('logs/update_sheet.log', encoding='utf-8')
    fmt = logging.Formatter('%(asctime)s %(levelname)s %(message)s')
    handler.setFormatter(fmt)
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)
    console = logging.StreamHandler()
    console.setFormatter(fmt)
    root.addHandler(console)
    return root


def main():
    log = setup_logging()
    p = argparse.ArgumentParser(description='Update Google Sheet with Pastebin URLs')
    p.add_argument('--metadata-file', required=True)
    p.add_argument('--cache-dir', required=True)
    p.add_argument('--spreadsheet', required=True)
    p.add_argument('--worksheet', required=True)
    p.add_argument('--column-name', required=True)
    p.add_argument('--service-account-file', help='Path to service account JSON')
    args = p.parse_args()

    try:
        update_sheet_to_google(
            metadata_file=args.metadata_file,
            cache_dir=args.cache_dir,
            spreadsheet=args.spreadsheet,
            worksheet=args.worksheet,
            column_name=args.column_name,
            service_account_file=args.service_account_file,
        )
    except Exception as e:
        log.error('Update sheet failed: %s', e)
        exit(1)


if __name__ == '__main__':
    main()
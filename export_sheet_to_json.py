#!/usr/bin/env python3
import os
import json
import argparse

import gspread


def export_sheet_to_json(spreadsheet: str, worksheet: str, output: str, service_account_file: str = None):
    """
    Read a Google Sheet and write its rows as JSON.

    Args:
        spreadsheet: name or key of the spreadsheet
        worksheet: worksheet/tab name
        output: path to write JSON file
        service_account_file: optional path to service account JSON
    """
    if service_account_file:
        gc = gspread.service_account(filename=service_account_file)
    else:
        gc = gspread.service_account()

    sh = gc.open(spreadsheet)
    ws = sh.worksheet(worksheet)
    records = ws.get_all_records()

    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, 'w', encoding='utf-8') as f:
        json.dump(records, f, ensure_ascii=False, indent=2)


def main():
    p = argparse.ArgumentParser(description='Export Google Sheet to JSON')
    p.add_argument('--spreadsheet', required=True)
    p.add_argument('--worksheet', required=True)
    p.add_argument('--output', required=True)
    p.add_argument('--service-account-file', help='Path to Google service account JSON')
    args = p.parse_args()

    export_sheet_to_json(
        spreadsheet=args.spreadsheet,
        worksheet=args.worksheet,
        output=args.output,
        service_account_file=args.service_account_file,
    )


if __name__ == '__main__':
    main()
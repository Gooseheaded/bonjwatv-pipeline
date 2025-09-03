import json
import logging
import os
from typing import Optional

import gspread


def run_google_sheet_read(
    spreadsheet: str,
    worksheet: str,
    output: str,
    service_account_file: Optional[str] = None,
) -> bool:
    """Read a Google Sheet and write its rows as JSON."""
    try:
        if service_account_file:
            gc = gspread.service_account(filename=service_account_file)
        else:
            gc = gspread.service_account()

        sh = gc.open(spreadsheet)
        ws = sh.worksheet(worksheet)
        records = ws.get_all_records()

        os.makedirs(os.path.dirname(output), exist_ok=True)
        with open(output, "w", encoding="utf-8") as f:
            json.dump(records, f, ensure_ascii=False, indent=2)
        return True
    except Exception as e:
        logging.error(f"Error in google_sheet_read: {e}")
        return False


# test wrapper removed; use run_google_sheet_read directly

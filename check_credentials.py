import argparse
import logging
import os

import gspread
import requests
from dotenv import load_dotenv
from openai import OpenAI, OpenAIError

# Load .env automatically, so you don't need to source it manually
load_dotenv()


def check_google(service_account_file: str, spreadsheet: str):
    """Check Google Sheets API credentials."""
    if not service_account_file or not os.path.isfile(service_account_file):
        return "Missing"
    try:
        gc = gspread.service_account(filename=service_account_file)
        gc.open(spreadsheet)
        return "Valid"
    except Exception:
        return "Invalid"


def check_openai():
    """Check OpenAI API credentials."""
    key = os.getenv("OPENAI_API_KEY")
    if not key:
        return "Missing"
    client = OpenAI(api_key=key)
    try:
        client.models.list()
        return "Valid"
    except OpenAIError:
        return "Invalid"


def check_pastebin():
    """Check Pastebin API credentials."""
    key = os.getenv("PASTEBIN_API_KEY")
    if not key:
        return "Missing"
    data = {"api_dev_key": key, "api_option": "list"}
    try:
        resp = requests.post("https://pastebin.com/api/api_post.php", data=data)
        text = resp.text.lower()
        if "invalid api_dev_key" in text:
            return "Invalid"
        return "Valid"
    except Exception:
        return "Invalid"


def main():
    """Perform credential health checks for all configured services."""
    p = argparse.ArgumentParser(description="Health check for pipeline credentials")
    p.add_argument("--service-account-file", help="Google Sheets service-account JSON")
    p.add_argument("--spreadsheet", help="Google spreadsheet name or key")
    args = p.parse_args()

    results = {}
    results["Google Sheets"] = check_google(args.service_account_file, args.spreadsheet)
    results["OpenAI API Key"] = check_openai()
    results["Pastebin API Key"] = check_pastebin()

    logging.info("\nCredential Health Check Summary:")
    for name, status in results.items():
        logging.info(f"  {name:<20}: {status}")

    if any(status != "Valid" for status in results.values()):
        exit(1)


if __name__ == "__main__":
    main()

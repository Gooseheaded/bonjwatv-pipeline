import json
import logging
import os
from typing import Optional

import requests


def run_upload_subtitles(  # noqa: C901
    input_file: str,
    cache_dir: str = ".cache",
    api_key: Optional[str] = None,
    user_key: Optional[str] = None,
    username: Optional[str] = None,
    password: Optional[str] = None,
) -> bool:
    """Upload a translated SRT to Pastebin and cache the resulting raw URL."""
    try:
        # Determine video ID and check per-video cache first
        vid = os.path.splitext(os.path.basename(input_file))[0][len("en_") :]
        os.makedirs(cache_dir, exist_ok=True)
        cache_file = os.path.join(cache_dir, f"pastebin_{vid}.json")
        if os.path.exists(cache_file):
            data = json.load(open(cache_file, encoding="utf-8"))
            logging.info("Using cached Pastebin URL for %s", vid)
            return True  # Already uploaded

        # Load Pastebin dev key and folder
        dev_key = api_key or os.getenv("PASTEBIN_API_KEY")
        folder = os.getenv("PASTEBIN_FOLDER", "")
        if not dev_key:
            logging.error("PASTEBIN_API_KEY is not set")
            return False

        # Determine or obtain a Pastebin user key (to post under account)
        user_key = user_key or os.getenv("PASTEBIN_USER_KEY")
        username = username or os.getenv("PASTEBIN_USERNAME")
        password = password or os.getenv("PASTEBIN_PASSWORD")
        user_key_cache = os.path.join(cache_dir, "pastebin_user_key.json")
        if not user_key and username and password:
            if os.path.exists(user_key_cache):
                user_key = json.load(open(user_key_cache, encoding="utf-8")).get(
                    "user_key"
                )
                logging.info("Using cached Pastebin user key")
            else:
                login_data = {
                    "api_dev_key": dev_key,
                    "api_user_name": username,
                    "api_user_password": password,
                }
                resp_login = requests.post(
                    "https://pastebin.com/api/api_login.php", data=login_data
                )
                if resp_login.status_code != 200:
                    logging.error(f"Pastebin login failed: {resp_login.status_code}")
                    return False
                user_key = resp_login.text.strip()
                with open(user_key_cache, "w", encoding="utf-8") as f:
                    json.dump({"user_key": user_key}, f, ensure_ascii=False, indent=2)
                logging.info("Logged in to Pastebin, user key cached")

        # Read subtitles and construct paste payload
        code = open(input_file, encoding="utf-8").read()
        data = {
            "api_dev_key": dev_key,
            "api_option": "paste",
            "api_paste_code": code,
            "api_paste_name": vid,
            "api_paste_private": "1",  # unlisted
            "api_paste_expire_date": "N",
            "api_paste_format": "",
        }
        if folder:
            # Pastebin API expects 'api_folder_key' to specify the destination folder
            data["api_folder_key"] = folder
        if user_key:
            data["api_user_key"] = user_key

        resp = requests.post("https://pastebin.com/api/api_post.php", data=data)
        if resp.status_code != 200:
            logging.error(f"Pastebin upload failed: {resp.status_code}")
            return False

        resp_text = resp.text.strip()
        # resp_text may be a pastebin URL or raw URL or just the paste ID
        if resp_text.startswith("http"):
            # Strip trailing slash and extract the paste ID
            pid = resp_text.rstrip("/").split("/")[-1]
            # If it's already a raw URL, keep it; otherwise build the raw URL
            if "/raw/" in resp_text:
                url = resp_text
            else:
                url = f"https://pastebin.com/raw/{pid}"
            paste_id = pid
        else:
            paste_id = resp_text
            url = f"https://pastebin.com/raw/{paste_id}"
        with open(cache_file, "w", encoding="utf-8") as f:
            json.dump(
                {"paste_id": paste_id, "url": url}, f, ensure_ascii=False, indent=2
            )
        logging.info("Uploaded to Pastebin: %s", url)
        return True
    except Exception as e:
        logging.error(f"Upload failed: {e}")
        return False

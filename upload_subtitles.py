#!/usr/bin/env python3
import os
import json
import argparse
import logging

import logging
import requests
from dotenv import load_dotenv

load_dotenv()


def upload_subtitles(input_file: str,
                     cache_dir: str = '.cache',
                     api_key: str = None,
                     user_key: str = None,
                     username: str = None,
                     password: str = None) -> str:
    # Determine video ID and check per-video cache first
    vid = os.path.splitext(os.path.basename(input_file))[0][len('en_'):]
    os.makedirs(cache_dir, exist_ok=True)
    cache_file = os.path.join(cache_dir, f'pastebin_{vid}.json')
    if os.path.exists(cache_file):
        data = json.load(open(cache_file, encoding='utf-8'))
        logging.info('Using cached Pastebin URL for %s', vid)
        return data['url']

    # Load Pastebin dev key and folder
    dev_key = api_key or os.getenv('PASTEBIN_API_KEY')
    folder = os.getenv('PASTEBIN_FOLDER', '')
    if not dev_key:
        raise RuntimeError('PASTEBIN_API_KEY is not set')

    # Determine or obtain a Pastebin user key (to post under account)
    user_key = user_key or os.getenv('PASTEBIN_USER_KEY')
    username = username or os.getenv('PASTEBIN_USERNAME')
    password = password or os.getenv('PASTEBIN_PASSWORD')
    user_key_cache = os.path.join(cache_dir, 'pastebin_user_key.json')
    if not user_key and username and password:
        if os.path.exists(user_key_cache):
            user_key = json.load(open(user_key_cache, encoding='utf-8')).get('user_key')
            logging.info('Using cached Pastebin user key')
        else:
            login_data = {
                'api_dev_key': dev_key,
                'api_user_name': username,
                'api_user_password': password,
            }
            resp_login = requests.post('https://pastebin.com/api/api_login.php', data=login_data)
            if resp_login.status_code != 200:
                raise RuntimeError(f'Pastebin login failed: {resp_login.status_code}')
            user_key = resp_login.text.strip()
            with open(user_key_cache, 'w', encoding='utf-8') as f:
                json.dump({'user_key': user_key}, f, ensure_ascii=False, indent=2)
            logging.info('Logged in to Pastebin, user key cached')

    # Read subtitles and construct paste payload
    code = open(input_file, encoding='utf-8').read()
    data = {
        'api_dev_key': dev_key,
        'api_option': 'paste',
        'api_paste_code': code,
        'api_paste_name': vid,
        'api_paste_private': '1',  # unlisted
        'api_paste_expire_date': 'N',
        'api_paste_format': '',
    }
    if folder:
        data['api_paste_folder'] = folder
    if user_key:
        data['api_user_key'] = user_key

    resp = requests.post('https://pastebin.com/api/api_post.php', data=data)
    if resp.status_code != 200:
        raise RuntimeError(f'Pastebin upload failed: {resp.status_code}')

    paste_id = resp.text.strip()
    url = f'https://pastebin.com/raw/{paste_id}'
    with open(cache_file, 'w', encoding='utf-8') as f:
        json.dump({'paste_id': paste_id, 'url': url}, f, ensure_ascii=False, indent=2)
    logging.info('Uploaded to Pastebin: %s', url)
    return url


def setup_logging():
    os.makedirs('logs', exist_ok=True)
    handler = logging.FileHandler('logs/upload_subtitles.log', encoding='utf-8')
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
    p = argparse.ArgumentParser(description='Upload English subtitles to Pastebin')
    p.add_argument('--input-file', required=True)
    p.add_argument('--cache-dir', default='.cache')
    p.add_argument('--api-key', help='Pastebin API dev key (or set PASTEBIN_API_KEY)')
    p.add_argument('--user-key', help='Pastebin API user key (or set PASTEBIN_USER_KEY)')
    p.add_argument('--username', help='Pastebin username (or set PASTEBIN_USERNAME)')
    p.add_argument('--password', help='Pastebin password (or set PASTEBIN_PASSWORD)')
    args = p.parse_args()

    try:
        upload_subtitles(
            args.input_file,
            args.cache_dir,
            args.api_key,
            args.user_key,
            args.username,
            args.password,
        )
    except Exception as e:
        log.error('Upload failed: %s', e)
        exit(1)


if __name__ == '__main__':
    main()
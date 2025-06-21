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
                     api_key: str = None) -> str:
    # Determine video ID and check cache first
    vid = os.path.splitext(os.path.basename(input_file))[0][len('en_'):]
    os.makedirs(cache_dir, exist_ok=True)
    cache_file = os.path.join(cache_dir, f'pastebin_{vid}.json')
    if os.path.exists(cache_file):
        data = json.load(open(cache_file, encoding='utf-8'))
        logging.info('Using cached Pastebin URL for %s', vid)
        return data['url']

    api_key = api_key or os.getenv('PASTEBIN_API_KEY')
    folder = os.getenv('PASTEBIN_FOLDER', '')
    if not api_key:
        raise RuntimeError('PASTEBIN_API_KEY is not set')

    code = open(input_file, encoding='utf-8').read()
    data = {
        'api_dev_key': api_key,
        'api_option': 'paste',
        'api_paste_code': code,
        'api_paste_name': vid,
        'api_paste_private': '1',  # unlisted
        'api_paste_expire_date': 'N',
        'api_paste_format': '',
    }
    if folder:
        data['api_paste_folder'] = folder

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
    args = p.parse_args()

    try:
        upload_subtitles(args.input_file, args.cache_dir, args.api_key)
    except Exception as e:
        log.error('Upload failed: %s', e)
        exit(1)


if __name__ == '__main__':
    main()
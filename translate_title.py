#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import time
from dotenv import load_dotenv
from openai import OpenAI, RateLimitError
import openai


load_dotenv()


def sha1(text: str) -> str:
    return hashlib.sha1(text.encode('utf-8')).hexdigest()


def ensure_client():
    key = os.getenv('OPENAI_API_KEY')
    if not key:
        raise RuntimeError('OPENAI_API_KEY is not set')
    return OpenAI(api_key=key)


def call_openai_translate(title: str, model: str) -> str:
    client = ensure_client()
    for attempt in range(5):
        try:
            resp = client.chat.completions.create(
                model=model,
                messages=[
                    {"role": "system", "content": "Translate a video title from Korean (or mixed) to concise, idiomatic English."},
                    {"role": "user", "content": f"Title:\n{title}\n\nReturn only the translated title."}
                ],
                temperature=0.3,
                max_tokens=200,
            )
            return (resp.choices[0].message.content or '').strip()
        except RateLimitError:
            time.sleep(5)
        except openai.BadRequestError as e:
            raise
        except Exception:
            time.sleep(3)
    raise RuntimeError('OpenAI API failed after retries')


def translate_title(video_id: str, metadata_dir: str, cache_dir: str, model: str = 'gpt-4.1-mini') -> str:
    os.makedirs(cache_dir, exist_ok=True)
    meta_path = os.path.join(metadata_dir, f"{video_id}.json")
    if not os.path.exists(meta_path):
        raise FileNotFoundError(f"Metadata not found for {video_id}: {meta_path}")
    details = json.load(open(meta_path, encoding='utf-8'))
    source_title = details.get('title') or ''
    if not source_title:
        raise RuntimeError(f"No title in metadata for {video_id}")

    cache_path = os.path.join(cache_dir, f"title_{video_id}.json")
    source_hash = sha1(source_title)
    if os.path.exists(cache_path):
        data = json.load(open(cache_path, encoding='utf-8'))
        if data.get('source_hash') == source_hash and data.get('title_en'):
            return data['title_en']

    translated = call_openai_translate(source_title, model=model)
    with open(cache_path, 'w', encoding='utf-8') as f:
        json.dump({
            'video_id': video_id,
            'title_en': translated,
            'source_hash': source_hash,
        }, f, ensure_ascii=False, indent=2)
    return translated


def main():
    p = argparse.ArgumentParser(description='Translate video title and cache result')
    p.add_argument('--video-id', required=True)
    p.add_argument('--metadata-dir', required=True)
    p.add_argument('--cache-dir', default='.cache')
    p.add_argument('--model', default='gpt-4.1-mini')
    args = p.parse_args()
    translate_title(args.video_id, args.metadata_dir, args.cache_dir, model=args.model)


if __name__ == '__main__':
    main()


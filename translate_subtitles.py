#!/usr/bin/env python3
import os
import re
import json
import argparse
import time
from common import setup_logging

from dotenv import load_dotenv
from openai import OpenAI, RateLimitError
import openai

class ContextLengthError(Exception):
    pass

load_dotenv()
_OPENAI_API_KEY = os.getenv('OPENAI_API_KEY')
if _OPENAI_API_KEY:
    client = OpenAI(api_key=_OPENAI_API_KEY)
else:
    client = None

class Subtitle:
    def __init__(self, index: int, start: str, end: str, lines: list):
        self.index = index
        self.start = start
        self.end = end
        self.lines = lines

    def to_srt_block(self) -> str:
        return f"{self.index}\n{self.start} --> {self.end}\n" + "\n".join(self.lines) + "\n"

def parse_srt_file(filename: str) -> list:
    with open(filename, encoding='utf-8-sig') as f:
        content = f.read()
    pattern = re.compile(
        r'(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)',
        re.MULTILINE
    )
    subs = []
    for m in pattern.finditer(content):
        subs.append(Subtitle(int(m.group(1)), m.group(2), m.group(3), m.group(4).strip().split('\n')))
    return subs

def write_srt_file(filename: str, subtitles: list) -> None:
    with open(filename, 'w', encoding='utf-8') as f:
        for sub in subtitles:
            f.write(sub.to_srt_block())
            f.write('\n')

def chunk_subtitles(subs: list, chunk_size: int, overlap: int) -> list:
    if chunk_size <= overlap:
        raise ValueError('chunk_size must be greater than overlap')
    chunks = []
    step = chunk_size - overlap
    for i in range(0, len(subs), step):
        chunk = subs[i:i + chunk_size]
        if not chunk:
            break
        if len(chunk) < chunk_size and i > 0:
            break
        chunks.append(chunk)
    return chunks

def build_prompt(chunk: list, slang_text: str) -> str:
    srt_content = ''
    for sub in chunk:
        srt_content += sub.to_srt_block() + '\n'
    return (
        'You are translating Korean StarCraft: Brood War subtitles to English.\n'
        'Use the provided slang glossary to improve translation accuracy.\n'
        'Translate the following subtitles, preserving timestamps and .srt formatting.\n'
        'Correct any duplicate lines or obvious errors.\n'
        '---\n'
        f'{srt_content}'
        '---\n'
        'KoreanSlang Glossary:\n'
        f'{slang_text}\n'
        'Translate the subtitles only, keep the formatting exactly like .srt.\n'
    )

def get_cache_filename(cache_dir: str, input_filename: str, idx: int) -> str:
    base = os.path.splitext(os.path.basename(input_filename))[0]
    return os.path.join(cache_dir, f"{base}_chunk{idx}.json")

def save_cache(cache_file: str, data: str) -> None:
    with open(cache_file, 'w', encoding='utf-8') as f:
        json.dump({'translation': data}, f, ensure_ascii=False, indent=2)

def load_cache(cache_file: str) -> str:
    if not os.path.isfile(cache_file):
        return None
    with open(cache_file, 'r', encoding='utf-8') as f:
        return json.load(f).get('translation')

def call_openai_api(prompt: str, model: str = 'gpt-4', temperature: float = 0.2) -> str:
    if client is None:
        raise RuntimeError('OPENAI_API_KEY is not set')
    for attempt in range(5):
        try:
            resp = client.chat.completions.create(
                model=model,
                messages=[
                    {'role': 'system', 'content': 'You translate and adapt subtitles from Korean to English accurately.'},
                    {'role': 'user', 'content': prompt}
                ],
                temperature=temperature,
                max_tokens=4000
            )
            return resp.choices[0].message.content.strip()
        except RateLimitError:
            log.warning('Rate limit hit, retrying in 5s...')
            time.sleep(5)
        except openai.BadRequestError as e:
            # Handle context-length errors by raising for outer retry with smaller chunks
            if getattr(e, 'code', '') == 'context_length_exceeded' or 'context_length_exceeded' in str(e):
                raise ContextLengthError from e
            log.warning(f'OpenAI API error: {e}, retrying in 5s...')
            time.sleep(5)
        except Exception as e:
            log.warning(f'OpenAI API error: {e}, retrying in 5s...')
            time.sleep(5)
    raise RuntimeError('OpenAI API failed after multiple retries')

def parse_translated_chunk(text: str) -> list:
    pattern = re.compile(
        r'(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)',
        re.MULTILINE
    )
    subs = []
    for m in pattern.finditer(text):
        subs.append(Subtitle(int(m.group(1)), m.group(2), m.group(3), m.group(4).strip().split('\n')))
    return subs

def merge_chunks(chunks: list, overlap: int) -> list:
    merged = []
    for i, chunk in enumerate(chunks):
        if i == 0:
            merged.extend(chunk)
        else:
            merged.extend(chunk[overlap:])
    for i, sub in enumerate(merged, start=1):
        sub.index = i
    return merged

def translate_srt_file(input_file: str,
                       output_file: str,
                       slang_file: str,
                       chunk_size: int = 50,
                       overlap: int = 5,
                       cache_dir: str = '.cache',
                       model: str = 'gpt-4') -> None:
    os.makedirs(cache_dir, exist_ok=True)
    os.makedirs(os.path.dirname(output_file), exist_ok=True)

    log.info(f'Loading subtitles from {input_file}')
    subs = parse_srt_file(input_file)
    log.info(f'Loaded {len(subs)} subtitles')

    with open(slang_file, encoding='utf-8') as f:
        slang_text = f.read()

    # Translate chunks, reducing chunk_size on context-length errors
    if chunk_size <= overlap or chunk_size < 1:
        raise ValueError('chunk_size must be greater than overlap and >=1')
    while True:
        chunks = chunk_subtitles(subs, chunk_size, overlap)
        log.info(f'Divided into {len(chunks)} chunks (size={chunk_size}, overlap={overlap})')
        translated = []
        try:
            for i, chunk in enumerate(chunks):
                cache_file = get_cache_filename(cache_dir, input_file, i)
                cached = load_cache(cache_file)
                if cached:
                    log.info(f'Using cache for chunk {i+1}/{len(chunks)}')
                    translated.append(parse_translated_chunk(cached))
                    continue

                log.info(f'Translating chunk {i+1}/{len(chunks)}')
                prompt = build_prompt(chunk, slang_text)
                result = call_openai_api(prompt, model=model)
                save_cache(cache_file, result)
                translated.append(parse_translated_chunk(result))
                time.sleep(1)
            break
        except ContextLengthError:
            new_size = chunk_size - 10
            if new_size <= overlap:
                raise RuntimeError(f'Cannot reduce chunk_size below overlap+1 ({overlap+1})')
            log.warning('Context length exceeded, reducing chunk_size by 10: %d -> %d', chunk_size, new_size)
            chunk_size = new_size

    merged = merge_chunks(translated, overlap)
    log.info(f'Writing output to {output_file}')
    write_srt_file(output_file, merged)

log = setup_logging(__name__, 'logs/translate_subtitles.log')

def main():
    # Logger already initialized at module level
    p = argparse.ArgumentParser(description='Translate Korean SRT to English with caching and glossary')
    p.add_argument('--input-file', required=True)
    p.add_argument('--output-file')
    p.add_argument('--slang-file', default='slang/KoreanSlang.txt')
    p.add_argument('--chunk-size', type=int, default=50)
    p.add_argument('--overlap', type=int, default=5)
    p.add_argument('--cache-dir', default='.cache')
    p.add_argument('--model', default='gpt-4.1-mini')
    args = p.parse_args()

    inp = args.input_file
    out = args.output_file or ('en_' + os.path.basename(inp))
    translate_srt_file(
        input_file=inp,
        output_file=out,
        slang_file=args.slang_file,
        chunk_size=args.chunk_size,
        overlap=args.overlap,
        cache_dir=args.cache_dir,
        model=args.model,
    )

if __name__ == '__main__':
    main()
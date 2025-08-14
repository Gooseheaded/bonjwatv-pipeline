#!/usr/bin/env python3
"""
Recursively translate all Korean .srt files in a directory tree to English,
preserving directory structure. Uses the same OpenAI-based logic as translate_subtitles.py.
"""
import os
import argparse

from common import setup_logging
from translate_subtitles import translate_srt_file
from normalize_srt import process_srt_file
import tempfile

log = setup_logging(__name__, 'logs/translate_folder.log')


def main():
    p = argparse.ArgumentParser(
        description='Translate all .srt files under a directory tree to English'
    )
    p.add_argument('--input-dir', required=True,
                   help='Root folder containing .srt files to translate')
    p.add_argument('--output-dir', required=True,
                   help='Destination folder for translated .srt files')
    p.add_argument('--slang-file', default='slang/KoreanSlang.txt',
                   help='Path to slang glossary file')
    p.add_argument('--chunk-size', type=int, default=50,
                   help='Number of subtitles per translation chunk')
    p.add_argument('--overlap', type=int, default=5,
                   help='Overlap between chunks for merging')
    p.add_argument('--cache-dir', default='.cache',
                   help='Directory for translation caches')
    p.add_argument('--model', default='gpt-4.1-mini',
                   help='OpenAI model to use')
    args = p.parse_args()

    input_dir = args.input_dir
    output_dir = args.output_dir

    # Use a temporary workspace to normalize raw .srt before translation
    with tempfile.TemporaryDirectory(prefix='cleaned_srt_') as clean_root:
        for root, _, files in os.walk(input_dir, topdown=False):
            for fname in files:
                if not fname.endswith('.srt'):
                    continue
                in_path = os.path.join(root, fname)
                # Post-process raw Korean SRT: normalize timestamps & collapse duplicates
                rel = os.path.relpath(root, input_dir)
                clean_dir = clean_root if rel == os.curdir else os.path.join(clean_root, rel)
                os.makedirs(clean_dir, exist_ok=True)
                clean_path = os.path.join(clean_dir, fname)
                log.info(f'Post-processing {in_path} -> {clean_path}')
                process_srt_file(in_path, clean_path)

                # Translate the cleaned SRT to English
                target_dir = output_dir if rel == os.curdir else os.path.join(output_dir, rel)
                os.makedirs(target_dir, exist_ok=True)
                out_fname = fname if fname.startswith('en_') else f'en_{fname}'
                out_path = os.path.join(target_dir, out_fname)
                log.info(f'Translating {clean_path} -> {out_path}')
                translate_srt_file(
                    input_file=clean_path,
                    output_file=out_path,
                    slang_file=args.slang_file,
                    chunk_size=args.chunk_size,
                    overlap=args.overlap,
                    cache_dir=args.cache_dir,
                    model=args.model,
                )


if __name__ == '__main__':
    main()

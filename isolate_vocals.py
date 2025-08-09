#!/usr/bin/env python3
import os
import subprocess
import argparse
import logging
from common import setup_logging


def isolate_vocals(input_file: str,
                   output_dir: str = 'vocals',
                   model: str = 'demucs',
                   two_stems: bool = False) -> str:
    if not os.path.exists(input_file):
        raise FileNotFoundError(f"Input audio not found: {input_file}")

    video_id = os.path.splitext(os.path.basename(input_file))[0]
    dest_dir = os.path.join(output_dir, video_id)
    os.makedirs(dest_dir, exist_ok=True)
    output_path = os.path.join(dest_dir, 'vocals.wav')

    if os.path.exists(output_path):
        logging.info(f"{output_path} already exists, skipping isolation")
        return output_path

    cmd = ['demucs', '--model', model]
    if two_stems:
        cmd.append('--two-stems')
    cmd += ['--out', output_dir, input_file]

    subprocess.run(cmd, check=True)
    logging.info(f"Vocal isolation complete: {output_path}")
    return output_path


log = setup_logging(__name__, 'logs/isolate_vocals.log')


def main():
    p = argparse.ArgumentParser(description='Isolate vocals from audio using Demucs')
    p.add_argument('--input-file', required=True, help='Path to input audio file')
    p.add_argument('--output-dir', default='vocals', help='Directory for isolated vocals')
    p.add_argument('--model', default='demucs', help='Demucs model name')
    p.add_argument('--two-stems', action='store_true', help='Enable two-stems separation')
    args = p.parse_args()

    try:
        isolate_vocals(
            input_file=args.input_file,
            output_dir=args.output_dir,
            model=args.model,
            two_stems=args.two_stems,
        )
    except Exception as e:
        log.error(f"Vocal isolation failed: {e}")
        exit(1)


if __name__ == '__main__':
    main()
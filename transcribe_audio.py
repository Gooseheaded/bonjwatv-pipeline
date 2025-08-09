#!/usr/bin/env python3
import os
import argparse
import logging
from common import setup_logging
import importlib

from whisper_postprocess import process_srt_file


def format_timestamp(seconds: float) -> str:
    ms = int(round((seconds - int(seconds)) * 1000))
    h = int(seconds // 3600)
    m = int((seconds % 3600) // 60)
    s = int(seconds % 60)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def transcribe_audio(
    audio_path: str,
    output_subtitle: str,
    model_size: str = 'large',
    language: str = 'ko',
):
    if not os.path.exists(audio_path):
        raise FileNotFoundError(f"Audio file not found: {audio_path}")

    # Lazy import of whisper to avoid top-level import errors
    whisper = importlib.import_module('whisper')
    model = whisper.load_model(model_size)

    result = model.transcribe(audio_path, language=language)

    os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)
    with open(output_subtitle, 'w', encoding='utf-8') as f:
        for i, segment in enumerate(result.get('segments', []), start=1):
            start = format_timestamp(segment['start'])
            end = format_timestamp(segment['end'])
            f.write(f"{i}\n{start} --> {end}\n{segment['text'].strip()}\n\n")

    # Post-process the raw SRT to normalize and collapse duplicates
    process_srt_file(output_subtitle, output_subtitle)
    logging.info(f"Subtitles saved to {output_subtitle}")
    return output_subtitle


log = setup_logging(__name__, 'logs/transcribe_audio.log')


def main():
    p = argparse.ArgumentParser(description='Transcribe audio to Korean SRT (Whisper)')
    p.add_argument('--input-file', required=True, help='Path to audio file')
    p.add_argument('--output-file', required=True, help='Path to output SRT file')
    p.add_argument('--model-size', default='large', help='Whisper model size (e.g. small, base, large)')
    p.add_argument('--language', default='ko', help='Language code for transcription')
    args = p.parse_args()

    try:
        transcribe_audio(
            audio_path=args.input_file,
            output_subtitle=args.output_file,
            model_size=args.model_size,
            language=args.language,
        )
    except Exception as e:
        log.error(f"Transcription failed: {e}")
        exit(1)


if __name__ == '__main__':
    main()
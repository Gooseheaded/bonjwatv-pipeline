#!/usr/bin/env python3
import os
import argparse
import logging
import json
from common import setup_logging
import importlib
from dotenv import load_dotenv

from normalize_srt import process_srt_file


def format_timestamp(seconds: float) -> str:
    ms = int(round((seconds - int(seconds)) * 1000))
    h = int(seconds // 3600)
    m = int((seconds % 3600) // 60)
    s = int(seconds % 60)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def transcribe_audio_openai(
    audio_path: str,
    output_subtitle: str,
    api_model: str,
    language: str,
):
    # Lazy import of openai to avoid top-level import errors
    openai = importlib.import_module('openai')
    load_dotenv()
    client = openai.OpenAI()  # API key is read from OPENAI_API_KEY env var

    with open(audio_path, 'rb') as audio_file:
        result = client.audio.transcriptions.create(
            model=api_model,
            file=audio_file,
            language=language,
            response_format="verbose_json",
            timestamp_granularities=["segment"]
        )

    os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)
    with open(output_subtitle, 'w', encoding='utf-8') as f:
        # The response object is a Pydantic model. We access segments via attributes.
        for i, segment in enumerate(result.segments, start=1):
            start = format_timestamp(segment.start)
            end = format_timestamp(segment.end)
            f.write(f"{i}\n{start} --> {end}\n{segment.text.strip()}\n\n")


def transcribe_audio_local(
    audio_path: str,
    output_subtitle: str,
    model_size: str,
    language: str,
):
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


def transcribe_audio(
    audio_path: str,
    output_subtitle: str,
    provider: str = 'local',
    model_size: str = 'large',
    language: str = 'ko',
    api_model: str = 'whisper-1',
):
    if not os.path.exists(audio_path):
        raise FileNotFoundError(f"Audio file not found: {audio_path}")

    if provider == 'openai':
        transcribe_audio_openai(audio_path, output_subtitle, api_model, language)
    elif provider == 'local':
        transcribe_audio_local(audio_path, output_subtitle, model_size, language)
    else:
        raise ValueError(f"Unsupported transcription provider: {provider}")

    # Post-process the raw SRT to normalize and collapse duplicates
    process_srt_file(output_subtitle, output_subtitle)
    logging.info(f"Subtitles saved to {output_subtitle}")
    return output_subtitle


log = setup_logging(__name__, 'logs/transcribe_audio.log')


def main():
    p = argparse.ArgumentParser(description='Transcribe audio to Korean SRT')
    p.add_argument('--input-file', required=True, help='Path to audio file')
    p.add_argument('--output-file', required=True, help='Path to output SRT file')
    p.add_argument('--provider', default='local', choices=['local', 'openai'], help='Transcription provider')
    p.add_argument('--model-size', default='large', help='Whisper model size for local provider (e.g. small, base, large)')
    p.add_argument('--api-model', default='whisper-1', help='Model name for OpenAI provider')
    p.add_argument('--language', default='ko', help='Language code for transcription')
    args = p.parse_args()

    try:
        transcribe_audio(
            audio_path=args.input_file,
            output_subtitle=args.output_file,
            provider=args.provider,
            model_size=args.model_size,
            language=args.language,
            api_model=args.api_model,
        )
    except Exception as e:
        log.error(f"Transcription failed: {e}")
        exit(1)


if __name__ == '__main__':
    main()

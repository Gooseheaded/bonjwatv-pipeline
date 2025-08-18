import importlib
import logging
import os

from dotenv import load_dotenv

from normalize_srt import run_normalize_srt


def format_timestamp(seconds: float) -> str:
    """Format a seconds float into an SRT timestamp string (HH:MM:SS,mmm)."""
    ms = round((seconds - int(seconds)) * 1000)
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
    """Transcribe audio via OpenAI API and write a basic SRT file."""
    # Lazy import of openai to avoid top-level import errors
    openai = importlib.import_module("openai")
    load_dotenv()
    client = openai.OpenAI()  # API key is read from OPENAI_API_KEY env var

    with open(audio_path, "rb") as audio_file:
        result = client.audio.transcriptions.create(
            model=api_model,
            file=audio_file,
            language=language,
            response_format="verbose_json",
            timestamp_granularities=["segment"],
        )

    os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)
    with open(output_subtitle, "w", encoding="utf-8") as f:
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
    """Transcribe audio locally using the Whisper library and write SRT output."""
    # Lazy import of whisper to avoid top-level import errors
    whisper = importlib.import_module("whisper")
    model = whisper.load_model(model_size)

    result = model.transcribe(audio_path, language=language)

    os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)
    with open(output_subtitle, "w", encoding="utf-8") as f:
        for i, segment in enumerate(result.get("segments", []), start=1):
            start = format_timestamp(segment["start"])
            end = format_timestamp(segment["end"])
            f.write(f"{i}\n{start} --> {end}\n{segment['text'].strip()}\n\n")


def run_transcribe_audio(
    audio_path: str,
    output_subtitle: str,
    provider: str = "local",
    model_size: str = "large",
    language: str = "ko",
    api_model: str = "whisper-1",
) -> bool:
    """Transcribe an audio file and normalize the resulting SRT file."""
    try:
        if not os.path.exists(audio_path):
            logging.error(f"Audio file not found for transcription: {audio_path}")
            return False

        if provider == "openai":
            # Call OpenAI API with response_format="srt" and write the returned text
            openai = importlib.import_module("openai")
            client = openai.OpenAI()
            with open(audio_path, "rb") as f:
                srt_text = client.audio.transcriptions.create(
                    model=api_model, file=f, language=language, response_format="srt"
                )
            os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)
            with open(output_subtitle, "w", encoding="utf-8") as out:
                out.write(srt_text)
            logging.info(f"Subtitles saved to {output_subtitle}")
            return True
        elif provider == "local":
            transcribe_audio_local(audio_path, output_subtitle, model_size, language)
        else:
            logging.error(f"Unsupported transcription provider: {provider}")
            return False

        # Post-process the raw SRT to normalize and collapse duplicates (local only)
        run_normalize_srt(output_subtitle, output_subtitle)
        logging.info(f"Subtitles saved to {output_subtitle}")
        return True
    except Exception as e:
        logging.error(f"Transcription failed for {audio_path}: {e}")
        return False

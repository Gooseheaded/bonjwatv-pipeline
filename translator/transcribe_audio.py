import importlib
import logging
import os

from dotenv import load_dotenv
import subprocess
import tempfile
import re
import time

from normalize_srt import run_normalize_srt


# Global flag indicating the last call experienced a quota error.
# Note: This is informational only; do not use it to gate future calls,
# which should be decided per-audio to avoid test/process leakage.
_quota_blocked = False


def _quota_marker_path(audio_path: str) -> str:
    """Path to a per-audio quota marker file used to short-circuit retries for the same audio."""
    return f"{audio_path}.quota_blocked"


def _mark_quota_exceeded():
    global _quota_blocked
    _quota_blocked = True


def quota_blocked() -> bool:
    return _quota_blocked


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


def _parse_srt(srt_text: str):
    pattern = re.compile(
        r"(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)",
        re.MULTILINE,
    )
    blocks = []
    for m in pattern.finditer(srt_text):
        blocks.append(
            (
                int(m.group(1)),
                m.group(2),
                m.group(3),
                m.group(4).strip(),
            )
        )
    return blocks


def _time_to_seconds(ts: str) -> float:
    h, m, rest = ts.split(":")
    s, ms = rest.split(",")
    return int(h) * 3600 + int(m) * 60 + int(s) + int(ms) / 1000.0


def _shift_timestamp(ts: str, offset_sec: float) -> str:
    return format_timestamp(_time_to_seconds(ts) + offset_sec)


def _shift_srt(srt_text: str, offset_sec: float) -> str:
    blocks = _parse_srt(srt_text)
    out_lines = []
    for i, (_, start, end, text) in enumerate(blocks, start=1):
        out_lines.append(str(i))
        out_lines.append(f"{_shift_timestamp(start, offset_sec)} --> {_shift_timestamp(end, offset_sec)}")
        out_lines.append(text)
        out_lines.append("")
    return "\n".join(out_lines) + "\n"


def _ffmpeg_segment(audio_path: str, out_dir: str, segment_time: int = 600, bitrate: str = "64k") -> list:
    os.makedirs(out_dir, exist_ok=True)
    out_pattern = os.path.join(out_dir, "chunk_%03d.mp3")
    cmd = [
        "ffmpeg",
        "-y",
        "-i",
        audio_path,
        "-ac",
        "1",
        "-b:a",
        bitrate,
        "-f",
        "segment",
        "-segment_time",
        str(segment_time),
        "-reset_timestamps",
        "1",
        out_pattern,
    ]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    chunks = [os.path.join(out_dir, f) for f in sorted(os.listdir(out_dir)) if f.startswith("chunk_")]
    return chunks


def run_transcribe_audio(
    audio_path: str,
    output_subtitle: str,
    provider: str = "local",
    model_size: str = "large",
    language: str = "ko",
    api_model: str = "whisper-1",
    max_upload_bytes: int = 24_500_000,
    segment_time: int = 600,
) -> bool:
    """Transcribe an audio file and normalize the resulting SRT file."""
    try:
        # Reset informational quota flag at the start of each run to avoid leaking across calls/tests.
        # Whether to short-circuit is decided by a per-audio marker file instead.
        global _quota_blocked
        _quota_blocked = False

        if not os.path.exists(audio_path):
            logging.error(f"Audio file not found for transcription: {audio_path}")
            return False

        # Fast-path: if the final SRT already exists and is non-empty, skip re-transcribing
        try:
            if os.path.exists(output_subtitle) and os.path.getsize(output_subtitle) > 0:
                logging.info("Skipping transcription: output already exists at %s", output_subtitle)
                return True
        except Exception:
            # Continue if we can't stat the file for any reason
            pass

        if provider == "openai":
            size = os.path.getsize(audio_path)
            openai = importlib.import_module("openai")
            client = openai.OpenAI()
            os.makedirs(os.path.dirname(output_subtitle), exist_ok=True)

            def _transcribe_file_with_retry(fobj, attempts: int = 5):
                delay = 0.5
                for i in range(attempts):
                    try:
                        return client.audio.transcriptions.create(
                            model=api_model,
                            file=fobj,
                            language=language,
                            response_format="srt",
                        )
                    except Exception as ex:
                        txt = str(ex)
                        if (
                            "insufficient_quota" in txt
                            or " 429" in txt
                            or "quota" in txt
                        ):
                            logging.error(
                                "OpenAI transcription quota exceeded detected; skipping further transcriptions this run. (%s)",
                                txt,
                            )
                            _mark_quota_exceeded()
                            # Create a per-audio marker so subsequent attempts for the same
                            # audio can short-circuit without calling the API again.
                            try:
                                marker = _quota_marker_path(audio_path)
                                open(marker, "w").close()
                            except Exception:
                                pass
                            raise
                        if i == attempts - 1:
                            raise
                        logging.warning(
                            "OpenAI transcription error (%s); retrying in %.1fs (%d/%d)",
                            ex,
                            delay,
                            i + 1,
                            attempts,
                        )
                        time.sleep(delay)
                        delay = min(delay * 2, 10.0)

            if size <= max_upload_bytes:
                # If a prior attempt for this same audio hit quota, short-circuit now.
                if os.path.exists(_quota_marker_path(audio_path)):
                    logging.warning(
                        "OpenAI transcription skipped: prior quota exceeded for this audio; skipping retry."
                    )
                    return False
                with open(audio_path, "rb") as f:
                    srt_text = _transcribe_file_with_retry(f)
                with open(output_subtitle, "w", encoding="utf-8") as out:
                    out.write(srt_text)
                logging.info(f"Subtitles saved to {output_subtitle}")
                return True
            else:
                logging.info(
                    "Audio size %d exceeds threshold %d; segmenting for chunked transcription",
                    size,
                    max_upload_bytes,
                )
                # Use a persistent chunk directory next to the audio file so repeated runs can reuse chunks
                stem = os.path.splitext(os.path.basename(audio_path))[0]
                persistent_dir = os.path.join(os.path.dirname(audio_path), f"{stem}_chunks")
                os.makedirs(persistent_dir, exist_ok=True)
                existing_chunks = [
                    os.path.join(persistent_dir, f)
                    for f in sorted(os.listdir(persistent_dir))
                    if f.startswith("chunk_") and f.endswith(".mp3")
                ]
                if existing_chunks:
                    logging.info(
                        "Reusing %d existing chunks from %s (skip re-segmentation)",
                        len(existing_chunks),
                        persistent_dir,
                    )
                    chunks = existing_chunks
                else:
                    try:
                        chunks = _ffmpeg_segment(
                            audio_path, persistent_dir, segment_time=segment_time
                        )
                    except Exception as e:
                        logging.error(f"Failed to segment audio: {e}")
                        return False

                # If we would need to call the API for any chunk and a prior attempt
                # for this audio hit quota, short-circuit before making API calls.
                marker_exists = os.path.exists(_quota_marker_path(audio_path))
                if marker_exists:
                    needs_api = False
                    for ch in chunks:
                        srt_cache = os.path.splitext(ch)[0] + ".srt"
                        if not (os.path.exists(srt_cache) and os.path.getsize(srt_cache) > 0):
                            needs_api = True
                            break
                    if needs_api:
                        logging.warning(
                            "OpenAI transcription skipped: prior quota exceeded for this audio; missing chunk caches."
                        )
                        return False

                merged_srt_lines = []
                for idx, ch in enumerate(chunks):
                    try:
                        # If we already transcribed this chunk before, reuse cached SRT next to the chunk file
                        srt_cache = os.path.splitext(ch)[0] + ".srt"
                        if os.path.exists(srt_cache) and os.path.getsize(srt_cache) > 0:
                            logging.info(
                                "Reusing cached transcription for %s (%d/%d)", ch, idx + 1, len(chunks)
                            )
                            with open(srt_cache, "r", encoding="utf-8") as cf:
                                part_srt = cf.read()
                        else:
                            with open(ch, "rb") as f:
                                part_srt = _transcribe_file_with_retry(f)
                            # Persist per‑chunk SRT cache for future runs
                            try:
                                with open(srt_cache, "w", encoding="utf-8") as cf:
                                    cf.write(part_srt)
                            except Exception:
                                # Cache write failure shouldn't abort the run
                                pass
                    except Exception as e:
                        logging.error(
                            "Transcription failed for chunk %d/%d (%s): %s",
                            idx + 1,
                            len(chunks),
                            ch,
                            e,
                        )
                        return False
                    offset = idx * float(segment_time)
                    shifted = _shift_srt(part_srt, offset)
                    merged_srt_lines.append(shifted)

                with open(output_subtitle, "w", encoding="utf-8") as out:
                    full = "".join(merged_srt_lines)
                    blocks = _parse_srt(full)
                    for i, (_, start, end, text) in enumerate(blocks, start=1):
                        out.write(f"{i}\n{start} --> {end}\n{text}\n\n")
                logging.info(f"Subtitles saved to {output_subtitle}")
                return True

        elif provider == "local":
            transcribe_audio_local(
                audio_path, output_subtitle, model_size, language
            )
            # Normalize only for local to preserve API SRT formatting
            run_normalize_srt(output_subtitle, output_subtitle)
            logging.info(f"Subtitles saved to {output_subtitle}")
            return True
        else:
            logging.error(f"Unsupported transcription provider: {provider}")
            return False
    except Exception as e:
        logging.error(f"Transcription failed for {audio_path}: {e}")
        return False

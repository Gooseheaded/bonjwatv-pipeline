import importlib
import os
import sys
from unittest.mock import MagicMock

sys.path.insert(0, os.getcwd())

import pytest

from transcribe_audio import format_timestamp, run_transcribe_audio
import transcribe_audio as stt


class DummyModel:
    def __init__(self, name, device=None):
        self.name = name

    def transcribe(self, audio_path, language=None):
        return {
            "segments": [
                {"start": 0.5, "end": 1.25, "text": "Hello"},
                {"start": 1.25, "end": 2.0, "text": "World"},
            ]
        }


class DummyWhisperModule:
    def load_model(self, model_size):
        return DummyModel(model_size)


@pytest.fixture
def mock_imports(monkeypatch):
    """Mock imports for both whisper and openai."""
    mock_whisper = DummyWhisperModule()

    mock_srt_response = "1\n00:00:00,500 --> 00:00:01,250\nHello\n\n2\n00:00:01,250 --> 00:00:02,000\nWorld\n\n"
    mock_client = MagicMock()
    mock_client.audio.transcriptions.create.return_value = mock_srt_response
    mock_openai_module = MagicMock()
    mock_openai_module.OpenAI.return_value = mock_client

    original_import = importlib.import_module

    def mock_import_module(name):
        if name == "whisper":
            return mock_whisper
        if name == "openai":
            return mock_openai_module
        return original_import(name)

    monkeypatch.setattr(importlib, "import_module", mock_import_module)
    monkeypatch.setattr("transcribe_audio.load_dotenv", lambda: None)
    return mock_client


def test_format_timestamp():
    assert format_timestamp(0) == "00:00:00,000"
    assert format_timestamp(1.2) == "00:00:01,200"
    assert format_timestamp(3661.015) == "01:01:01,015"


def test_transcribe_audio_local(tmp_path, mock_imports):
    audio = tmp_path / "audio.mp3"
    audio.write_text("", encoding="utf-8")
    output_srt = tmp_path / "out.srt"

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="local",
        model_size="dummy",
        language="ko",
    )
    assert ok is True
    assert output_srt.exists()
    content = output_srt.read_text(encoding="utf-8")
    assert "1" in content and "Hello" in content
    assert "2" in content and "World" in content


def test_transcribe_audio_openai(tmp_path, mock_imports):
    audio = tmp_path / "audio.mp3"
    audio.write_text("", encoding="utf-8")
    output_srt = tmp_path / "out.srt"

    mock_client = mock_imports
    mock_srt_response = mock_client.audio.transcriptions.create.return_value

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="gpt-4o-transcribe",
        language="ko",
    )
    assert ok is True
    assert output_srt.exists()
    content = output_srt.read_text(encoding="utf-8")
    assert content.strip() == mock_srt_response.strip()

    mock_client.audio.transcriptions.create.assert_called_once()
    _, call_kwargs = mock_client.audio.transcriptions.create.call_args
    assert call_kwargs["model"] == "gpt-4o-transcribe"
    assert call_kwargs["language"] == "ko"
    assert call_kwargs["response_format"] == "srt"


def test_transcribe_audio_openai_chunking(tmp_path, monkeypatch, mock_imports):
    # Create a big "audio" file to exceed threshold
    audio = tmp_path / "big.mp3"
    audio.write_bytes(b"0" * (25_000_000))
    output_srt = tmp_path / "out.srt"

    # Monkeypatch segmenter to avoid calling ffmpeg; return two chunk paths
    chunks_dir = tmp_path / "chunks"
    chunks_dir.mkdir()
    c1 = chunks_dir / "chunk_000.mp3"
    c2 = chunks_dir / "chunk_001.mp3"
    c1.write_text("dummy", encoding="utf-8")
    c2.write_text("dummy", encoding="utf-8")

    def fake_segment(path, out_dir, segment_time=600, bitrate="64k"):
        return [str(c1), str(c2)]

    monkeypatch.setattr("transcribe_audio._ffmpeg_segment", fake_segment)

    # Mock OpenAI to return simple SRT per chunk with timestamps starting at 0
    mock_client = mock_imports
    mock_client.audio.transcriptions.create.side_effect = [
        "1\n00:00:00,500 --> 00:00:01,000\nA\n\n",
        "1\n00:00:00,000 --> 00:00:00,500\nB\n\n",
    ]

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
        max_upload_bytes=1,  # force chunking by using tiny threshold
        segment_time=1,
    )
    assert ok is True
    data = output_srt.read_text(encoding="utf-8")
    # Expect two lines, renumbered 1..2 and shifted offsets
    assert "1\n00:00:00,500 --> 00:00:01,000\nA" in data
    assert "2\n00:00:01,000 --> 00:00:01,500\nB" in data


def test_transcribe_audio_missing_file(tmp_path):
    ok = run_transcribe_audio(
        audio_path="no_such.mp3", output_subtitle=str(tmp_path / "out.srt")
    )
    assert ok is False
def test_transcribe_audio_openai_retries_success(tmp_path, mock_imports):
    audio = tmp_path / "audio.mp3"
    audio.write_text("", encoding="utf-8")
    output_srt = tmp_path / "out.srt"

    mock_client = mock_imports
    # Fail twice, then succeed
    mock_client.audio.transcriptions.create.side_effect = [
        Exception("500"),
        Exception("500"),
        "1\n00:00:00,500 --> 00:00:01,000\nOK\n\n",
    ]

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
    )
    assert ok is True
    assert output_srt.exists()
    assert "OK" in output_srt.read_text(encoding="utf-8")


def test_transcribe_audio_openai_retries_fail(tmp_path, mock_imports):
    audio = tmp_path / "audio.mp3"
    audio.write_text("", encoding="utf-8")
    output_srt = tmp_path / "out.srt"

    mock_client = mock_imports
    # Always fail
    mock_client.audio.transcriptions.create.side_effect = [Exception("500")] * 6

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
    )
    assert ok is False
    assert not output_srt.exists()


def test_transcribe_audio_openai_insufficient_quota_short_circuit(tmp_path, mock_imports, monkeypatch):
    audio = tmp_path / "audio.mp3"
    audio.write_text("", encoding="utf-8")
    out1 = tmp_path / "out1.srt"
    out2 = tmp_path / "out2.srt"

    mock_client = mock_imports
    # First attempt triggers quota error (429 insufficient_quota)
    mock_client.audio.transcriptions.create.side_effect = [
        Exception("Error code: 429 - {\"error\": {\"code\": \"insufficient_quota\"}}")
    ]

    ok1 = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(out1),
        provider="openai",
        api_model="whisper-1",
        language="ko",
    )
    assert ok1 is False
    assert stt.quota_blocked() is True

    # Subsequent call should short-circuit without calling API again
    calls_before = mock_client.audio.transcriptions.create.call_count
    ok2 = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(out2),
        provider="openai",
        api_model="whisper-1",
        language="ko",
    )
    calls_after = mock_client.audio.transcriptions.create.call_count
    assert ok2 is False
    assert calls_after == calls_before


def test_transcribe_audio_openai_reuse_existing_chunks_skips_segment(tmp_path, mock_imports, monkeypatch):
    # Create a big audio to trigger chunking
    audio = tmp_path / "big.mp3"
    audio.write_bytes(b"0" * (25_000_000))
    output_srt = tmp_path / "out.srt"

    # Precreate persistent chunks next to audio so we reuse them
    chunks_dir = tmp_path / "big_chunks"
    chunks_dir.mkdir()
    c1 = chunks_dir / "chunk_000.mp3"
    c2 = chunks_dir / "chunk_001.mp3"
    c1.write_bytes(b"dummy")
    c2.write_bytes(b"dummy")

    # Track if ffmpeg segmenter gets called (it should not)
    called = {"v": False}

    def fake_segment(path, out_dir, segment_time=600, bitrate="64k"):
        called["v"] = True
        return [str(c1), str(c2)]

    monkeypatch.setattr("transcribe_audio._ffmpeg_segment", fake_segment)

    # Mock API to return simple srt for each chunk
    mock_client = mock_imports
    mock_client.audio.transcriptions.create.side_effect = [
        "1\n00:00:00,000 --> 00:00:00,500\nX\n\n",
        "1\n00:00:00,500 --> 00:00:01,000\nY\n\n",
    ]

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
        max_upload_bytes=1,
        segment_time=1,
    )
    assert ok is True
    assert called["v"] is False  # did not call segmenter
    assert output_srt.exists()


def test_transcribe_audio_openai_reuse_chunk_srt_cache_skips_api(tmp_path, mock_imports):
    # Create big audio and matching persistent chunk directory with cached SRTs
    audio = tmp_path / "big2.mp3"
    audio.write_bytes(b"0" * (25_000_000))
    output_srt = tmp_path / "out2.srt"
    chunks_dir = tmp_path / "big2_chunks"
    chunks_dir.mkdir()
    c1 = chunks_dir / "chunk_000.mp3"
    c2 = chunks_dir / "chunk_001.mp3"
    s1 = chunks_dir / "chunk_000.srt"
    s2 = chunks_dir / "chunk_001.srt"
    c1.write_bytes(b"dummy")
    c2.write_bytes(b"dummy")
    s1.write_text("1\n00:00:00,000 --> 00:00:00,500\nA\n\n", encoding="utf-8")
    s2.write_text("1\n00:00:00,500 --> 00:00:01,000\nB\n\n", encoding="utf-8")

    mock_client = mock_imports

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
        max_upload_bytes=1,
        segment_time=1,
    )
    assert ok is True
    # With per-chunk srt caches present, we shouldn't hit the API
    assert mock_client.audio.transcriptions.create.call_count == 0
    data = output_srt.read_text(encoding="utf-8")
    assert "A" in data and "B" in data


def test_transcribe_audio_skip_when_output_exists(tmp_path, mock_imports, monkeypatch):
    audio = tmp_path / "skip.mp3"
    audio.write_text("", encoding="utf-8")
    output_srt = tmp_path / "out_existing.srt"
    output_srt.write_text("1\n00:00:00,000 --> 00:00:00,500\nDONE\n\n", encoding="utf-8")

    # Ensure neither segmenter nor API are used
    called = {"v": False}

    def fake_segment(path, out_dir, segment_time=600, bitrate="64k"):
        called["v"] = True
        return []

    monkeypatch.setattr("transcribe_audio._ffmpeg_segment", fake_segment)
    mock_client = mock_imports

    ok = run_transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        provider="openai",
        api_model="whisper-1",
        language="ko",
    )
    assert ok is True
    assert called["v"] is False
    assert mock_client.audio.transcriptions.create.call_count == 0

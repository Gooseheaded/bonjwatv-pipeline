import importlib
import os
import sys
from unittest.mock import MagicMock

sys.path.insert(0, os.getcwd())

import pytest

from transcribe_audio import format_timestamp, run_transcribe_audio


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


def test_transcribe_audio_missing_file(tmp_path):
    ok = run_transcribe_audio(
        audio_path="no_such.mp3", output_subtitle=str(tmp_path / "out.srt")
    )
    assert ok is False

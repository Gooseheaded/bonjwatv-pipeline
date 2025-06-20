import os
import sys
import importlib

sys.path.insert(0, os.getcwd())

import pytest

from transcribe_audio import transcribe_audio, format_timestamp


class DummyModel:
    def __init__(self, name, device=None):
        self.name = name

    def transcribe(self, audio_path, language=None):
        return {'segments': [
            {'start': 0.5, 'end': 1.25, 'text': 'Hello'},
            {'start': 1.25, 'end': 2.0, 'text': 'World'},
        ]}


class DummyWhisperModule:
    def load_model(self, model_size):
        return DummyModel(model_size)


def test_format_timestamp():
    assert format_timestamp(0) == '00:00:00,000'
    assert format_timestamp(1.2) == '00:00:01,200'
    assert format_timestamp(3661.015) == '01:01:01,015'


def test_transcribe_audio(tmp_path, monkeypatch, caplog):
    # Create dummy audio file
    audio = tmp_path / 'audio.mp3'
    audio.write_text('', encoding='utf-8')

    output_srt = tmp_path / 'out.srt'

    # Monkeypatch importlib to return dummy whisper module
    monkeypatch.setattr(importlib, 'import_module', lambda name: DummyWhisperModule())

    # Run transcription
    result = transcribe_audio(
        audio_path=str(audio),
        output_subtitle=str(output_srt),
        model_size='dummy',
        language='ko'
    )

    assert result == str(output_srt)
    assert output_srt.exists()
    content = output_srt.read_text(encoding='utf-8')
    # Check indices and text
    assert '1' in content and 'Hello' in content
    assert '2' in content and 'World' in content

    # Error on missing file
    with pytest.raises(FileNotFoundError):
        transcribe_audio(audio_path='no_such.mp3', output_subtitle=str(output_srt), model_size='dummy', language='ko')
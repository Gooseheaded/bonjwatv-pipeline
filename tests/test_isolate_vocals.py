import os
import sys

sys.path.insert(0, os.getcwd())


from isolate_vocals import run_isolate_vocals


def test_skip_existing(tmp_path, caplog):
    # Prepare dummy input and existing output
    audio_dir = tmp_path / "audio"
    audio_dir.mkdir()
    input_file = audio_dir / "vid.mp3"
    input_file.write_text("", encoding="utf-8")

    vocals_dir = tmp_path / "vocals" / "vid"
    vocals_dir.mkdir(parents=True)
    existing = vocals_dir / "vocals.wav"
    existing.write_text("", encoding="utf-8")

    caplog.set_level("INFO")
    ok = run_isolate_vocals(
        input_file=str(input_file),
        output_dir=str(tmp_path / "vocals"),
        model="dummy",
        two_stems=False,
    )
    assert ok is True
    assert "already exists" in caplog.text


def test_isolate_creates_file(tmp_path, monkeypatch):
    # Prepare dummy input
    audio_dir = tmp_path / "audio"
    audio_dir.mkdir()
    input_file = audio_dir / "vid.mp3"
    input_file.write_text("", encoding="utf-8")

    output_dir = tmp_path / "vocals"

    calls = {}

    def fake_run(cmd, check):
        # Simulate Demucs invocation and file creation
        calls["cmd"] = cmd
        vocals_path = output_dir / "vid" / "vocals.wav"
        vocals_path.parent.mkdir(parents=True, exist_ok=True)
        vocals_path.write_text("dummy", encoding="utf-8")

    monkeypatch.setattr("isolate_vocals.subprocess.run", fake_run)

    ok = run_isolate_vocals(
        input_file=str(input_file),
        output_dir=str(output_dir),
        model="demucs-model",
        two_stems=True,
    )
    # Verify subprocess command includes demucs and flags
    assert calls["cmd"][0] == "demucs"
    assert "--two-stems" in calls["cmd"]
    # Verify output path and return value
    assert ok is True
    out_path = output_dir / "vid" / "vocals.wav"
    assert os.path.exists(out_path)

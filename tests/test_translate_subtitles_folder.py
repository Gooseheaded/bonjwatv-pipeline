import os
import sys
from pathlib import Path

import pytest

import translate_subtitles_folder


def test_translate_all_srt_files(tmp_path, monkeypatch):
    # Prepare input directory with nested structure and sample files
    input_dir = tmp_path / "mini"
    subdir = input_dir / "pvp"
    subdir.mkdir(parents=True)
    # Create .srt file and a non-.srt file
    file1 = subdir / "a.srt"
    file1.write_text("1\n00:00:00,000 --> 00:00:01,000\nHello\n", encoding="utf-8")
    (subdir / "ignore.txt").write_text("skip me", encoding="utf-8")
    file2 = input_dir / "c.srt"
    file2.write_text("1\n00:00:02,000 --> 00:00:03,000\nWorld\n", encoding="utf-8")

    # Intercept calls to parse_srt_file and translate_srt_file to avoid real work
    proc_calls = []

    def fake_process(input_file, output_file):
        proc_calls.append((input_file, output_file))
        os.makedirs(os.path.dirname(output_file), exist_ok=True)
        Path(output_file).write_text("cleaned", encoding="utf-8")

    monkeypatch.setattr(
        translate_subtitles_folder, "run_normalize_srt", lambda inp, out: fake_process(inp, out)
    )

    trans_calls = []

    def fake_translate(
        input_file, output_file, slang_file, chunk_size, overlap, cache_dir, model
    ):
        trans_calls.append(
            (input_file, output_file, slang_file, chunk_size, overlap, cache_dir, model)
        )
        os.makedirs(os.path.dirname(output_file), exist_ok=True)
        Path(output_file).write_text("translated", encoding="utf-8")

    monkeypatch.setattr(
        translate_subtitles_folder,
        "run_translate_subtitles",
        lambda **kwargs: fake_translate(
            kwargs["input_file"],
            kwargs["output_file"],
            kwargs["slang_file"],
            kwargs["chunk_size"],
            kwargs["overlap"],
            kwargs["cache_dir"],
            kwargs["model"],
        )
        or True,
    )

    # Run the folder translation script with custom flags
    out_dir = tmp_path / "out"
    sys.argv = [
        "prog",
        "--input-dir",
        str(input_dir),
        "--output-dir",
        str(out_dir),
        "--slang-file",
        "my_slang.txt",
        "--chunk-size",
        "10",
        "--overlap",
        "2",
        "--cache-dir",
        "my_cache",
        "--model",
        "test-model",
    ]
    translate_subtitles_folder.main()

    # Expected post-processing and translation calls for .srt inputs only
    # Order: leaf directories first (a.srt), then top-level (c.srt)
    rel1 = str(file1)
    rel2 = str(file2)
    # Processed paths should be under the temp clean root; suffix matches original filenames
    assert proc_calls[0][0] == rel1
    assert proc_calls[1][0] == rel2
    assert proc_calls[0][1].endswith(os.path.join("pvp", "a.srt"))
    assert proc_calls[1][1].endswith(os.path.join("c.srt"))

    # Translate should consume the cleaned outputs
    expected_out1 = str(out_dir / "pvp" / "en_a.srt")
    expected_out2 = str(out_dir / "en_c.srt")
    assert len(trans_calls) == 2
    # translation input_file args should match the clean path produced above
    assert trans_calls[0][0] == proc_calls[0][1]
    assert trans_calls[1][0] == proc_calls[1][1]
    # translation output paths are correct
    assert trans_calls[0][1] == expected_out1
    assert trans_calls[1][1] == expected_out2


def test_missing_required_args_shows_usage(monkeypatch, capsys):
    # Invoking without required flags should exit with usage message
    monkeypatch.setattr(sys, "argv", ["prog"])
    with pytest.raises(SystemExit):
        translate_subtitles_folder.main()
    captured = capsys.readouterr()
    assert "usage" in captured.err.lower() or "usage" in captured.out.lower()

import os
import sys
sys.path.insert(0, os.getcwd())
import pytest

from translate_subtitles import (
    parse_srt_file,
    chunk_subtitles,
    merge_chunks,
    translate_srt_file,
    call_openai_api,
)

SRT_SAMPLE = """1
00:00:00,000 --> 00:00:01,000
Line A

2
00:00:01,000 --> 00:00:02,000
Line B

3
00:00:02,000 --> 00:00:03,000
Line C
"""


def test_chunk_and_merge(tmp_path):
    srt_file = tmp_path / "sample.srt"
    srt_file.write_text(SRT_SAMPLE, encoding="utf-8")
    subs = parse_srt_file(str(srt_file))
    chunks = chunk_subtitles(subs, chunk_size=2, overlap=1)
    assert len(chunks) == 2
    merged = merge_chunks(chunks, overlap=1)
    assert len(merged) == 3
    assert [s.index for s in merged] == [1, 2, 3]


def test_translate_srt_file(tmp_path, monkeypatch):
    # Prepare test files
    srt_file = tmp_path / "kr_sample.srt"
    srt_file.write_text(SRT_SAMPLE, encoding="utf-8")
    slang_file = tmp_path / "KoreanSlang.txt"
    slang_file.write_text("", encoding="utf-8")

    # Stub OpenAI API call to return only the chunk SRT text
    def fake_call(prompt, model=None, temperature=None):
        parts = prompt.split("---\n")
        return parts[1] if len(parts) > 1 else prompt

    monkeypatch.setenv("OPENAI_API_KEY", "testkey")
    monkeypatch.setattr("translate_subtitles.call_openai_api", fake_call)

    output_file = tmp_path / "en_sample.srt"
    translate_srt_file(
        input_file=str(srt_file),
        output_file=str(output_file),
        slang_file=str(slang_file),
        chunk_size=10,
        overlap=0,
        cache_dir=str(tmp_path / "cache"),
        model="test-model",
    )

    assert output_file.exists()
    text = output_file.read_text(encoding="utf-8")
    assert "Line A" in text
    assert "Line B" in text
    assert "Line C" in text
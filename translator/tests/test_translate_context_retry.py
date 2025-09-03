import os
import sys

sys.path.insert(0, os.getcwd())


from translate_subtitles import ContextLengthError, run_translate_subtitles

SRT_SAMPLE = """
1
00:00:00,000 --> 00:00:01,000
Line A

2
00:00:01,000 --> 00:00:02,000
Line B

3
00:00:02,000 --> 00:00:03,000
Line C

4
00:00:03,000 --> 00:00:04,000
Line D

5
00:00:04,000 --> 00:00:05,000
Line E
"""


def test_context_length_retry(tmp_path, monkeypatch, caplog):
    # Create a sample SRT file with enough lines to force multiple chunks
    input_srt = tmp_path / "kr_test.srt"
    input_srt.write_text(SRT_SAMPLE, encoding="utf-8")
    slang_file = tmp_path / "KoreanSlang.txt"
    slang_file.write_text("", encoding="utf-8")

    # Prepare cache dir and output srt
    cache_dir = tmp_path / ".cache"
    cache_dir.mkdir()
    output_srt = tmp_path / "en_test.srt"

    # Monkeypatch call_openai_api to raise ContextLengthError once, then return SRT text
    calls = {"count": 0}

    def fake_call(prompt, model=None, temperature=None):
        calls["count"] += 1
        if calls["count"] == 1:
            raise ContextLengthError("Exceeded")
        # return the prompt part after '---\n'
        parts = prompt.split("---\n")
        return parts[1] if len(parts) > 1 else prompt

    monkeypatch.setenv("OPENAI_API_KEY", "testkey")
    monkeypatch.setattr("translate_subtitles.call_openai_api", fake_call)
    caplog.set_level("WARNING")

    # Run translation with default chunk size to allow retry loop
    ok = run_translate_subtitles(
        input_file=str(input_srt),
        output_file=str(output_srt),
        slang_file=str(slang_file),
        chunk_size=50,
        overlap=0,
        cache_dir=str(cache_dir),
        model="test-model",
    )
    assert ok is True

    # Should have retried once
    assert calls["count"] > 1
    # Warning about context-length should be logged
    assert "reducing chunk_size" in caplog.text
    # Output should exist and contain all lines
    text = output_srt.read_text(encoding="utf-8")
    assert "Line A" in text and "Line E" in text

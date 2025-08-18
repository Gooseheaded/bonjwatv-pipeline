import os
import sys

sys.path.insert(0, os.getcwd())

from normalize_srt import normalize_timestamp, run_normalize_srt

SAMPLE_RAW = """1
59.9 --> 69.9
Hello

2
00:01:09.900 --> 00:01:19.900
Hello

3
00:01:20,000 --> 00:01:21,000
World
"""


def test_parse_srt_file(tmp_path):
    # Write a sample raw SRT with mixed formats and duplicate lines
    raw = tmp_path / "raw.srt"
    raw.write_text(SAMPLE_RAW, encoding="utf-8")
    clean = tmp_path / "clean.srt"

    # Run the post-processing step
    assert run_normalize_srt(str(raw), str(clean))

    assert clean.exists()
    text = clean.read_text(encoding="utf-8")
    # Check normalization: first timestamp becomes full HH:MM:SS,mmm
    assert "00:00:59,900 --> 00:01:19,900" in text
    # Duplicate 'Hello' blocks collapsed to one
    assert text.count("Hello") == 1
    # 'World' remains
    assert "World" in text


def test_normalize_timestamp_overflow():
    # No overflow remains unchanged except zero-padding
    assert normalize_timestamp("00:00:10,005") == "00:00:10,005"
    assert normalize_timestamp("1:2:3.4") == "01:02:03,400"

    # Carry 60 seconds to minutes
    assert normalize_timestamp("00:00:60,000") == "00:01:00,000"

    # Carry seconds overflow within minutes
    assert normalize_timestamp("00:00:125,030") == "00:02:05,030"

    # Overflow multiple levels, minutes and seconds
    assert normalize_timestamp("00:65:70,700") == "01:06:10,700"

    # Excessive seconds to hours
    assert normalize_timestamp("1:2:3600,123") == "02:02:00,123"

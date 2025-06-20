import os
import sys

sys.path.insert(0, os.getcwd())

from whisper_postprocess import process_srt_file

SAMPLE_RAW = '''1
59.9 --> 69.9
Hello

2
00:01:09.900 --> 00:01:19.900
Hello

3
00:01:20,000 --> 00:01:21,000
World
'''


def test_process_srt_file(tmp_path):
    # Write a sample raw SRT with mixed formats and duplicate lines
    raw = tmp_path / 'raw.srt'
    raw.write_text(SAMPLE_RAW, encoding='utf-8')
    clean = tmp_path / 'clean.srt'

    # Run the post-processing step
    process_srt_file(str(raw), str(clean))

    assert clean.exists()
    text = clean.read_text(encoding='utf-8')
    # Check normalization: first timestamp becomes full HH:MM:SS,mmm
    assert '00:00:59,900 --> 00:01:19,900' in text
    # Duplicate 'Hello' blocks collapsed to one
    assert text.count('Hello') == 1
    # 'World' remains
    assert 'World' in text
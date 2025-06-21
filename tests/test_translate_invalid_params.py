import pytest

from translate_subtitles import translate_srt_file, ContextLengthError


def test_invalid_chunk_overlap(tmp_path):
    # Create a dummy input SRT
    srt = tmp_path / 'kr.srt'
    srt.write_text('1\n00:00:00,000 --> 00:00:01,000\nHello\n', encoding='utf-8')
    slang = tmp_path / 'slang.txt'
    slang.write_text('', encoding='utf-8')

    # overlap equal to chunk_size should error
    with pytest.raises(ValueError):
        translate_srt_file(
            input_file=str(srt),
            output_file=str(tmp_path / 'en.srt'),
            slang_file=str(slang),
            chunk_size=5,
            overlap=5,
            cache_dir=str(tmp_path / 'cache'),
            model='test'
        )

    # chunk_size below overlap should error
    with pytest.raises(ValueError):
        translate_srt_file(
            input_file=str(srt),
            output_file=str(tmp_path / 'en.srt'),
            slang_file=str(slang),
            chunk_size=4,
            overlap=5,
            cache_dir=str(tmp_path / 'cache'),
            model='test'
        )

    # chunk_size <= 0 should error
    with pytest.raises(ValueError):
        translate_srt_file(
            input_file=str(srt),
            output_file=str(tmp_path / 'en.srt'),
            slang_file=str(slang),
            chunk_size=0,
            overlap=0,
            cache_dir=str(tmp_path / 'cache'),
            model='test'
        )
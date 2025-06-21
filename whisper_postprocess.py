#!/usr/bin/env python3
import os
import re
import argparse


class Subtitle:
    def __init__(self, index: int, start: str, end: str, lines: list):
        self.index = index
        self.start = start
        self.end = end
        self.lines = lines

    def to_srt_block(self) -> str:
        return f"{self.index}\n{self.start} --> {self.end}\n" + "\n".join(self.lines) + "\n"


def parse_srt_file(path: str) -> list:
    with open(path, encoding='utf-8-sig') as f:
        content = f.read()
    pattern = re.compile(
        r'(\d+)\s+(.*?) --> (.*?)\s+([\s\S]*?)(?=\n\n|\Z)',
        re.MULTILINE
    )
    subs = []
    for m in pattern.finditer(content):
        idx = int(m.group(1))
        subs.append(Subtitle(idx, m.group(2).strip(), m.group(3).strip(), m.group(4).strip().split('\n')))
    return subs


def normalize_timestamp(ts: str) -> str:
    """
    Normalize a timestamp string (e.g. "MM:SS,ms" or with overflow) into
    a semantically correct HH:MM:SS,mmm format, carrying overflow across units.
    """
    ts = ts.strip()
    # Split milliseconds
    if ',' in ts:
        base, ms = ts.split(',', 1)
    elif '.' in ts:
        base, ms = ts.split('.', 1)
    else:
        base, ms = ts, '000'
    # Exactly three digits of milliseconds
    ms = (ms + '000')[:3]
    # Parse hours/minutes/seconds components (allow variable lengths)
    parts = [int(p) for p in base.split(':')]
    if len(parts) == 3:
        h, m, s = parts
    elif len(parts) == 2:
        h, m, s = 0, parts[0], parts[1]
    else:
        h, m, s = 0, 0, parts[0]
    # Compute total milliseconds and re-split to correct overflow
    total_ms = ((h * 3600 + m * 60 + s) * 1000) + int(ms)
    total_sec, ms = divmod(total_ms, 1000)
    h, rem_sec = divmod(total_sec, 3600)
    m, s = divmod(rem_sec, 60)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def collapse_subtitles(subs: list) -> list:
    if not subs:
        return []
    merged = [subs[0]]
    for sub in subs[1:]:
        last = merged[-1]
        if sub.lines == last.lines:
            last.end = sub.end
        else:
            merged.append(sub)
    return merged


def write_srt_file(path: str, subs: list) -> None:
    with open(path, 'w', encoding='utf-8') as f:
        for sub in subs:
            f.write(sub.to_srt_block())
            f.write('\n')


def process_srt_file(input_file: str, output_file: str) -> None:
    subs = parse_srt_file(input_file)
    for sub in subs:
        sub.start = normalize_timestamp(sub.start)
        sub.end = normalize_timestamp(sub.end)
    cleaned = collapse_subtitles(subs)
    for i, sub in enumerate(cleaned, 1):
        sub.index = i
    os.makedirs(os.path.dirname(output_file), exist_ok=True)
    write_srt_file(output_file, cleaned)


def main():
    p = argparse.ArgumentParser(description='Post-process Whisper SRT output')
    p.add_argument('--input-file', required=True)
    p.add_argument('--output-file', required=True)
    args = p.parse_args()
    process_srt_file(args.input_file, args.output_file)


if __name__ == '__main__':
    main()
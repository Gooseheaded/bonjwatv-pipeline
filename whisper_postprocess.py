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
    ts = ts.strip()
    # Split milliseconds
    if ',' in ts:
        base, ms = ts.split(',', 1)
    elif '.' in ts:
        base, ms = ts.split('.', 1)
    else:
        base, ms = ts, '000'
    ms = (ms + '000')[:3]
    parts = base.split(':')
    if len(parts) == 3:
        hh, mm, ss = parts
    elif len(parts) == 2:
        hh = '00'
        mm, ss = parts
    else:
        hh, mm, ss = '00', '00', parts[0]
    hh = hh.zfill(2)
    mm = mm.zfill(2)
    ss = ss.zfill(2)
    return f"{hh}:{mm}:{ss},{ms}"


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
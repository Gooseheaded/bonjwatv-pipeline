import json
import logging  # Added for logging
import os
import re
import time

import openai
from dotenv import load_dotenv
from openai import OpenAI, RateLimitError


class ContextLengthError(Exception):
    """Raised when the OpenAI API reports context-length exceeded."""


class Subtitle:
    """Lightweight container for a single SRT subtitle block."""

    def __init__(self, index: int, start: str, end: str, lines: list):
        self.index = index
        self.start = start
        self.end = end
        self.lines = lines

    def to_srt_block(self) -> str:
        """Serialize the subtitle back to an SRT-formatted block."""
        return (
            f"{self.index}\n{self.start} --> {self.end}\n"
            + "\n".join(self.lines)
            + "\n"
        )


def parse_srt_file(filename: str) -> list:
    """Parse an SRT file path into a list of Subtitle objects."""
    with open(filename, encoding="utf-8-sig") as f:
        content = f.read()
    pattern = re.compile(
        r"(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)",
        re.MULTILINE,
    )
    subs = []
    for m in pattern.finditer(content):
        subs.append(
            Subtitle(
                int(m.group(1)), m.group(2), m.group(3), m.group(4).strip().split("\n")
            )
        )
    return subs


def write_srt_file(filename: str, subtitles: list) -> None:
    """Write a list of Subtitle objects to disk as an SRT file."""
    with open(filename, "w", encoding="utf-8") as f:
        for sub in subtitles:
            f.write(sub.to_srt_block())
            f.write("\n")


def chunk_subtitles(subs: list, chunk_size: int, overlap: int) -> list:
    """Split subtitles into overlapping chunks for translation."""
    if chunk_size <= overlap:
        raise ValueError("chunk_size must be greater than overlap")
    chunks = []
    step = chunk_size - overlap
    for i in range(0, len(subs), step):
        chunk = subs[i : i + chunk_size]
        if not chunk:
            break
        if len(chunk) < chunk_size and i > 0:
            break
        chunks.append(chunk)
    return chunks


def build_prompt(chunk: list, slang_text: str) -> str:
    """Build a translation prompt with SRT content and glossary."""
    srt_content = ""
    for sub in chunk:
        srt_content += sub.to_srt_block() + "\n"
    return (
        "You are translating Korean StarCraft: Brood War subtitles to English.\n"
        "Use the provided slang glossary to improve translation accuracy.\n"
        "Translate the following subtitles, preserving timestamps and .srt formatting.\n"
        "Correct any duplicate lines or obvious errors.\n"
        "---\n"
        f"{srt_content}"
        "---\n"
        "KoreanSlang Glossary:\n"
        f"{slang_text}\n"
        "Translate the subtitles only, keep the formatting exactly like .srt.\n"
    )


def get_cache_filename(cache_dir: str, input_filename: str, idx: int) -> str:
    """Return the path for a chunk-level cache file."""
    base = os.path.splitext(os.path.basename(input_filename))[0]
    return os.path.join(cache_dir, f"{base}_chunk{idx}.json")


def save_cache(cache_file: str, data: str) -> None:
    """Write a chunk translation to the JSON cache file."""
    with open(cache_file, "w", encoding="utf-8") as f:
        json.dump({"translation": data}, f, ensure_ascii=False, indent=2)


def load_cache(cache_file: str) -> str:
    """Read a chunk translation from the JSON cache file, if it exists."""
    if not os.path.isfile(cache_file):
        return None
    with open(cache_file, encoding="utf-8") as f:
        return json.load(f).get("translation")


def call_openai_api(prompt: str, model: str = "gpt-4", temperature: float = 0.2) -> str:
    """Call the OpenAI Chat Completions API with retry and error handling."""
    load_dotenv()
    api_key = os.getenv("OPENAI_API_KEY")
    client = OpenAI(api_key=api_key) if api_key else OpenAI()
    for _attempt in range(5):
        try:
            resp = client.chat.completions.create(
                model=model,
                messages=[
                    {
                        "role": "system",
                        "content": "You translate and adapt subtitles from Korean to English accurately.",
                    },
                    {"role": "user", "content": prompt},
                ],
                temperature=temperature,
                max_tokens=4000,
            )
            return resp.choices[0].message.content.strip()
        except RateLimitError:
            logging.warning("Rate limit hit, retrying in 5s...")
            time.sleep(5)
        except openai.BadRequestError as e:
            # Handle context-length errors by raising for outer retry with smaller chunks
            if getattr(
                e, "code", ""
            ) == "context_length_exceeded" or "context_length_exceeded" in str(e):
                raise ContextLengthError from e
            logging.warning(f"OpenAI API error: {e}, retrying in 5s...")
            time.sleep(5)
        except Exception as e:
            logging.warning(f"OpenAI API error: {e}, retrying in 5s...")
            time.sleep(5)
    raise RuntimeError("OpenAI API failed after multiple retries")


def parse_translated_chunk(text: str) -> list:
    """Parse an SRT-formatted translation chunk into Subtitle objects."""
    pattern = re.compile(
        r"(\d+)\s+(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\s+([\s\S]*?)(?=\n\n|\Z)",
        re.MULTILINE,
    )
    subs = []
    for m in pattern.finditer(text):
        subs.append(
            Subtitle(
                int(m.group(1)), m.group(2), m.group(3), m.group(4).strip().split("\n")
            )
        )
    return subs


def merge_chunks(chunks: list, overlap: int) -> list:
    """Merge translated chunks, removing overlapping items based on overlap size."""
    merged = []
    for i, chunk in enumerate(chunks):
        if i == 0:
            merged.extend(chunk)
        else:
            merged.extend(chunk[overlap:])
    for i, sub in enumerate(merged, start=1):
        sub.index = i
    return merged


def run_translate_subtitles(
    input_file: str,
    output_file: str,
    slang_file: str,
    chunk_size: int = 50,
    overlap: int = 5,
    cache_dir: str = ".cache",
    model: str = "gpt-4.1-mini",
) -> bool:
    """Translate a Korean SRT file to English using the OpenAI API in chunks."""
    try:
        load_dotenv()

        os.makedirs(cache_dir, exist_ok=True)
        os.makedirs(os.path.dirname(output_file), exist_ok=True)

        logging.info(f"Loading subtitles from {input_file}")
        subs = parse_srt_file(input_file)
        logging.info(f"Loaded {len(subs)} subtitles")

        with open(slang_file, encoding="utf-8") as f:
            slang_text = f.read()

        # Translate chunks, reducing chunk_size on context-length errors
        if chunk_size <= overlap or chunk_size < 1:
            logging.error("chunk_size must be greater than overlap and >=1")
            return False
        while True:
            chunks = chunk_subtitles(subs, chunk_size, overlap)
            logging.info(
                f"Divided into {len(chunks)} chunks (size={chunk_size}, overlap={overlap})"
            )
            translated = []
            try:
                for i, chunk in enumerate(chunks):
                    cache_file = get_cache_filename(cache_dir, input_file, i)
                    cached = load_cache(cache_file)
                    if cached:
                        logging.info(f"Using cache for chunk {i+1}/{len(chunks)}")
                        translated.append(parse_translated_chunk(cached))
                        continue

                    logging.info(f"Translating chunk {i+1}/{len(chunks)}")
                    prompt = build_prompt(chunk, slang_text)
                    result = call_openai_api(prompt, model=model)
                    save_cache(cache_file, result)
                    translated.append(parse_translated_chunk(result))
                    time.sleep(1)
                break
            except ContextLengthError:
                new_size = chunk_size - 10
                if new_size <= overlap:
                    logging.error(
                        f"Cannot reduce chunk_size below overlap+1 ({overlap+1})"
                    )
                    return False
                logging.warning(
                    "Context length exceeded, reducing chunk_size by 10: %d -> %d",
                    chunk_size,
                    new_size,
                )
                chunk_size = new_size

        merged = merge_chunks(translated, overlap)
        logging.info(f"Writing output to {output_file}")
        write_srt_file(output_file, merged)
        return True
    except Exception as e:
        logging.error(f"Subtitle translation failed for {input_file}: {e}")
        return False

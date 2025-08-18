import hashlib
import json
import logging  # Added for logging
import os
import time

import openai
from dotenv import load_dotenv
from openai import OpenAI, RateLimitError


def sha1(text: str) -> str:
    """Return the SHA-1 hex digest of the given text (UTF-8)."""
    return hashlib.sha1(text.encode("utf-8")).hexdigest()


def ensure_client():
    """Ensure an OpenAI client is available using the OPENAI_API_KEY env var."""
    load_dotenv()
    key = os.getenv("OPENAI_API_KEY")
    if not key:
        raise RuntimeError("OPENAI_API_KEY is not set")
    return OpenAI(api_key=key)


def call_openai_translate(title: str, model: str) -> str:
    """Translate a video title to English using the Chat Completions API."""
    client = ensure_client()
    for _attempt in range(5):
        try:
            resp = client.chat.completions.create(
                model=model,
                messages=[
                    {
                        "role": "system",
                        "content": "Translate a video title from Korean (or mixed) to concise, idiomatic English.",
                    },
                    {
                        "role": "user",
                        "content": f"Title:\n{title}\n\nReturn only the translated title.",
                    },
                ],
                temperature=0.3,
                max_tokens=200,
            )
            return (resp.choices[0].message.content or "").strip()
        except RateLimitError:
            logging.warning("Rate limit hit, retrying in 5s...")
            time.sleep(5)
        except openai.BadRequestError as e:
            logging.warning(f"OpenAI API error: {e}, retrying in 5s...")
            raise  # Re-raise to propagate specific errors if needed
        except Exception as e:
            logging.warning(f"OpenAI API error: {e}, retrying in 5s...")
            time.sleep(5)
    raise RuntimeError("OpenAI API failed after multiple retries")


def run_translate_title(
    video_id: str, metadata_dir: str, cache_dir: str, model: str = "gpt-4.1-mini"
) -> bool:
    """Translate and cache the English title for the given video ID."""
    try:
        os.makedirs(cache_dir, exist_ok=True)
        meta_path = os.path.join(metadata_dir, f"{video_id}.json")
        if not os.path.exists(meta_path):
            logging.error(f"Metadata not found for {video_id}: {meta_path}")
            return False
        details = json.load(open(meta_path, encoding="utf-8"))
        source_title = details.get("title") or ""
        if not source_title:
            logging.error(f"No title in metadata for {video_id}")
            return False

        cache_path = os.path.join(cache_dir, f"title_{video_id}.json")
        source_hash = sha1(source_title)
        if os.path.exists(cache_path):
            data = json.load(open(cache_path, encoding="utf-8"))
            if data.get("source_hash") == source_hash and data.get("title_en"):
                logging.info(f"Using cached title for {video_id}")
                return True  # Title already translated and cached

        translated = call_openai_translate(source_title, model=model)
        with open(cache_path, "w", encoding="utf-8") as f:
            json.dump(
                {
                    "video_id": video_id,
                    "title_en": translated,
                    "source_hash": source_hash,
                },
                f,
                ensure_ascii=False,
                indent=2,
            )
        logging.info(f"Successfully translated and cached title for {video_id}")
        return True
    except Exception as e:
        logging.error(f"Title translation failed for {video_id}: {e}")
        return False


# test wrapper removed; use run_translate_title directly

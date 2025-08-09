# Bonjwa.tv Subtitle Processing Pipeline: Project Design Document

## Overview

This document outlines the next-generation design for the Bonjwa.tv subtitle processing pipeline. The pipeline ingests metadata curated in Google Sheets, downloads and processes Korean-language StarCraft: Brood War video content, transcribes and translates subtitles using state-of-the-art machine learning models and LLMs, and produces structured artifacts for web distribution.

Key goals:

- Batchable and idempotent processing
- Modular, maintainable, and easy to extend
- Integration between C# (pipeline orchestration, manifest building) and Python (ML tasks)
- Human-friendly, JSON-driven metadata and artifacts
- Robust error handling, logging, and reporting

---

## 1. High-Level Architecture

```
     [Google Sheets]
     (video curation)
           │
[Sheet Export → videos.json]
           │
[Pipeline Orchestrator (Python CLI)]
           │
```

┌──────────────┬──────────────┬─────────────┬──────────────┐ │    Audio DL  │  Demucs      │ Whisper     │ Translation  │ │  (Python)    │ (optional)   │ (Python)    │ (Python LLM) │ └──────────────┴──────────────┴─────────────┴──────────────┘ │ [Artifacts: audio, vocals, subtitles] │ [Manifest Builder (Python)] │ [subtitles.json + assets for bonjwa.tv]

---

## 2. Directory Structure

/bonjwatv-pipeline/
/video_metadata_dir/
  videos.json                  # Exported from Google Sheets
  /details/
    {video_id}.json            # Cached raw video metadata from yt-dlp
/audio/ {video_id}.mp3
/vocals/ {video_id}*vocals.wav
/subtitles/ kr*{video_id}.srt en_{video_id}.srt
/cache/       # (for translation chunk caching)
/slang/ KoreanSlang.txt
/website/ subtitles.json               # Final manifest for bonjwa.tv

fetch_video_metadata.py download_audio.py isolate_vocals.py transcribe_audio.py whisper_postprocess.py translate_subtitles.py export_sheet_to_json.py manifest_builder.py pipeline_orchestrator.py README.md

---

## 3. Pipeline Steps

The full pipeline is composed of the following sequential steps, each implemented as a standalone Python script. The `pipeline_orchestrator.py` calls these scripts in order. All steps are idempotent—scripts will skip processing if the expected output already exists.

### A. Export Metadata (`export_sheet_to_json.py`)
- **Purpose:** Fetches video metadata curated in Google Sheets and saves it as `video_list_file`.
- Admin curates all videos and metadata in Google Sheets.
- Export script reads the relevant worksheet and writes `video_list_file`.

### B. Fetch Video Metadata (`fetch_video_metadata.py`)
**Purpose:** Fetch detailed video metadata from YouTube, including the upload date, and cache it locally.

**Inputs/Outputs:**
- Input: `video_id`
- Output: `/video_metadata_dir/details/{video_id}.json`

**Features:**
- CLI args: `--video-id`, `--output-dir`
- Idempotent: skip download if target `.json` already exists
- Uses `yt_dlp.YoutubeDL` to extract video information without downloading the audio.
- Saves the `upload_date` and other relevant metadata to a JSON file.
- Minimal logging to `logs/fetch_video_metadata.log` and stdout

**Testing:**
1. Pytest smoke test that:
   - Creates a dummy existing file and asserts skip behavior
   - Monkeypatches `yt_dlp.YoutubeDL` to simulate the API call and the creation of the JSON file.
   - Verifies the content of the created JSON file.

**Next Steps:**
1. Write test suite for `fetch_video_metadata.py`
2. Implement the script per this plan

### C. Audio Download (`download_audio.py`)
**Purpose:** Fetch YouTube audio tracks via `yt-dlp` and save as `.mp3` files for subsequent processing.

**Inputs/Outputs:**
- Input: YouTube URL and `video_id`
- Output: `/audio/{video_id}.mp3`

**Features:**
- CLI args: `--url`, `--video-id`, `--output-dir`
- Idempotent: skip download if target `.mp3` already exists
- Uses `yt_dlp.YoutubeDL` with audio-extraction postprocessor to produce mp3
- Minimal logging to `logs/download_audio.log` and stdout

**Testing:**
1. Pytest smoke test that:
   - Creates a dummy existing file and asserts skip behavior
   - Monkeypatches `yt_dlp.YoutubeDL` to simulate download and file creation

**Next Steps:**
1. Write test suite for `download_audio.py`
2. Implement the script per this plan

### D. Vocal Isolation (Optional) (`isolate_vocals.py`)
**Purpose:** Separate vocals from background audio using Demucs.

**Inputs/Outputs:**
- Input: `/audio/{video_id}.mp3`
- Output vocals: `/vocals/{video_id}/vocals.wav`

**Features:**
- CLI args: `--input-file`, `--output-dir`, `--model`, `--two-stems`
- Idempotent: skip processing if `vocals.wav` already exists
- Uses `subprocess.run(['demucs', ...])` to invoke Demucs CLI
- Minimal logging to `logs/isolate_vocals.log` and stdout

**Testing:**
1. Pytest smoke test for existing output skip
2. Monkeypatch `subprocess.run` to simulate Demucs invocation and output file creation

**Next Steps:**
1. Write test suite for `isolate_vocals.py`
2. Implement the script per this plan

### E. Whisper Transcription (`transcribe_audio.py`)
**Purpose:** Transcribe audio files to raw Korean SRT using OpenAI Whisper (local model).

**Inputs/Outputs:**
- Input audio: `/audio/{video_id}.mp3`
- Output subtitle: `/subtitles/kr_{video_id}.srt`

**Features:**
- CLI with `--input-file`, `--output-file`, `--model-size`, `--language` parameters
- Validate input path, handle errors with nonzero exit code
- Load Whisper model via `importlib.import_module('whisper')` for lazy import
- Format timestamps from floats to `HH:MM:SS,mmm`
- Write segments to `.srt`

**Testing:**
1. Pytest smoke test with a dummy Whisper module (monkeypatch importlib)
2. Verify `.srt` file contents, indices, and timestamp formatting

**Next Steps:**
1. Write the test suite for `transcribe_audio.py`
2. Implement the script per this plan

### F. Whisper Post-Processing (`whisper_postprocess.py`)
**Purpose:** Clean up raw Whisper-generated SRT by normalizing timestamp formats and collapsing duplicate subtitle blocks.

**Inputs/Outputs:**
- Input: `subtitles/kr_{video_id}.srt` (raw)
- Output: `subtitles/kr_{video_id}.srt` (cleaned, overwritten)

**Features:**
- Detect and convert non-standard timestamps (e.g. seconds-only or missing hours) to full `HH:MM:SS,mmm` format
- Collapse adjacent subtitle entries with identical text into a single block spanning the combined duration
- Simple CLI (`--input-file`, `--output-file`) returning nonzero on error

**Testing:**
1. Create pytest smoke test that feeds a sample SRT with mixed timestamp formats and duplicate lines
2. Assert normalized timestamps and collapsed duplicates in output

**Next Steps:**
1. Write the test suite for `whisper_postprocess.py`
2. Implement the script per this plan

### G. Translate Subtitles (`translate_subtitles.py`)
**Purpose:** Translate Korean `.srt` files to English `.srt` using the OpenAI API.

**Inputs/Outputs:**
 - Input SRT: `subtitles/kr_{video_id}.srt`
 - Slang glossary: `slang/KoreanSlang.txt`
 - Output SRT: `subtitles/en_{video_id}.srt`
 - Environment: `.env` with `OPENAI_API_KEY`
 - Cache directory: `.cache/` for chunk-level JSON caches
 - Logs: `logs/translate_subtitles.log`

**Features:**
- Parse and chunk subtitles (default 50 lines, 5-line overlap; chunk_size > overlap and >=1)
- On context-length-exceeded errors, automatically reduce chunk size (by 10 lines) and retry
 - Build prompts with glossary and .srt formatting
- Call OpenAI with retries (up to 5 attempts, backoff)
- On context-length-exceeded errors, automatically reduce chunk size by 10 lines and retry (as long as chunk_size > overlap and >=1)
 - Load/save cached translations per chunk
 - Merge translated chunks, deduplicate overlaps, reindex subtitles
 - Minimal logging to stdout and log file

**Testing:** A pytest-based smoke test will be created first to:
   1. Verify parsing/chunking logic on a sample SRT
   2. Mock the OpenAI client and test prompt construction
   3. Ensure merged output preserves timestamps and formatting

**Next Steps:**
   1. Write the test suite for `translate_subtitles.py`
   2. Implement the script per this plan

### H. Upload Subtitles to Pastebin (`upload_subtitles.py`)
**Purpose:** Upload translated English SRTs to Pastebin and retrieve raw URLs for public hosting.

**Inputs/Outputs:**
- Input: translated English SRT file `subtitles/en_{video_id}.srt`
- Output: raw Pastebin URL (e.g. `https://pastebin.com/raw/{paste_id}`)

**Optional Inputs (login/caching):**
- `PASTEBIN_USER_KEY` or `--user-key` to directly supply your `api_user_key`
- `PASTEBIN_USERNAME` & `PASTEBIN_PASSWORD` (or `--username`/`--password`) to login and obtain an `api_user_key`
- Cache the user key in `.cache/pastebin_user_key.json` to avoid repeated logins
- Cache mapping in `.cache/pastebin_{video_id}.json` to avoid re‑upload

**Features:**
-- CLI args: `--input-file`, `--cache-dir`, `--api-key`, `--user-key`, `--username`, `--password` (or via `.env`)
-- Idempotent: skip upload if a Pastebin cache entry exists
-- Automatically login to Pastebin to obtain `api_user_key` when only credentials are supplied
-- Include `api_user_key` in the paste-creation request to publish under your account
-- Use Pastebin API to create a new unlisted paste (no syntax highlighting)
-- Minimal logging to `logs/upload_subtitles.log` and stdout

**Testing:**
1. Pytest smoke test mocking HTTP POST to Pastebin API and cache file creation

**Next Steps:**
1. Write test suite for `upload_subtitles.py`
2. Implement the script per this plan

### I. Update Google Sheet (`update_sheet_to_google.py`)
**Purpose:** Write back the Pastebin URL (and/or status) into the source Google Sheet for each video.

**Inputs/Outputs:**
- Input: `video_list_file`
- Input: cache files `.cache/pastebin_{video_id}.json` (to get `url`)
- Configuration: service-account JSON path, column name to update (e.g. "Pastebin URL")

**Features:**
- CLI args: `--video-list-file`, `--cache-dir`, `--spreadsheet`, `--worksheet`, `--column-name`, `--service-account-file`
- Idempotent: skip updating if the cell already contains a value
- Uses `gspread` to open the sheet, find rows by video ID, and update the designated column
- Minimal logging to `logs/update_sheet.log` and stdout

**Testing:**
1. Pytest smoke test monkeypatching `gspread` to simulate row lookup and cell update

**Next Steps:**
1. Write test suite for `update_sheet_to_google.py`
2. Implement the script per this plan

### J. Manifest Builder (`manifest_builder.py`)
- **Purpose:** Builds the final `subtitles.json` manifest for the website.
- Scans /subtitles/ for available EN SRTs.
- Collects metadata from videos.json.
- Reads `upload_date` from `/video_metadata_dir/details/{video_id}.json`.
- Builds /website/subtitles.json in the format: { "v": "isIm67yGPzo", "title": "Two-Hatchery Against Mech Terran - "Master Mech Strategies"", "description": "(no description)", "creator": "", "subtitleUrl": "https://pastebin.com/raw/Miy3QqBn", "releaseDate": "2023-10-26", "tags": ["z", "zvt"] }
- Only includes videos with translated subtitles.

---

## 4. Orchestration and Batch Control

- `pipeline_orchestrator.py` (Python CLI):
- Reads `pipeline-config.json` and `video_list_file`
- Sequentially runs batch steps (via subprocess calls to Python scripts)
- Logs skips, progress, and errors (skip-and-log strategy)
- Supports resume/retry by skipping completed steps
- Outputs summary and error reports for admin review

---

## 5. Error Handling

- All steps should:
  - Log errors (to file and stdout)
  - Skip failed/invalid entries and continue
  - Print a summary at the end

---

## 6. Integration & Extensibility

- Python for all steps (audio download, Demucs isolation, Whisper transcription, post-processing, translation, orchestration, and manifest generation)
- Future support for:
  - Additional languages (new columns/SRTs, minor changes to manifest builder)
  - Alternate video sources (e.g., SOOP Live)
  - Per-video or per-chunk processing
  - Scheduled or triggered runs (e.g., cron, CI, webhooks)

---

## 7. Sample Workflow

# 1. Export Google Sheets as videos.json

python export\_sheet\_to\_json.py \
  --spreadsheet "Translation Tracking" \
  --worksheet "Translated Videos" \
  --output video_list_file \
  --service-account-file path/to/service-account.json

# 2. Run orchestrator to process new/changed videos

python pipeline_orchestrator.py --config pipeline-config.json

# 3. Deploy /website/subtitles.json and SRTs to bonjwa.tv

---

## 8. Implementation Notes

- Config file (pipeline-config.json):
-  - All paths, API keys, options
-  - service_account_file: path to Google Sheets service-account JSON (for export metadata)
-  - skip_steps: list of step names to skip (e.g. ["download_audio","isolate_vocals","transcribe_audio"])
- Modular scripts: Each Python/C# script should work independently, accept CLI args, and return nonzero exit code on failure
- Testing: All major scripts should have basic smoke tests (e.g., run on a test row/video)
- Documentation: README.md with all usage instructions, dependencies, known issues

---

## 9. Open Questions & Next Steps

- Confirm/decide if tags should move to a separate tags.json or remain inline per video
- Plan for migrating pipeline to a GPU-enabled VPS if Colab becomes too limited
- Confirm ownership of each pipeline step (who maintains what)
- Security & Credentials Check: manage API keys and access control for translation/ML services

### H. Credentials Health Check (`check_credentials.py`) — PLAN

**Purpose:** Verify presence and validity of all required credentials (Google service account, OpenAI key, Pastebin key) before running the pipeline.

**Checks:**
- **Google Sheets**: confirm `service_account_file` exists, parses as JSON, and can open the configured spreadsheet.
- **OpenAI**: confirm `OPENAI_API_KEY` is set and accepted by calling `openai.Model.list()`.
- **Pastebin**: confirm `PASTEBIN_API_KEY` is set and accepted by a lightweight HTTP POST to the Pastebin API (e.g. checking error message for invalid key).

**Output:**
- Prints a summary table indicating for each credential:
  - Missing, Valid, or Invalid
  
**Next Steps:**
1. Implement `check_credentials.py` per this plan
2. Run health check early in the orchestrator or CI

---

## 10. References

- OpenAI Whisper: [https://github.com/openai/whisper](https://github.com/openai/whisper)
- Demucs: [https://github.com/facebookresearch/demucs](https://github.com/facebookresearch/demucs)
- yt-dlp: [https://github.com/yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp)
- Google Sheets API: [https://developers.google.com/sheets/api](https://developers.google.com/sheets/api)
- OpenAI API: [https://platform.openai.com/docs/api-reference](https://platform.openai.com/docs/api-reference)

---

## Appendix: Key File Formats

### videos.json

[ { "v": "isIm67yGPzo", "youtube\_url": "[https://www.youtube.com/watch?v=isIm67yGPzo](https://www.youtube.com/watch?v=isIm67yGPzo)", "title\_kr": "...", "title\_en": "...", "description": "...", "creator": "...", "subtitleUrl": "...", "tags": ["z", "zvt"] } ]

### subtitles.json (for bonjwa.tv)

[ { "v": "isIm67yGPzo", "title": "Two-Hatchery Against Mech Terran - "Master Mech Strategies"", "description": "(no description)", "creator": "", "subtitleUrl": "[https://pastebin.com/raw/Miy3QqBn](https://pastebin.com/raw/Miy3QqBn)", "tags": ["z", "zvt"] } ]

---

# END OF DOCUMENT


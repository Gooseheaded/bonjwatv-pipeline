# Bonjwa.tv Subtitle Processing Pipeline: Project Design Document

## Overview

This document outlines the next-generation design for the Bonjwa.tv subtitle processing pipeline. The pipeline ingests metadata curated in Google Sheets, downloads and processes Korean-language StarCraft: Brood War video content, transcribes and translates subtitles using state-of-the-art machine learning models and LLMs, and produces structured artifacts for web distribution.

Key goals:

- Batchable and idempotent processing
- Modular, maintainable, and easy to extend
- Python-only implementation across orchestration, processing, and manifest building
- Human-friendly, JSON-driven metadata and artifacts
- Robust error handling, logging, and reporting

---

## 1. High-Level Architecture

- Source curation: Google Sheets → exported to `videos.json`.
- Orchestrator: `pipeline_orchestrator.py` reads config and coordinates steps.
- Steps per video:
  - Fetch metadata (`fetch_video_metadata.py`) → `metadata/{video_id}.json`.
  - Download audio (`download_audio.py`) → `audio/{video_id}.mp3`.
  - Isolate vocals (optional, `isolate_vocals.py`) → `vocals/{video_id}/vocals.wav`.
  - Transcribe (`transcribe_audio.py`) → `subtitles/kr_{video_id}.srt`.
- Normalize (`normalize_srt.py`) → normalized Korean SRT.
  - Translate (`translate_subtitles.py`) → `subtitles/en_{video_id}.srt`.
  - Upload (`upload_subtitles.py`) → Pastebin raw URL (cached).
  - Update sheet (`google_sheet_write.py`) with paste URL.
- Manifest: `manifest_builder.py` → `website/subtitles.json` for bonjwa.tv.

---

## 2. Directory Structure

/bonjwatv-pipeline/
/metadata/
  videos.json                  # Exported from Google Sheets
  {video_id}.json              # Cached raw video metadata from yt-dlp
/audio/ {video_id}.mp3
/vocals/ {video_id}*vocals.wav
/subtitles/ kr*{video_id}.srt en_{video_id}.srt
/cache/       # (for translation chunk caching)
/slang/ KoreanSlang.txt
/website/ subtitles.json               # Final manifest for bonjwa.tv

fetch_video_metadata.py download_audio.py isolate_vocals.py transcribe_audio.py normalize_srt.py translate_subtitles.py google_sheet_read.py google_sheet_write.py manifest_builder.py pipeline_orchestrator.py README.md

---

## 3. Pipeline Steps

The full pipeline is composed of the following sequential steps, each implemented as a standalone Python script. The `pipeline_orchestrator.py` calls these scripts in order. All steps are idempotent—scripts will skip processing if the expected output already exists.

### A. Google Sheet Read (`google_sheet_read.py`)
- Purpose: Export curated Google Sheet rows and save them as `video_list_file`.
- Admin curates all videos and metadata in Google Sheets.
- Export script reads the relevant worksheet and writes `video_list_file`.

### B. Fetch Video Metadata (`fetch_video_metadata.py`)
**Purpose:** Fetch detailed video metadata from YouTube, including the upload date, and cache it locally.

**Inputs/Outputs:**
- Input: `video_id`
- Output: `metadata/{video_id}.json`

**Features:**
- CLI args: `--video-id`, `--output-dir`
- Idempotent: skip download if target `.json` already exists
- Uses `yt_dlp.YoutubeDL` to extract video information without downloading the audio.
- Saves the `upload_date` and other relevant metadata to a JSON file.
- Minimal logging to `logs/fetch_video_metadata.log` and stdout

Testing: Implemented; see `tests/test_fetch_video_metadata.py`.

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

Testing: Implemented; see `tests/test_download_audio.py`.

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

Testing: Implemented; see `tests/test_isolate_vocals.py`.

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

Testing: Implemented; see `tests/test_transcribe_audio.py`.

### F. Normalize SRT (`normalize_srt.py`)
**Purpose:** Clean up raw SRT by normalizing timestamp formats and collapsing duplicate subtitle blocks.

**Inputs/Outputs:**
- Input: `subtitles/kr_{video_id}.srt` (raw)
- Output: `subtitles/kr_{video_id}.srt` (cleaned, overwritten)

**Features:**
- Detect and convert non-standard timestamps (e.g. seconds-only or missing hours) to full `HH:MM:SS,mmm` format
- Collapse adjacent subtitle entries with identical text into a single block spanning the combined duration
- Simple CLI (`--input-file`, `--output-file`) returning nonzero on error

Testing: Implemented; see `tests/test_normalize_srt.py`.

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
- Default model: `gpt-4.1-mini` (configurable via `--model`)
- On context-length-exceeded errors, automatically reduce chunk size (by 10 lines) and retry
 - Build prompts with glossary and .srt formatting
- Call OpenAI with retries (up to 5 attempts, backoff)
- On context-length-exceeded errors, automatically reduce chunk size by 10 lines and retry (as long as chunk_size > overlap and >=1)
 - Load/save cached translations per chunk
 - Merge translated chunks, deduplicate overlaps, reindex subtitles
 - Minimal logging to stdout and log file

Testing: Implemented; see `tests/test_translate_subtitles.py` and `tests/test_translate_subtitles_folder.py`.

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

Testing: Implemented; see `tests/test_upload_subtitles.py`.

### I. Google Sheet Write (`google_sheet_write.py`)
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

Testing: Implemented; see `tests/test_google_sheet_write.py`.

### J. Manifest Builder (`manifest_builder.py`)
- **Purpose:** Builds the final `subtitles.json` manifest for the website.
- Scans /subtitles/ for available EN SRTs.
- Collects metadata from videos.json.
- Reads `upload_date` from `metadata/{video_id}.json`.
- Builds /website/subtitles.json in the format: { "v": "isIm67yGPzo", "title": "Two-Hatchery Against Mech Terran - "Master Mech Strategies"", "description": "(no description)", "creator": "", "subtitleUrl": "https://pastebin.com/raw/Miy3QqBn", "releaseDate": "2023-10-26", "tags": ["z", "zvt"] }
- Only includes videos with translated subtitles.

---

## 4. Orchestration and Batch Control

- `pipeline_orchestrator.py` (Python CLI):
- Reads `pipeline-config.json` and executes an ordered list of `steps`.
- Global steps (run once): `google_sheet_read` (export sheet to JSON), `google_sheet_write` (write Pastebin URLs), `manifest_builder` (build website manifest).
- Per-video steps (run for each `v` in `videos.json`): `fetch_video_metadata`, `download_audio`, `isolate_vocals`, `transcribe_audio`, `normalize_srt`, `translate_subtitles`, `upload_subtitles`.
- Validates `steps` presence and order (e.g., `google_sheet_read` before per-video steps; `manifest_builder` last).
- Logs progress and errors; continues on per-video failures to process subsequent videos.
 - Sources are mutually exclusive: use exactly one of `read_youtube_urls` (URL workflow) or `google_sheet_read` (legacy Sheet export).

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

### A. Default: URL file → pipeline

1) Create `metadata/urls.txt` with one YouTube URL per line.
2) Use steps like: `read_youtube_urls`, `fetch_video_metadata`, `translate_title`, `build_videos_json`, …, `manifest_builder`.
   The pipeline derives a per-run folder named after the URLs filename (e.g., `metadata/urls/`) and places `audio/`, `vocals/`, `subtitles/`, and `.cache/` inside it. Global paths like `website_dir`, `slang_file`, and Google Sheets configs remain unchanged.
3) Run:
```
python pipeline_orchestrator.py --config pipeline-config.json
```

### B. Google Sheet → pipeline

1) Export Google Sheets as videos.json

```
python google_sheet_read.py \
  --spreadsheet "Translation Tracking" \
  --worksheet "Translated Videos" \
  --output metadata/videos.json \
  --service-account-file path/to/service-account.json
```

2) Run orchestrator to process new/changed videos

```
python pipeline_orchestrator.py --config pipeline-config.json
```

3) Deploy `/website/subtitles.json` and SRTs to bonjwa.tv

---

## 8. Implementation Notes

- Config file (pipeline-config.json):
-  - All paths, API keys, options
-  - service_account_file: path to Google Sheets service-account JSON (for export metadata)
-  - steps: ordered list of step names to run (e.g. ["google_sheet_read", "fetch_video_metadata", ... , "manifest_builder"]) 
- Modular scripts: Each Python script should work independently, accept CLI args, and return nonzero exit code on failure
- Testing: All major scripts should have basic smoke tests (e.g., run on a test row/video)
- Documentation: README.md with all usage instructions, dependencies, known issues

### Naming Convention
- Default: `action_resource` for steps and filenames (e.g., `fetch_video_metadata`, `translate_title`, `build_videos_json`).
- Exception: Google Sheets helpers keep `google_sheet_read` / `google_sheet_write`.

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

---

## 11. Planned Feature: URL-file Workflow and Title Translation

Overview
- Default workflow uses a plain text file with one YouTube URL per line to generate an equivalent `videos.json` to the current Google Sheet export.
- For each URL, derive `video_id`, fetch metadata, translate the title, and build `videos.json` entries with: `v` (id), `youtube_url`, `Creator`, and `EN Title`.
- Keep Google Sheet workflow intact and optional.

Input handling
 - Introduce a dedicated input for URLs (e.g., `urls_file`).
 - Global step `read_youtube_urls`: parse `urls_file`, extract `video_id`, dedupe, validate forms (`watch?v=…`, `youtu.be/…`, `shorts/…`), and write a minimal `videos.json` to `video_list_file` with objects `{ "v": id, "youtube_url": url }`.

Per‑video step: `translate_title`
- Script: `translate_title.py`.
- Inputs: `metadata/{video_id}.json` (source title), OpenAI key.
- Output cache: `.cache/title_{video_id}.json` with `{ "title_en": "<translated>", "source_hash": "<sha1_of_source_title>" }`.
- Behavior: idempotent; if cache exists and `source_hash` matches, skip. Use retry/backoff like subtitle translation.
- Recommended order: run after `fetch_video_metadata`.

videos.json build
 - Global step `build_videos_json` writes an enriched file separate from the minimal list (default name `videos_enriched.json`) with fields beyond the initial `{ v, youtube_url }`:
  - `EN Title`: taken from cached translation; if absent, fallback to original title from `metadata/{video_id}.json`.
  - `Creator`: from `metadata/{video_id}.json` (e.g., uploader/channel name).
- This step can also preserve any existing keys (e.g., when using Google Sheet workflow) and only fill missing fields.

Config and steps
 - Add an optional `urls_file` key (path to URLs .txt). `video_list_file` remains the canonical JSON output consumed by other steps.
 - Default steps for URL‑file workflow:
   1) `read_youtube_urls` (global)
   2) per‑video: `fetch_video_metadata`, `translate_title`
   3) `build_videos_json` (global)
   4) optional downstream per‑video steps: `download_audio`, `isolate_vocals`, `transcribe_audio`, `normalize_srt`, `translate_subtitles`, `upload_subtitles`
   5) `manifest_builder` (global)
 - Google Sheet steps (`google_sheet_read`/`google_sheet_write`) remain optional/alternative.

Testing plan (to implement with the feature)
- `youtube_urls_read`: valid/invalid URL parsing, dedupe, and warnings; writes minimal JSON.
- `translate_title.py`: mock OpenAI; verify cache write and idempotency via `source_hash`.
- `videos_json_build`: merges creator and title from metadata/cache; preserves existing fields; verifies enriched `videos.json`.

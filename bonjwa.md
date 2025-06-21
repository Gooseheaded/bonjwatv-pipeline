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

/bonjwatv-pipeline/ /metadata/ videos.json                  # Exported from Google Sheets
/audio/ {video_id}.mp3
/vocals/ {video_id}*vocals.wav
/subtitles/ kr*{video_id}.srt en_{video_id}.srt
/cache/       # (for translation chunk caching)
/slang/ KoreanSlang.txt
/website/ subtitles.json               # Final manifest for bonjwa.tv

download_audio.py isolate_vocals.py transcribe_audio.py whisper_postprocess.py translate_subtitles.py export_sheet_to_json.py manifest_builder.py pipeline_orchestrator.py README.md

---

## 3. Pipeline Steps

### 3.1 Export Metadata

- Admin curates all videos and metadata in Google Sheets.
- Export script reads the relevant worksheet and writes metadata/videos.json:
  - One object per video, e.g.: { "v": "isIm67yGPzo", "youtube\_url": "[https://www.youtube.com/watch?v=isIm67yGPzo](https://www.youtube.com/watch?v=isIm67yGPzo)", "title\_kr": "[ZvT] 2해처리 메카닉 업테란 상대법", "title\_en": "Two-Hatchery Against Mech Terran - "Master Mech Strategies"", "description": "(no description)", "creator": "", "subtitleUrl": "[https://pastebin.com/raw/Miy3QqBn](https://pastebin.com/raw/Miy3QqBn)", "tags": ["z", "zvt"] }
- Tags may be edited by hand or tracked separately.

### 3.2 Batch Processing Pipeline

All steps are idempotent—scripts will skip processing if the expected output exists.

A. Audio Download

- Downloads audio (yt-dlp) for all videos not already present in /audio/.

##### C0. Audio Download (`download_audio.py`) — PLAN

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

#### F. Upload Subtitles to Pastebin (`upload_subtitles.py`) — PLAN

**Purpose:** Upload translated English SRTs to Pastebin and retrieve raw URLs for public hosting.

**Inputs/Outputs:**
- Input: `subtitles/en_{video_id}.srt`
- Output: raw Pastebin URL (e.g. `https://pastebin.com/raw/{paste_id}`)
- Cache mapping in `.cache/pastebin_{video_id}.json` to avoid re‑upload

**Features:**
- CLI args: `--input-file`, `--cache-dir`, `--api-key` (or via `.env`)
- Idempotent: skip upload if cache entry exists
- Use Pastebin API to create a new paste (private/unlisted)
- Minimal logging to `logs/upload_subtitles.log` and stdout

**Testing:**
1. Pytest smoke test mocking HTTP POST to Pastebin API and cache file creation

**Next Steps:**
1. Write test suite for `upload_subtitles.py`
2. Implement the script per this plan

#### G. Update Google Sheet (`update_sheet_to_google.py`) — PLAN

**Purpose:** Write back the Pastebin URL (and/or status) into the source Google Sheet for each video.

**Inputs/Outputs:**
- Input: `metadata/videos.json`
- Input: cache files `.cache/pastebin_{video_id}.json` (to get `url`)
- Configuration: service-account JSON path, column name to update (e.g. "Pastebin URL")

**Features:**
- CLI args: `--metadata-file`, `--cache-dir`, `--spreadsheet`, `--worksheet`, `--column-name`, `--service-account-file`
- Idempotent: skip updating if the cell already contains a value
- Uses `gspread` to open the sheet, find rows by video ID, and update the designated column
- Minimal logging to `logs/update_sheet.log` and stdout

**Testing:**
1. Pytest smoke test monkeypatching `gspread` to simulate row lookup and cell update

**Next Steps:**
1. Write test suite for `update_sheet_to_google.py`
2. Implement the script per this plan

##### B. Vocal Isolation (Optional) (`isolate_vocals.py`) — PLAN

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

B. Vocal Isolation (Optional)

- Runs Demucs on each audio file, outputting vocals to /vocals/.

C. Transcription & Post-Processing

- Uses Whisper to transcribe audio to Korean SRT (/subtitles/kr\_{video\_id}.srt).
- Post-process Whisper output (placeholder step):
  - Normalize timestamps to full HH:MM:SS,mmm format
  - Collapse adjacent duplicate subtitle blocks
  - (Implement in `whisper_postprocess.py`)

D. Translation

- Uses OpenAI LLM (with glossary prompt) to translate Korean SRTs to English SRTs (/subtitles/en_{video_id}.srt).
- Caching per chunk, error handling, and logging included.

##### C1. Whisper Transcription (`transcribe_audio.py`) — PLAN

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

##### C2. Whisper Post-Processing (`whisper_postprocess.py`) — PLAN

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

#### E. Translate Subtitles (`translate_subtitles.py`) - PLAN

 **Purpose:** Translate Korean `.srt` files to English `.srt` using the OpenAI API.

 **Inputs/Outputs:**
 - Input SRT: `subtitles/kr_{video_id}.srt`
 - Slang glossary: `slang/KoreanSlang.txt`
 - Output SRT: `subtitles/en_{video_id}.srt`
 - Environment: `.env` with `OPENAI_API_KEY`
 - Cache directory: `.cache/` for chunk-level JSON caches
 - Logs: `logs/translate_subtitles.log`

 **Features:**
 - Parse and chunk subtitles (default 50 lines, 5-line overlap)
 - Build prompts with glossary and .srt formatting
 - Call OpenAI with retries (up to 5 attempts, backoff)
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

### 3.3 Manifest Builder

- Scans /subtitles/ for available EN SRTs.
- Collects metadata from videos.json.
- Builds /website/subtitles.json in the format: { "v": "isIm67yGPzo", "title": "Two-Hatchery Against Mech Terran - "Master Mech Strategies"", "description": "(no description)", "creator": "", "subtitleUrl": "[https://pastebin.com/raw/Miy3QqBn](https://pastebin.com/raw/Miy3QqBn)", "tags": ["z", "zvt"] }
- Only includes videos with translated subtitles.

---

## 4. Orchestration and Batch Control

- `pipeline_orchestrator.py` (Python CLI):
- Reads `pipeline-config.json` and `metadata/videos.json`
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
  --output metadata/videos.json \
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
- Security: manage API keys and access control for translation/ML services

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


# bonjwatv-pipeline

Subtitle processing pipeline for Bonjwa.tv, implementing transcription, post-processing, translation,
and orchestration of Korean StarCraft: Brood War video subtitles.

## Quickstart: URL Workflow

1) Create a list of YouTube URLs (one per line):
```
mkdir -p metadata
cat > metadata/urls.txt <<'EOF'
https://www.youtube.com/watch?v=abcDEF123
https://youtu.be/XYZ_987
EOF
```
2) Copy the config and run (uses ordered steps including `read_youtube_urls`, `fetch_video_metadata`, `translate_title`, `build_videos_json`). The pipeline will create a per-run folder named after `urls.txt` (e.g., `metadata/urls`), and place `audio/`, `vocals/`, `subtitles/`, and `.cache/` inside it. It writes the minimal `videos.json` and an enriched `videos_enriched.json` used by downstream steps like the manifest.
```
cp pipeline-config.example.json pipeline-config.json
python pipeline_orchestrator.py --config pipeline-config.json
```

### Optional GUI
- Launch: `python -m gui.app`
- The GUI writes a per-run orchestrator config to `<run_root>/gui-config.json` (derived from your selected `urls.txt`). It then runs `pipeline_orchestrator.py` with that config and streams logs to the UI.
- The GUI allows selecting a transcription provider (`local` or `openai`).
- Note: On Linux you may need Tkinter installed (e.g., `sudo apt-get install python3-tk`).

## Workflows

This project supports two primary workflows for subtitle processing: a default URL-list pipeline and a simple ad-hoc folder translation.

### 0. Default Workflow: URL List → Pipeline

Provide a text file with YouTube URLs (one per line). The pipeline will parse IDs, fetch metadata, translate video titles, write a minimal `videos.json`, and then produce an enriched `videos_enriched.json` (adds Creator + EN Title). You can optionally run audio/transcription/translation/upload and manifest steps.

Example `metadata/urls.txt`:
```
https://www.youtube.com/watch?v=abcDEF123
https://youtu.be/XYZ_987
```

Run with ordered steps (see config template):
```
python pipeline_orchestrator.py --config pipeline-config.json
```

### 1. Simple Workflow: Ad-hoc Folder Translation

For quick, one-off translation tasks, use the `translate_subtitles_folder.py` script. This is the easiest way to get started. It recursively finds all `.srt` files in a specified input directory, translates them to English, and saves them to an output directory while preserving the original folder structure.

This workflow is self-contained and only requires an OpenAI API key. It does **not** interact with external services like Google Sheets or Pastebin.

**Example:**
```bash
python translate_subtitles_folder.py \
  --input-dir path/to/korean_subtitles \
  --output-dir path/to/english_subtitles \
  [--slang-file slang/KoreanSlang.txt] \
  [--model gpt-4.1-mini]
```
Ensure you have an `.env` file in the project root containing your `OPENAI_API_KEY`.

### 2. Advanced Workflow: Full End-to-End Pipeline (Google Sheet)

For fully automated, large-scale processing, the `pipeline_orchestrator.py` script manages the entire pipeline from video to final translated subtitles. This is the "fat" pipeline that handles all steps.

Typical steps (configurable via `steps`):
1.  Export rows (`google_sheet_read.py`).
2.  Fetch metadata (`fetch_video_metadata.py`).
3.  Download audio (`download_audio.py`).
4.  Isolate vocals (optional) (`isolate_vocals.py`).
5.  Transcribe to Korean SRT (`transcribe_audio.py`).
6.  Normalize SRT (`normalize_srt.py`).
7.  Translate subtitles (`translate_subtitles.py`).
8.  Upload to Pastebin (`upload_subtitles.py`).
9.  Write URLs back (`google_sheet_write.py`).
10. Build manifest (`manifest_builder.py`).

This workflow is ideal for batch processing and requires credentials for Google Sheets and Pastebin in addition to the OpenAI API key.

Refer to `bonjwa.md` for detailed step-by-step plans, directory structure, and orchestration notes.

Before running the full pipeline, please verify your credentials:
```bash
python check_credentials.py \
  --service-account-file path/to/service-account.json \
  --spreadsheet "Translation Tracking"
```

## Getting Started

These steps show how to set up the development environment, run tests, and explore the pipeline thus far.

### Prerequisites
- Python 3.8 or newer
- Git
- (Optional) GPU drivers/environments for later steps (Demucs, Whisper) 

### Setup
1. Clone the repository:
   ```bash
   git clone https://github.com/Gooseheaded/bonjwatv-pipeline.git
   cd bonjwatv-pipeline
   ```
2. Create and activate a virtual environment:
   ```bash
   python3 -m venv .venv
   source ./.venv/bin/activate
   ```
3. Upgrade pip and install Python dependencies:
   ```bash
   pip install --upgrade pip
   pip install python-dotenv openai pytest whisper yt-dlp requests gspread oauth2client
   # (Optional) install Demucs for vocal isolation: pip install demucs
   ```

### Environment Variables

- Create a `.env` file in the project root with:
  ```dotenv
  OPENAI_API_KEY=your-openai-api-key
  PASTEBIN_API_KEY=your-pastebin-api-key
  PASTEBIN_FOLDER=BWKT
  # (Optional) to upload under your Pastebin account:
  PASTEBIN_USER_KEY=your-pastebin-user-key
  PASTEBIN_USERNAME=your-pastebin-username
  PASTEBIN_PASSWORD=your-pastebin-password
  ```

### Running Tests

Smoke tests for the core Python steps are in `tests/`. To run all tests:
```bash
pytest -q
```

### Code Quality

This project uses [Ruff](https://docs.astral.sh/ruff/) for linting and [Black](https://black.readthedocs.io/en/stable/) for code formatting.

To run the formatter:
```bash
black .
```

To run the linter:
```bash
ruff check .
```

### API Conventions
- Public Python entry points inside each step module are exposed as `run_*` functions (e.g., `run_download_audio`, `run_transcribe_audio`).
- The orchestrator and tests call these `run_*` functions; any non-`run_*` helpers are internal implementation details.
- Return values follow a simple pattern: boolean success for steps that produce files, and paths or caches are validated by callers when needed.

### Config Template
- Copy and edit the example config (ordered steps):
  ```bash
  cp pipeline-config.example.json pipeline-config.json
  # edit spreadsheet, worksheet, and service_account_file
  ```

### YouTube Download (yt-dlp) Auth
- If YouTube returns 403/bot checks, provide cookies to yt-dlp with either:
  - `YTDLP_COOKIES=/path/to/cookies.txt` (export from your browser)
  - or `YTDLP_COOKIES_BROWSER=chrome` (or `firefox`, `brave`, `edge`) for automatic cookie pickup
  The downloader uses a mobile user agent and Android player client by default to reduce challenges.

### Transcription Limits and Chunking (OpenAI)
- OpenAI Whisper uploads have a size cap (~25 MB). The pipeline automatically:
  - Re-encodes large audio to mono ~64 kbps and segments into ~10-minute chunks; each chunk is transcribed separately and timestamps are merged.
  - You can tune thresholds via `run_transcribe_audio(..., max_upload_bytes=..., segment_time=...)` in code.
  - Local Whisper fallback is available but disabled by default when `provider="openai"`.
  The `steps` array defines the pipeline order. Allowed values:
  - Global: `read_youtube_urls`, `build_videos_json`, `google_sheet_read`, `google_sheet_write`, `manifest_builder`
  - Per-video: `fetch_video_metadata`, `translate_title`, `download_audio`, `isolate_vocals`, `transcribe_audio`, `normalize_srt`, `translate_subtitles`, `upload_subtitles`
  - Note: Use exactly one source step: either `read_youtube_urls` (URL workflow) or `google_sheet_read` (legacy), not both.
  - The `transcription_provider` key can be set to `local` or `openai`.
  Then run the orchestrator:
  ```bash
  python pipeline_orchestrator.py --config pipeline-config.json
  ```

## Defaults

- Model (translation): `gpt-4.1-mini` (`--model` to override)
- Chunking: `--chunk-size 50`, `--overlap 5`
- Directories: `audio/`, `subtitles/`, `metadata/`, `.cache/`, `website/`
- Transcription (local): `whisper` with `--model-size large`, `--language ko`
- Transcription (remote): `openai` with `--api-model whisper-1`
- Orchestrator config: see `pipeline-config.json` for `video_list_file`, dirs, and ordered `steps`

## Project Layout

The project is organized into two main user-facing scripts and several sub-scripts that they orchestrate. Users should primarily focus on the main scripts for their respective workflows.

```
# Main Scripts (User Entry Points)
pipeline_orchestrator.py      # 1. Main script for the full, end-to-end pipeline
translate_subtitles_folder.py # 2. Main script for ad-hoc folder translation

# Pipeline Sub-scripts (Internal components)
google_sheet_read.py      # Export Google Sheet rows to metadata/videos.json
read_youtube_urls.py      # Parse URLs .txt into run/videos.json
translate_title.py        # Translate video titles and cache results
build_videos_json.py      # Enrich videos.json into videos_enriched.json
fetch_video_metadata.py   # Fetch detailed video metadata from YouTube
download_audio.py         # Download video audio using yt-dlp
isolate_vocals.py         # Isolate vocals from audio using Demucs
transcribe_audio.py       # Transcribe audio via local Whisper or OpenAI API
normalize_srt.py          # Normalize SRT timestamps and collapse duplicates
translate_subtitles.py    # Translate subtitles using the OpenAI API
upload_subtitles.py       # Upload translated SRTs to Pastebin
google_sheet_write.py     # Update the Google Sheet with Pastebin links
manifest_builder.py       # Build the subtitles.json manifest

# Other Project Files
.venv/                    # Python virtual environment
bonjwa.md                 # Design & planning document
tests/                    # Pytest smoke tests for each step
README.md                 # This contributor guide
```

## Build (PyInstaller)

This repo ships two PyInstaller specs to publish the app as standalone binaries:

- `Orchestrator.spec`: builds a CLI binary for `pipeline_orchestrator.py`.
- `BWKTSubtitlePipeline.spec`: builds the GUI and bundles the Orchestrator binary.

### Prerequisites
- Python virtualenv active
- Install runtime deps + dev tooling:
  ```bash
  pip install -r requirements.txt -r requirements-dev.txt
  ```

### Build
- One command:
  ```bash
  bash build.sh
  ```
  This produces one-dir bundles in `dist/Orchestrator/` and `dist/BWKTSubtitlePipeline/`.

- Or run specs individually:
  ```bash
  pyinstaller --noconfirm Orchestrator.spec
  pyinstaller --noconfirm BWKTSubtitlePipeline.spec
  ```

### Run Built Apps
- GUI: `./dist/BWKTSubtitlePipeline/BWKTSubtitlePipeline` (Windows: `BWKTSubtitlePipeline.exe`)
- Orchestrator: `./dist/Orchestrator/Orchestrator --config <path/to/config.json>`

Note: The GUI writes a per-run config under the derived run folder (`gui-config.json`) and invokes the bundled Orchestrator. Progress is streamed via `PROGRESS:N/TOTAL` lines.

### Windows Notes
- The GUI auto-detects `Orchestrator` vs `Orchestrator.exe` and also supports a nested `Orchestrator/Orchestrator(.exe)` layout inside the bundle.
- If you switch to one-file builds, adjust the GUI data inclusion (spec) to include the single binary.

## Contributing

1. **Plan** your feature or bug-fix in `bonjwa.md` following the established format.
2. **Write tests** for new behavior before implementation.
3. **Implement** code changes.
4. **Run** `pytest -q` and ensure all tests pass.
5. **Update** documentation (`bonjwa.md` and/or `README.md`) as needed.

---
Happy hacking! :)

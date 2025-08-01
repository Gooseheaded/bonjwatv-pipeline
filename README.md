# bonjwatv-pipeline

Subtitle processing pipeline for Bonjwa.tv, implementing transcription, post-processing, translation,
and orchestration of Korean StarCraft: Brood War video subtitles.

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

## Project Layout

The project is organized into two main user-facing scripts and several sub-scripts that they orchestrate. Users should primarily focus on the main scripts for their respective workflows.

```
# Main Scripts (User Entry Points)
pipeline_orchestrator.py      # 1. Main script for the full, end-to-end pipeline
translate_subtitles_folder.py # 2. Main script for ad-hoc folder translation

# Pipeline Sub-scripts (Internal components)
export_sheet_to_json.py   # A. Fetches video metadata from Google Sheets
download_audio.py         # B. Downloads video audio using yt-dlp
isolate_vocals.py         # C. Isolates vocals from audio using Demucs
transcribe_audio.py       # D. Transcribes audio to subtitles using Whisper
whisper_postprocess.py    # E. Post-processes raw Whisper SRT files
translate_subtitles.py    # F. Translates subtitles using the OpenAI API
upload_subtitles.py       # G. Uploads translated SRTs to Pastebin
update_sheet_to_google.py # H. Updates the Google Sheet with Pastebin links
manifest_builder.py       # I. Builds the subtitles.json manifest

# Other Project Files
.venv/                    # Python virtual environment
bonjwa.md                 # Design & planning document
tests/                    # Pytest smoke tests for each step
README.md                 # This contributor guide
```

## Workflows

This project supports two primary workflows for subtitle processing: a simple, ad-hoc folder translation and a comprehensive, end-to-end pipeline.

### 1. Simple Workflow: Ad-hoc Folder Translation

For quick, one-off translation tasks, use the `translate_subtitles_folder.py` script. This is the easiest way to get started. It recursively finds all `.srt` files in a specified input directory, translates them to English, and saves them to an output directory while preserving the original folder structure.

This workflow is self-contained and only requires an OpenAI API key. It does **not** interact with external services like Google Sheets or Pastebin.

**Example:**
```bash
python translate_subtitles_folder.py \
  --input-dir path/to/korean_subtitles \
  --output-dir path/to/english_subtitles \
  [--slang-file slang/KoreanSlang.txt] \
  [--model gpt-4]
```
Ensure you have an `.env` file in the project root containing your `OPENAI_API_KEY`.

### 2. Advanced Workflow: Full End-to-End Pipeline

For fully automated, large-scale processing, the `pipeline_orchestrator.py` script manages the entire pipeline from video to final translated subtitles. This is the "fat" pipeline that handles all steps.

This advanced workflow includes:
1.  Fetching video metadata from a Google Sheet.
2.  Downloading video audio (`download_audio.py`).
3.  Transcribing audio to create source subtitles (`transcribe_audio.py`).
4.  Translating subtitles into English (`translate_subtitles.py`).
5.  Uploading the translated subtitles to Pastebin (`upload_subtitles.py`).
6.  Updating the Google Sheet with the new Pastebin links (`update_sheet_to_google.py`).

This workflow is ideal for batch processing and requires credentials for Google Sheets and Pastebin in addition to the OpenAI API key.

Refer to `bonjwa.md` for detailed step-by-step plans, directory structure, and orchestration notes.

Before running the full pipeline, please verify your credentials:
```bash
python check_credentials.py \
  --service-account-file path/to/service-account.json \
  --spreadsheet "Translation Tracking"
```

## Contributing

1. **Plan** your feature or bug-fix in `bonjwa.md` following the established format.
2. **Write tests** for new behavior before implementation.
3. **Implement** code changes.
4. **Run** `pytest -q` and ensure all tests pass.
5. **Update** documentation (`bonjwa.md` and/or `README.md`) as needed.

---
Happy hacking! :)
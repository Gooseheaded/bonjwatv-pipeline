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
   git clone https://github.com/your-org/bonjwatv-pipeline.git
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

### Credential Health Check

Before running the pipeline, verify your credentials:
```bash
python check_credentials.py \
  --service-account-file path/to/service-account.json \
  --spreadsheet "Translation Tracking"
```

### Running Tests

Smoke tests for the core Python steps are in `tests/`. To run all tests:
```bash
pytest -q
```

## Project Layout

```
.venv/                     # Python virtual environment
bonjwa.md                 # Design & planning document for the pipeline
download_audio.py         # A: Audio download via yt-dlp
isolate_vocals.py         # B: Vocal isolation via Demucs
transcribe_audio.py       # C1: Whisper transcription driver
whisper_postprocess.py    # C2: Whisper SRT post-processing
translate_subtitles.py        # D: OpenAI-based subtitle translation
translate_subtitles_folder.py # Standalone: translate a folder of .srt files recursively
upload_subtitles.py           # F: Upload English SRTs to Pastebin
manifest_builder.py       # 3.3: Build subtitles.json manifest
update_sheet_to_google.py # G: Update Google Sheet with Pastebin URLs
pipeline_orchestrator.py  # 4: Orchestration and batch control
export_sheet_to_json.py   # 3.1: Export metadata from Google Sheets
tests/                    # Pytest smoke tests for each step
README.md                 # This contributor guide
```

### Translating a directory of subtitles

To recursively translate all `.srt` files under a folder into English SRTs, preserving directory structure:

```bash
python translate_subtitles_folder.py \
  --input-dir path/to/input_subtitles \
  --output-dir path/to/output_en_subtitles \
  [--slang-file slang/KoreanSlang.txt] \
  [--chunk-size 50] \
  [--overlap 5] \
  [--cache-dir .cache] \
  [--model gpt-4]
```

This script first post-processes each raw `.srt` (normalizing timestamps & collapsing duplicates via `whisper_postprocess.py`), then translates via OpenAI. It uses `python-dotenv` to load your `.env` file for the OpenAI API key.
Ensure you have an `.env` in the project root containing at least:

```dotenv
OPENAI_API_KEY=your-openai-api-key
```

Refer to `bonjwa.md` for detailed step-by-step plans, directory structure, and orchestration notes.

## Contributing

1. **Plan** your feature or bug-fix in `bonjwa.md` following the established format.
2. **Write tests** for new behavior before implementation.
3. **Implement** code changes.
4. **Run** `pytest -q` and ensure all tests pass.
5. **Update** documentation (`bonjwa.md` and/or `README.md`) as needed.

---
Happy hacking! :)
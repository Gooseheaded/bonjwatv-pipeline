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
   source .venv/bin/activate
   ```
3. Upgrade pip and install Python dependencies:
   ```bash
   pip install --upgrade pip
   pip install python-dotenv openai pytest whisper
   ```

### Running Tests

Smoke tests for the core Python steps are in `tests/`. To run all tests:
```bash
pytest -q
```

## Project Layout

```
.venv/                   # Python virtual environment
bonjwa.md               # Design & planning document for the pipeline
transcribe_audio.py     # C1: Whisper transcription driver
whisper_postprocess.py  # C2: Whisper SRT post-processing
translate_subtitles.py  # D: OpenAI-based subtitle translation
tests/                  # Pytest smoke tests for each step
README.md               # This contributor guide
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
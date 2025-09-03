# Repository Guidelines

## Project Structure & Module Organization
- Root scripts: `pipeline_orchestrator.py` (end‑to‑end pipeline) and `translate_subtitles_folder.py` (ad‑hoc folder translation). Supporting steps live as single‑purpose scripts (e.g., `download_audio.py`, `transcribe_audio.py`, `translate_subtitles.py`, `upload_subtitles.py`).
- Data/outputs: `audio/`, `subtitles/`, `metadata/`, `pado*/`, `mini*/`, `best*/` store inputs, caches, and translated artifacts.
- Config: `pipeline-config.json`, `.env`.
- Tests: `tests/` with pytest smoke tests.
- Docs: `README.md`, planning notes in `bonjwa.md`.

## Build, Test, and Development Commands
- Create venv: `python3 -m venv .venv && source .venv/bin/activate`
- Install deps: `pip install -r requirements.txt`
- Lint: `ruff check .` (auto‑fix: `ruff check . --fix`)
- Format: `black .`
- Run ad‑hoc translate: `python translate_subtitles_folder.py --input-dir <in> --output-dir <out>`
- Run full pipeline: `python pipeline_orchestrator.py`
- Quick credentials check: `python check_credentials.py --service-account-file <path> --spreadsheet "Translation Tracking"`
- Tests: `pytest -q`

## API Conventions
- Each step module exposes a single public entry point `run_*` (e.g., `run_download_audio`, `run_isolate_vocals`, `run_transcribe_audio`, `run_translate_subtitles`, `run_upload_subtitles`).
- The orchestrator and tests call these `run_*` functions; non‑`run_*` helpers are internal implementation details.
- Return values are boolean success where appropriate; callers validate resulting files (e.g., SRT paths, manifest outputs) when needed.

## Coding Style & Naming Conventions
- Python style: PEP 8, 4‑space indentation, limit lines to ~100 chars.
- Naming: `snake_case` for files/functions, `PascalCase` for classes, constants UPPER_CASE.
- Types: Prefer type hints for new/changed code; keep function signatures clear and small.
- IO paths: Use existing folder patterns (e.g., write SRTs under `subtitles/` or the workflow’s designated output tree).

## Testing Guidelines
- Framework: `pytest` with lightweight smoke/integration tests per step in `tests/`.
- Naming: place tests as `tests/test_<module>.py`; use descriptive test names.
- Scope: cover new logic and error paths (e.g., missing files, bad SRTs). No strict coverage threshold, but aim for meaningful assertions.
 - Tests import and call `run_*` functions from modules.

## Commit & Pull Request Guidelines
- Messages: imperative mood, concise subject; prefer Conventional Commits (feat, fix, chore, docs) when reasonable.
- PRs: include purpose, minimal reproduction or sample commands, and screenshots/log snippets when useful. Link related issues and note impacts on config or data paths.
- Checks: ensure `pytest -q` passes; validate key workflows (folder translation and/or orchestrator) on a small sample.

## Security & Configuration Tips
- Secrets: keep API keys in `.env`; never commit credentials. `.gitignore` already excludes typical secrets.
- Services: OpenAI, Google Sheets, Pastebin may be required depending on workflow; validate with `check_credentials.py` before long runs.
- Large runs: use caches (`*_cache/`) to avoid reprocessing; commit only code and lightweight manifests, not generated media.

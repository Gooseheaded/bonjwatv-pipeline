#!/usr/bin/env bash
set -euo pipefail

# Package a minimal translator/ source zip for Windows builds.
#
# Usage:
#   scripts/package-translator-zip.sh [output-zip]
#
# Default output path: dist/translator-src-YYYYMMDD-HHMMSS.zip
#
# This uses `git archive` so it only includes tracked files — ideal for
# excluding local venvs, caches, logs, and other untracked artifacts.

repo_root_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root_dir"

if [[ ! -d translator ]]; then
  echo "translator/ directory not found at repo root: $repo_root_dir" >&2
  exit 1
fi

timestamp=$(date +%Y%m%d-%H%M%S)
out_dir="dist"
mkdir -p "$out_dir"

default_zip="$out_dir/translator-src-$timestamp.zip"
out_zip="${1:-$default_zip}"

# Verify we are in a git repo (git archive relies on it)
if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Not inside a git repository. git archive is required." >&2
  exit 1
fi

echo "Creating translator source archive: $out_zip"

#
# Include the translator project while excluding non-essential/heavy bits.
# Notes:
# - Keep .spec files and build-win32.ps1 (Windows build pipeline).
# - Keep requirements.txt and pyproject.toml.
# - Keep GUI + slang assets and pipeline-config.example.json.
# - Exclude local configs, venv, caches, logs, tests, metadata, previous zips.
#
git archive -o "$out_zip" HEAD \
  -- \
  translator \
  \
  :!translator/.venv \
  :!translator/__pycache__ \
  :!translator/.pytest_cache \
  :!translator/logs \
  :!translator/tests \
  :!translator/metadata \
  :!translator/*.zip \
  :!translator/*.tar \
  :!translator/*.tar.gz \
  :!translator/pipeline-config.json

echo "Done. Wrote: $out_zip"
if command -v du >/dev/null 2>&1; then
  du -h "$out_zip" | awk '{print "Size:", $1}'
fi

cat <<INFO

Contents summary (kept vs. excluded):
  Kept:
    - translator/*.py, gui/* (incl. settings.json), slang/*
    - requirements.txt, pyproject.toml
    - build-win32.ps1, *.spec, pipeline-config.example.json
  Excluded:
    - .venv, __pycache__, .pytest_cache, logs/, tests/, metadata/
    - pipeline-config.json (local config), previous *.zip/*.tar*

On Windows, unzip then:
  1) python -m venv .venv && .venv\\Scripts\\Activate.ps1
  2) pip install --upgrade pip
  3) pip install -r requirements.txt pyinstaller
  4) .\\build-win32.ps1

Artifacts will be under translator/dist/release/.
INFO


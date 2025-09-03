#!/usr/bin/env bash
set -euo pipefail

# Build the Orchestrator CLI first (one-dir by default)
pyinstaller --clean --noconfirm Orchestrator.spec

# Then build the GUI, which bundles the Orchestrator folder as data
# Place the user-facing GUI under dist/release/
pyinstaller --clean --noconfirm --distpath dist/release BWKTSubtitlePipeline.spec

# Tidy up: remove the top-level Orchestrator artifact to avoid confusion
if [ -d "dist/Orchestrator" ]; then
  rm -rf dist/Orchestrator
fi

echo "Build complete. GUI in dist/release/BWKTSubtitlePipeline/"

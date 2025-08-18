#!/usr/bin/env bash
set -euo pipefail

# Build the Orchestrator CLI first (one-dir by default)
pyinstaller --noconfirm Orchestrator.spec

# Then build the GUI, which bundles the Orchestrator folder as data
pyinstaller --noconfirm BWKTSubtitlePipeline.spec

echo "Build complete. Binaries in dist/"

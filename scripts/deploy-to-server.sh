#!/usr/bin/env bash
set -euo pipefail

# Copy a docker bundle tar.gz to a server and deploy it.
# Usage: scripts/deploy-to-server.sh user@host /remote/dir /path/to/bwkt-docker-*.tar.gz

if [[ $# -lt 3 ]]; then
  echo "Usage: $0 user@host /remote/dir /path/to/bundle.tar.gz" >&2
  exit 2
fi

TARGET="$1"
REMOTE_DIR="$2"
BUNDLE="$3"

if [[ ! -f "$BUNDLE" ]]; then
  echo "Bundle not found: $BUNDLE" >&2
  exit 1
fi

echo "Creating remote directory: $TARGET:$REMOTE_DIR"
ssh "$TARGET" "mkdir -p '$REMOTE_DIR'"

echo "Uploading bundle: $BUNDLE → $TARGET:$REMOTE_DIR/"
scp "$BUNDLE" "$TARGET:$REMOTE_DIR/"

BASE_NAME="$(basename "$BUNDLE")"

echo "Extracting and running on remote…"
ssh "$TARGET" bash -lc "cd '$REMOTE_DIR' && rm -rf current && mkdir -p current && tar xzf '$BASE_NAME' -C current && cd current && ./run.sh"

echo "Deployment complete."


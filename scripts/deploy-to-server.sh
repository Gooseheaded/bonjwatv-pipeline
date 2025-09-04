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
ssh "$TARGET" "REMOTE_DIR='$REMOTE_DIR' BASE_NAME='$BASE_NAME' bash -s" <<'REMOTE'
set -euo pipefail
cd "$REMOTE_DIR"
echo "Remote dir content:"
ls -alh

echo "Preflight: ensuring Docker is installed and running…"
if ! command -v docker >/dev/null 2>&1; then
  if [ -f /etc/os-release ]; then . /etc/os-release; else ID=unknown; fi
  case "$ID" in
    arch)
      echo "- Installing Docker on Arch…"
      pacman -Sy --noconfirm docker docker-compose-plugin docker-buildx || true
      ;;
    debian|ubuntu)
      echo "- Installing Docker on Debian/Ubuntu…"
      apt-get update -y || true
      apt-get install -y docker.io docker-compose-plugin || true
      ;;
    *)
      echo "WARNING: Unknown distro ($ID). Please install Docker manually and rerun." >&2
      ;;
  esac
fi

# Start Docker if not running
if ! docker info >/dev/null 2>&1; then
  echo "- Starting Docker service…"
  systemctl enable --now containerd docker || systemctl start docker || true
fi

if ! docker info >/dev/null 2>&1; then
  echo "ERROR: Docker daemon is not running or not accessible. Aborting." >&2
  exit 1
fi

BUNDLE_FILE="$BASE_NAME"
if [[ ! -f "$BUNDLE_FILE" ]]; then
  echo "Bundle not found as expected name; trying latest *.tar.gz"
  BUNDLE_FILE=$(ls -1t *.tar.gz 2>/dev/null | head -n1 || true)
fi
if [[ -z "${BUNDLE_FILE:-}" || ! -f "$BUNDLE_FILE" ]]; then
  echo "ERROR: No bundle .tar.gz found in remote dir" >&2
  exit 1
fi
echo "Using bundle: $BUNDLE_FILE"
rm -rf current
mkdir -p current
tar xzf "$BUNDLE_FILE" -C current
cd current
./run.sh
REMOTE

echo "Deployment complete."

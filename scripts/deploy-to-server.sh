#!/usr/bin/env bash
set -euo pipefail

# Copy a docker bundle tar.gz to a server and deploy it.
# Usage:
#   scripts/deploy-to-server.sh /path/to/bwkt-docker-*.tar.gz
#   scripts/deploy-to-server.sh user@host /remote/dir /path/to/bwkt-docker-*.tar.gz
# Defaults (1-arg mode):
#   target: root@157.230.165.165
#   dir:    /root/bwkt

DEFAULT_TARGET="root@157.230.165.165"
DEFAULT_REMOTE_DIR="/root/bwkt"

if [[ $# -eq 1 ]]; then
  TARGET="$DEFAULT_TARGET"
  REMOTE_DIR="$DEFAULT_REMOTE_DIR"
  BUNDLE="$1"
elif [[ $# -eq 3 ]]; then
  TARGET="$1"
  REMOTE_DIR="$2"
  BUNDLE="$3"
else
  echo "Usage: $0 /path/to/bundle.tar.gz" >&2
  echo "   or: $0 user@host /remote/dir /path/to/bundle.tar.gz" >&2
  exit 2
fi

# Reuse a single SSH connection across all ssh/scp calls to avoid multiple password prompts
SOCK_DIR=$(mktemp -d)
CONTROL_PATH="$SOCK_DIR/ssh_mux_%h_%p_%r"
SSH_OPTS=("-o" "ControlMaster=auto" "-o" "ControlPersist=600" "-o" "ControlPath=$CONTROL_PATH")
cleanup_mux() {
  # Try to gracefully close the master connection and remove socket dir
  ssh -O exit "${SSH_OPTS[@]}" "$TARGET" >/dev/null 2>&1 || true
  rm -rf "$SOCK_DIR" || true
}
trap cleanup_mux EXIT

if [[ ! -f "$BUNDLE" ]]; then
  echo "Bundle not found: $BUNDLE" >&2
  exit 1
fi

echo "Creating remote directory: $TARGET:$REMOTE_DIR"
ssh "${SSH_OPTS[@]}" "$TARGET" "mkdir -p '$REMOTE_DIR'"

echo "Checking remote directory is writable…"
if ! ssh "${SSH_OPTS[@]}" "$TARGET" "touch '$REMOTE_DIR/.deploy-write-test' && rm '$REMOTE_DIR/.deploy-write-test'"; then
  echo "ERROR: Remote directory $REMOTE_DIR is not writable. Choose a different path (e.g. /root/bwkt) and retry." >&2
  exit 1
fi

echo "Checking Docker bind-mount support for remote directory…"
if ! ssh "${SSH_OPTS[@]}" "$TARGET" "mkdir -p '$REMOTE_DIR/.docker-bind-test' && docker run --rm -v '$REMOTE_DIR/.docker-bind-test':/test alpine sh -lc 'echo ok >/test/probe && test -s /test/probe' >/dev/null 2>&1"; then
  echo "ERROR: Docker cannot bind-mount paths under $REMOTE_DIR on the remote host." >&2
  echo "       This is common on some containerized/LXC hosts for /opt." >&2
  echo "       Retry with a Docker-friendly path such as /root/bwkt." >&2
  exit 1
fi
ssh "${SSH_OPTS[@]}" "$TARGET" "rm -rf '$REMOTE_DIR/.docker-bind-test'" >/dev/null 2>&1 || true

echo "Uploading bundle: $BUNDLE → $TARGET:$REMOTE_DIR/"
scp "${SSH_OPTS[@]}" "$BUNDLE" "$TARGET:$REMOTE_DIR/"

BASE_NAME="$(basename "$BUNDLE")"

echo "Extracting and running on remote…"
ssh "${SSH_OPTS[@]}" "$TARGET" "REMOTE_DIR='$REMOTE_DIR' BASE_NAME='$BASE_NAME' bash -s" <<'REMOTE'
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
# Preserve existing .env before replacing current bundle dir
if [[ -f current/.env ]]; then
  echo "Preserving existing .env"
  cp current/.env .env.preserved
fi
rm -rf current
mkdir -p current
tar xzf "$BUNDLE_FILE" -C current
# Restore preserved .env if present
if [[ -f .env.preserved ]]; then
  if [[ -f current/.env ]]; then
    echo ".env exists in new bundle; keeping preserved version as .env.preserved"
  else
    echo "Restoring preserved .env into current/.env"
    cp .env.preserved current/.env
  fi
fi
cd current
./run.sh
REMOTE

echo "Deployment complete."

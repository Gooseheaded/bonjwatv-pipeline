#!/usr/bin/env bash
set -euo pipefail

# Restore Docker volumes from tarballs produced by export-volumes.sh.

usage() {
  cat <<'EOF'
Usage: scripts/import-volumes.sh [options]

Options:
  -v, --volumes   Space-separated list of volumes to restore
                   (default: "current_api-data current_web-data")
  -s, --src-dir   Directory containing vol-<name>-*.tgz archives (default: ./volume-backups)
  -h, --help      Show this help and exit

For each requested volume, the script picks the newest matching
vol-<volume>-*.tgz in the source directory, wipes the current data, and
extracts the archive into the Docker volume.
EOF
}

VOL_LIST="current_api-data current_web-data"
SRC_DIR="./volume-backups"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -v|--volumes)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --volumes requires an argument" >&2
        exit 1
      fi
      VOL_LIST="$2"
      shift 2
      ;;
    -s|--src-dir)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --src-dir requires an argument" >&2
        exit 1
      fi
      SRC_DIR="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker CLI not found" >&2
  exit 1
fi

if [[ ! -d "$SRC_DIR" ]]; then
  echo "ERROR: source directory not found: $SRC_DIR" >&2
  exit 1
fi

SRC_ABS=$(cd "$SRC_DIR" && pwd)
read -r -a VOLUMES <<<"$VOL_LIST"
if [[ ${#VOLUMES[@]} -eq 0 ]]; then
  echo "ERROR: no volumes specified" >&2
  exit 1
fi

echo "Restoring volumes from $SRC_ABS"
for vol in "${VOLUMES[@]}"; do
  tar_path=$(ls -1t "$SRC_ABS"/vol-${vol}-*.tgz 2>/dev/null | head -n1 || true)
  if [[ -z "$tar_path" ]]; then
    echo "ERROR: no archive found for $vol under $SRC_ABS" >&2
    exit 1
  fi

  echo "- $vol ← $(basename "$tar_path")"
  docker volume create "$vol" >/dev/null 2>&1 || true
  docker run --rm \
    -v "${vol}:/data" \
    -v "${SRC_ABS}:/backup" \
    alpine sh -lc "rm -rf /data/* && tar xzf /backup/$(basename "$tar_path") -C /data"
done

echo "Restore complete."

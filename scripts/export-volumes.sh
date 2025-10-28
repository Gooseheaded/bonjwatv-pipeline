#!/usr/bin/env bash
set -euo pipefail

# Export Docker volumes to timestamped tarballs.
# Defaults are tuned for the BWKT stack: current_api-data and current_web-data.

usage() {
  cat <<'EOF'
Usage: scripts/export-volumes.sh [options]

Options:
  -v, --volumes   Space-separated list of volumes to export
                   (default: "current_api-data current_web-data")
  -o, --out-dir   Directory to place the exported archives (default: ./volume-backups)
  -t, --transfer  Optional scp destination (e.g. user@host:/path) to copy the run folder
  -h, --help      Show this help and exit

Examples:
  scripts/export-volumes.sh
  scripts/export-volumes.sh -o ~/bwkt-volume-backups
  scripts/export-volumes.sh -t root@165.232.63.240:/root/backups
EOF
}

VOL_LIST="current_api-data current_web-data"
OUT_DIR="./volume-backups"
TRANSFER=""

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
    -o|--out-dir)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --out-dir requires an argument" >&2
        exit 1
      fi
      OUT_DIR="$2"
      shift 2
      ;;
    -t|--transfer)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --transfer requires an argument" >&2
        exit 1
      fi
      TRANSFER="$2"
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

read -r -a VOLUMES <<<"$VOL_LIST"
if [[ ${#VOLUMES[@]} -eq 0 ]]; then
  echo "ERROR: no volumes specified" >&2
  exit 1
fi

OUT_ROOT=$(mkdir -p "$OUT_DIR" && cd "$OUT_DIR" && pwd)
TS=$(date +%F-%H%M%S)
RUN_DIR="$OUT_ROOT/volumes-$TS"
mkdir -p "$RUN_DIR"

echo "Exporting volumes: ${VOLUMES[*]}"
for vol in "${VOLUMES[@]}"; do
  if ! docker volume inspect "$vol" >/dev/null 2>&1; then
    echo "ERROR: volume $vol not found" >&2
    exit 1
  fi
  archive="vol-${vol}-${TS}.tgz"
  echo "- $vol → $RUN_DIR/$archive"
  docker run --rm \
    -v "${vol}:/data" \
    -v "${RUN_DIR}:/backup" \
    alpine sh -lc "cd /data && tar czf /backup/${archive} ."
done

echo "Archives written to: $RUN_DIR"

if [[ -n "$TRANSFER" ]]; then
  echo "Transferring $RUN_DIR → $TRANSFER"
  scp -r "$RUN_DIR" "$TRANSFER"
fi

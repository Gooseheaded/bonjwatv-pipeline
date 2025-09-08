#!/usr/bin/env bash
set -euo pipefail

# Measure Docker image sizes, layer histories, and bundle size.
# Usage: scripts/measure-docker-sizes.sh [OUTPUT_FILE]
# If OUTPUT_FILE is omitted, writes to docs/logs/docker-sizes-YYYY-MM-DD.txt

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
OUT_FILE="${1:-$ROOT_DIR/docs/logs/docker-sizes-$(date +%F).txt}"

mkdir -p "$(dirname "$OUT_FILE")"

log() {
  echo -e "$*" | tee -a "$OUT_FILE"
}

log "Image Size and Bundle Measurement — $(date -u +'%F %T %Z')"
log

log "## Docker versions"
{ docker --version; docker compose version || docker-compose --version || true; } 2>&1 | tee -a "$OUT_FILE"
log

log "## Rebuild (clean cache)"
log "+ docker compose -f docker-compose.yml -f docker-compose.prod.yml build --no-cache"
(cd "$ROOT_DIR" && docker compose -f docker-compose.yml -f docker-compose.prod.yml build --no-cache) 2>&1 | tee -a "$OUT_FILE"
log

log "## Image sizes"
docker images | grep -E 'bwkt-webapp|bwkt-catalog-api' | sort -k3 -h 2>&1 | tee -a "$OUT_FILE"
log

log "## Layer history — bwkt-webapp:prod"
docker history bwkt-webapp:prod 2>&1 | tee -a "$OUT_FILE"
log

log "## Layer history — bwkt-catalog-api:prod"
docker history bwkt-catalog-api:prod 2>&1 | tee -a "$OUT_FILE"
log

log "## Package bundle"
log "+ scripts/package-docker.sh"
(cd "$ROOT_DIR" && scripts/package-docker.sh) 2>&1 | tee -a "$OUT_FILE"
log

log "## Latest bundle size"
ls -lh "$ROOT_DIR"/dist/bwkt-docker-*.tar.gz 2>/dev/null | tail -n1 | tee -a "$OUT_FILE" || log "No bundle found."
log

log "Done. Output written to: $OUT_FILE"


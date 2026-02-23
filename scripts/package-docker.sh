#!/usr/bin/env bash
set -euo pipefail

# Build Docker images (Release via prod override), bundle images + compose files
# into a single tar.gz for transfer and offline deployment.

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

DEFAULT_DEPLOY_TARGET="root@157.230.165.165"
DEFAULT_DEPLOY_DIR="/root/bwkt"
DEPLOY_TARGET="${DEPLOY_TARGET:-$DEFAULT_DEPLOY_TARGET}"
DEPLOY_DIR="${DEPLOY_DIR:-$DEFAULT_DEPLOY_DIR}"
AUTO_DEPLOY=1
if [[ "${SKIP_DEPLOY:-0}" == "1" ]]; then
  AUTO_DEPLOY=0
fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-deploy|--bundle-only)
      AUTO_DEPLOY=0
      shift
      ;;
    --deploy)
      AUTO_DEPLOY=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: $0 [--no-deploy|--bundle-only]" >&2
      echo "Env overrides: DEPLOY_TARGET, DEPLOY_DIR, SKIP_DEPLOY=1" >&2
      exit 2
      ;;
  esac
done

TAG="$(date +%Y%m%d-%H%M%S)"
OUT_DIR="$ROOT_DIR/dist/docker-bundle-$TAG"
mkdir -p "$OUT_DIR"

echo "[1/4] Building images with prod overrides (Release)…"
docker compose -f docker-compose.yml -f docker-compose.prod.yml build

WEBAPP_IMAGE="bwkt-webapp:prod"
API_IMAGE="bwkt-catalog-api:prod"

echo "[2/4] Saving images to tar files…"
docker save -o "$OUT_DIR/webapp-image.tar" "$WEBAPP_IMAGE"
docker save -o "$OUT_DIR/catalog-api-image.tar" "$API_IMAGE"

echo "[3/4] Staging compose files and run script…"
cp docker-compose.yml docker-compose.prod.yml "$OUT_DIR/"

# Determine seed source for videos.json
SEED_SRC="${SEED_VIDEOS_FILE:-$ROOT_DIR/webapp/data/videos.json}"
if [ -s "$SEED_SRC" ]; then
  echo "- Including seed catalog: $SEED_SRC"
  cp "$SEED_SRC" "$OUT_DIR/videos.json"
else
  echo "WARNING: No seed videos.json found (looked at $SEED_SRC)."
  echo "         You can set SEED_VIDEOS_FILE=/path/to/videos.json to include one."
fi
cat > "$OUT_DIR/run.sh" <<'RUN'
#!/usr/bin/env bash
set -euo pipefail

# Optional: set SKIP_BACKUP=1 to skip volume backups
TS="$(date +%F-%H%M%S)"
mkdir -p backups

HOST_BIND_OK=1
if ! docker run --rm -v "$(pwd)":/bundle alpine sh -lc 'test -d /bundle' >/dev/null 2>&1; then
  HOST_BIND_OK=0
  echo "WARNING: Docker cannot bind-mount the current deployment directory: $(pwd)"
  echo "         Backups and bundled seed copy will be skipped unless the host path is changed."
fi

backup_volume() {
  local vol="$1"
  if docker volume inspect "$vol" >/dev/null 2>&1; then
    echo "Backing up volume: $vol → backups/${vol}-${TS}.tgz"
    docker run --rm -v "$vol":/data -v "$(pwd)/backups":/backup alpine sh -c \
      "cd /data && tar czf /backup/${vol}-${TS}.tgz . || true"
  else
    echo "Volume not found (skipping backup): $vol"
  fi
}

if [ "${SKIP_BACKUP:-0}" != "1" ]; then
  if [ "$HOST_BIND_OK" = "1" ]; then
    backup_volume web-data || true
    backup_volume api-data || true
  else
    echo "Skipping backups because host bind mounts are unavailable for $(pwd)"
  fi
fi

echo "Loading images…"
docker load -i webapp-image.tar
docker load -i catalog-api-image.tar

if [ ! -f ./.env ]; then
  echo "INFO: No .env found in $(pwd). Compose will use only in-file defaults and environment; create .env to inject secrets/config."
fi

echo "Ensuring named volumes exist…"
# Create volumes if missing (so we can seed before first run)
docker volume create web-data >/dev/null 2>&1 || true
docker volume create api-data >/dev/null 2>&1 || true

echo "Seeding api-data with initial videos.json (if empty)…"
# Strategy:
# 1) If bundled videos.json exists → use it
# 2) Else if webapp image contains /app/data/videos.json → extract and use it
# 3) Else if web-data volume contains videos.json → copy it
if [ "$HOST_BIND_OK" = "1" ]; then
  docker run --rm \
    -v api-data:/data \
    -v "$(pwd)":/bundle \
    -v web-data:/webdata \
    --entrypoint sh alpine -c '
      if [ -s /data/videos.json ]; then
        echo " - Skipping (already present)"; exit 0;
      fi
      if [ -s /bundle/videos.json ]; then
        echo " - Seeding from bundle/videos.json"; cp /bundle/videos.json /data/videos.json || true; exit 0;
      fi
      echo " - No bundled catalog; checking webapp image…"
      if docker image inspect bwkt-webapp:prod >/dev/null 2>&1; then
        # Use a nested docker-in-docker trick via host (not available here); fallback to web-data volume
        echo "   (Cannot read image from inside container; trying web-data volume)"
      fi
      if [ -s /webdata/videos.json ]; then
        echo " - Seeding from web-data volume"; cp /webdata/videos.json /data/videos.json || true; exit 0;
      fi
      echo " - WARNING: No seed source found; api-data/videos.json remains missing."
    '
else
  echo " - Skipping seed copy from bundle because host bind mounts are unavailable."
  echo " - If this is a first deploy, rerun from a Docker-bind-mount-friendly directory (e.g. /root/bwkt)."
fi

echo "Starting (or updating) stack…"
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --no-build

echo "Done. Webapp should be on port 80. Volumes preserved."
RUN
chmod +x "$OUT_DIR/run.sh"

echo "[4/4] Creating bundle archive…"
BUNDLE="$ROOT_DIR/dist/bwkt-docker-$TAG.tar.gz"
tar czf "$BUNDLE" -C "$OUT_DIR" .

echo
echo "Bundle created: $BUNDLE"
echo "Contents:"
ls -lh "$OUT_DIR"
echo
if [[ "$AUTO_DEPLOY" == "1" ]]; then
  echo "Auto-deploy enabled → $DEPLOY_TARGET:$DEPLOY_DIR"
  "$ROOT_DIR/scripts/deploy-to-server.sh" "$DEPLOY_TARGET" "$DEPLOY_DIR" "$BUNDLE"
else
  echo "Bundle-only mode (no deploy)."
  echo "Next: scripts/deploy-to-server.sh \"$BUNDLE\""
  echo "      (defaults to $DEPLOY_TARGET:$DEPLOY_DIR)"
fi

#!/usr/bin/env bash
set -euo pipefail

# Build Docker images (Release via prod override), bundle images + compose files
# into a single tar.gz for transfer and offline deployment.

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

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
cat > "$OUT_DIR/run.sh" <<'RUN'
#!/usr/bin/env bash
set -euo pipefail

# Optional: set SKIP_BACKUP=1 to skip volume backups
TS="$(date +%F-%H%M%S)"
mkdir -p backups

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
  backup_volume web-data || true
  backup_volume api-data || true
fi

echo "Loading images…"
docker load -i webapp-image.tar
docker load -i catalog-api-image.tar

echo "Starting (or updating) stack…"
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d

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
echo "Next: copy to server and run run.sh there, or use scripts/deploy-to-server.sh user@host /opt/bwkt $BUNDLE"

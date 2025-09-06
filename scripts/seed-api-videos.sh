#!/usr/bin/env bash
set -euo pipefail

# Diagnose empty search in prod by checking/feeding catalog-api's /app/data/videos.json.
# Usage:
#   chmod +x scripts/seed-api-videos.sh
#   ./scripts/seed-api-videos.sh
#   SEED_FILE=/path/to/videos.json ./scripts/seed-api-videos.sh

# === Config (override via env) ===
WEBAPP_SERVICE_LABEL="${WEBAPP_SERVICE_LABEL:-webapp}"
API_SERVICE_LABEL="${API_SERVICE_LABEL:-catalog-api}"
WEBAPP_FALLBACK_NAME="${WEBAPP_FALLBACK_NAME:-current-webapp-1}"
API_FALLBACK_NAME="${API_FALLBACK_NAME:-current-catalog-api-1}"
RESTART_API="${RESTART_API:-1}"           # set to 0 to skip restart
SEED_FILE="${SEED_FILE:-}"                # path to a local videos.json to use as seed (optional)
API_TEST_PAGE_SIZE="${API_TEST_PAGE_SIZE:-1}"

log()  { printf "\n==> %s\n" "$*"; }
sub()  { printf "    - %s\n" "$*"; }
die()  { printf "\nERROR: %s\n" "$*" >&2; exit 1; }

have_cmd() { command -v "$1" >/dev/null 2>&1; }

ensure_docker() {
  have_cmd docker || die "Docker CLI not found. Install Docker and retry."
  docker info >/dev/null 2>&1 || die "Docker daemon not running or not accessible."
}

find_container_by_label() {
  local label="$1"
  docker ps --filter "label=com.docker.compose.service=${label}" --format '{{.Names}}' | head -n1
}

find_container_any_match() {
  local name="$1"
  docker ps --format '{{.Names}}' | grep -E "^${name}$" || true
}

container_image_name() {
  local c="$1"
  docker inspect -f '{{.Config.Image}}' "$c" 2>/dev/null || true
}

show_container_data_listing() {
  local c="$1"
  sub "Listing /app and /app/data in $c:"
  docker exec "$c" sh -lc 'ls -al /app || true; ls -al /app/data || true'
  sub "File size of /app/data/videos.json in $c:"
  docker exec "$c" sh -lc 'wc -c /app/data/videos.json || true'
}

api_quick_test() {
  local web_c="$1"
  sub "API quick test from within $web_c (pageSize=${API_TEST_PAGE_SIZE}):"
  # Use an alpine sidecar in the web container's network namespace (alpine is already used by run.sh backups)
  docker run --rm --network container:"$web_c" alpine sh -lc \
    "wget -qO- 'http://catalog-api:8080/api/videos?pageSize=${API_TEST_PAGE_SIZE}' | sed -n '1,8p' || true"
}

seed_from_local_file() {
  local api_c="$1"
  local file="$2"
  [ -s "$file" ] || die "Seed file not found or empty: $file"
  sub "Seeding from local file: $file"
  docker exec "$api_c" sh -lc 'mkdir -p /app/data && rm -f /app/data/videos.json'
  cat "$file" | docker exec -i "$api_c" sh -lc 'cat > /app/data/videos.json'
}

seed_from_webapp_image() {
  local api_c="$1"
  local web_image="$2"
  [ -n "$web_image" ] || return 1
  sub "Trying to read /app/data/videos.json from image: $web_image"
  if docker run --rm "$web_image" sh -lc 'test -s /app/data/videos.json'; then
    docker run --rm "$web_image" sh -lc 'cat /app/data/videos.json' \
      | docker exec -i "$api_c" sh -lc 'mkdir -p /app/data && cat > /app/data/videos.json'
    return 0
  fi
  return 1
}

main() {
  ensure_docker

  log "Detecting containers by compose labels"
  local web_c api_c
  web_c="$(find_container_by_label "$WEBAPP_SERVICE_LABEL" || true)"
  api_c="$(find_container_by_label "$API_SERVICE_LABEL" || true)"
  [ -n "$web_c" ] || web_c="$(find_container_any_match "$WEBAPP_FALLBACK_NAME" || true)"
  [ -n "$api_c" ] || api_c="$(find_container_any_match "$API_FALLBACK_NAME" || true)"
  [ -n "$web_c" ] || die "Could not find webapp container (looked for label ${WEBAPP_SERVICE_LABEL} or name ${WEBAPP_FALLBACK_NAME})"
  [ -n "$api_c" ] || die "Could not find catalog-api container (looked for label ${API_SERVICE_LABEL} or name ${API_FALLBACK_NAME})"
  sub "Webapp container: $web_c"
  sub "Catalog API container: $api_c"

  log "Pre-flight diagnostics"
  show_container_data_listing "$api_c"
  show_container_data_listing "$web_c"

  sub "Checking webapp DATA_CATALOG_URL"
  docker exec "$web_c" sh -lc 'echo "      DATA_CATALOG_URL=${DATA_CATALOG_URL:-<unset>}"'

  sub "Checking API DATA_JSON_PATH/DATA_RATINGS_PATH"
  docker exec "$api_c" sh -lc 'echo "      DATA_JSON_PATH=${DATA_JSON_PATH:-<unset>}"; echo "      DATA_RATINGS_PATH=${DATA_RATINGS_PATH:-<unset>}"'

  log "Quick API probe via webapp network"
  api_quick_test "$web_c"

  log "Determine if we need to seed /app/data/videos.json for the Catalog API"
  if docker exec "$api_c" sh -lc 'test -s /app/data/videos.json'; then
    sub "Catalog API already has videos.json; skipping seeding."
  else
    sub "Catalog API has no videos.json; attempting to seed."

    # Option 1: explicit SEED_FILE
    if [ -n "${SEED_FILE:-}" ]; then
      seed_from_local_file "$api_c" "$SEED_FILE"
    else
      # Option 2: try common local candidates
      for candidate in \
        "./videos.json" \
        "./webapp/data/videos.json" \
        "/opt/bwkt-webapp/webapp/data/videos.json"
      do
        if [ -s "$candidate" ]; then
          seed_from_local_file "$api_c" "$candidate"
          break
        fi
      done

      # Option 3: try extracting from webapp image
      if ! docker exec "$api_c" sh -lc 'test -s /app/data/videos.json'; then
        local web_img
        web_img="$(container_image_name "$web_c")"
        if ! seed_from_webapp_image "$api_c" "$web_img"; then
          sub "Could not read videos.json from $web_img."
          die "No seed source found. Provide one with SEED_FILE=/path/to/videos.json and rerun."
        fi
      fi
    fi

    sub "Verifying seeded file size in API container"
    docker exec "$api_c" sh -lc 'ls -al /app/data && wc -c /app/data/videos.json || true'

    if [ "${RESTART_API:-1}" = "1" ]; then
      log "Restarting catalog-api container to ensure watchers reload the file"
      docker restart "$api_c" >/dev/null
      sub "Restarted $api_c"
    else
      sub "Skipping API restart (RESTART_API=0)"
    fi
  fi

  log "Post-seed diagnostics"
  show_container_data_listing "$api_c"
  log "API probe after seeding"
  api_quick_test "$web_c"

  log "Done. If results look good, retry Search in the browser."
}

main "$@"


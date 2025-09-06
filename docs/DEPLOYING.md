Docker Deployment for Production

This guide describes how to deploy the webapp and catalog API on a Proxmox LXC using Docker and Compose. It matches the repo’s production files and conventions.

Prereqs on the LXC
- Enable “Nesting” in Proxmox for the container (Options → Features).
- Install Docker and Compose plugin (Debian/Ubuntu):
  - sudo apt-get update
  - sudo apt-get install -y docker.io docker-compose-plugin
  - sudo usermod -aG docker $USER && newgrp docker
- Verify: docker compose version

Get the code onto the LXC
- Clone or sync the repo to the LXC, e.g.:
  - rsync -av --delete ./bwkt-webapp/ user@lxc:/opt/bwkt-webapp
  - or git clone <repo> /opt/bwkt-webapp

Build and run (production)
- From the repo root on the LXC:
  - docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
- What this does:
  - Builds images in Release mode (via build args) and starts both services.
  - Webapp is published on host port 80 → container 8080.
  - catalog-api is internal only (no host port).
  - Named volumes persist data:
    - `web-data` → webapp `/app/data` (e.g., app data/cache)
    - `api-data` → catalog-api `/app/data` (videos/ratings JSON)
  - When using the bundled run.sh, the script pre-creates volumes and seeds `api-data/videos.json` on first deploy so search works immediately.

Verify
- Check containers: docker ps
- Logs (follow): docker compose logs -f
- Open the UI: http://<lxc-ip>/

Updating to a new version
- Pull new code and rebuild in place:
  - git pull
  - docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
- Compose recreates only what changed; containers are restarted after images are ready.
 - Data is preserved because named volumes (`web-data`, `api-data`) are not removed.

Stopping services
- docker compose -f docker-compose.yml -f docker-compose.prod.yml down

Alternative workflow: build elsewhere, deploy images
- Push to a registry and reference tags in compose, or save+copy:
  - docker save -o webapp.tar bwkt-webapp:dev
  - docker save -o api.tar bwkt-catalog-api:dev
  - scp to the LXC and load:
    - docker load -i webapp.tar
    - docker load -i api.tar
  - Then run the same compose up command above.

Bundle-based deploy (no registry, scripted)
- Package on your dev machine:
  - scripts/package-docker.sh
  - Output: dist/bwkt-docker-YYYYMMDD-HHMMSS.tar.gz containing images, compose files, and a run.sh
- Upload and deploy to the server (example):
  - scripts/deploy-to-server.sh root@172.16.4.104 /opt/bwkt dist/bwkt-docker-YYYYMMDD-HHMMSS.tar.gz
  - The script uploads, extracts under /opt/bwkt/current, loads images, and runs: docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
- Manual alternative:
  - scp dist/bwkt-docker-*.tar.gz user@host:/path && ssh user@host 'mkdir -p /path/current && tar xzf /path/bwkt-docker-*.tar.gz -C /path/current && cd /path/current && ./run.sh'
- Notes:
  - Server still needs Docker + compose plugin installed.
  - Safety: run.sh backs up named volumes `web-data` and `api-data` to `backups/` before updating (set `SKIP_BACKUP=1` to skip).
  - Seeding: run.sh seeds `api-data/videos.json` from the bundle if missing. You can override the seed source at package time with `SEED_VIDEOS_FILE=/path/to/videos.json scripts/package-docker.sh`.
  - Webapp listens on port 80; catalog-api is internal-only.
  - .env preservation: deploy-to-server.sh preserves an existing `.env` in the target directory and restores it into the new `current/` bundle so your secrets/config aren’t overwritten.

TLS and domain
- Easiest: run a reverse proxy (Caddy/Nginx) on the LXC that terminates TLS and forwards to `webapp:8080`.
- If exposing directly on port 80, ensure upstream firewall/NAT allows HTTP (and 443 if adding TLS later).

Health, logs, troubleshooting
- Health: docker ps, docker inspect <container>
- Logs: docker compose logs -f webapp | catalog-api
- If Docker fails to start, run scripts/docker-diagnose.sh and review docker-output.txt

Persisted data paths (production)
- Webapp: `/app/data` mounted from `web-data` (already configured).
- Catalog API: `/app/data` mounted from `api-data` with:
  - `DATA_JSON_PATH=/app/data/videos.json`
  - `DATA_RATINGS_PATH=/app/data/ratings.json`
  Ensure the files exist. The bundled run.sh seeds `videos.json` on first deploy; otherwise copy one in manually. Compose also sets `Data__JsonPath=/app/data/videos.json` so the API prefers the volume path.

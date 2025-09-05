Developing BWKT (Webapp + Catalog API)

This doc covers local development using Docker Compose and the .NET SDK.

Prerequisites
- Docker Engine + Compose plugin installed (or Podman with podman‑compose)
- .NET 9 SDK (optional, for running outside containers)

Quick Start (containerized)
- Build and run both services:
  - docker compose up --build
- Live code reload (Compose Watch):
  - docker compose watch
  - Rebuilds on source changes; syncs `webapp/data/` directly into the container for instant `videos.json` reloads.
- Open the apps:
  - Webapp: http://localhost:5001
  - Catalog API (Swagger): http://localhost:5002/swagger
- Tail logs:
  - docker compose logs -f webapp
  - docker compose logs -f catalog-api
- Stop containers:
  - docker compose down

Edit–Build–Run Loop
- After code changes, rebuild and restart:
  - docker compose up -d --build
- Compose will recreate only the image/container that changed.

Environment and Ports
- Webapp runs at container port 8080, published to host 5001.
- Catalog API runs at 8080, published to host 5002.
- Webapp reads its data from the Catalog API via:
  - DATA_CATALOG_URL=http://catalog-api:8080/api/videos
  (This is set in docker-compose.yml.)

Secrets (.env + Compose)
- Create a local `.env` (git-ignored) next to `docker-compose.yml` or copy `.env.example`:
  - DISCORD_CLIENT_ID=your-discord-client-id
  - DISCORD_CLIENT_SECRET=your-discord-client-secret
  - OAUTH_CALLBACK_URL=http://localhost:5001/account/callback
- Compose auto-loads `.env` and injects these into the `webapp` container via `${VAR}` expansion.
- Rebuild/run: `docker compose up -d --build` (or use `docker compose watch`).

Static Assets and Razor Pages
- Images are built with dotnet publish to include static web assets.
- During dev, publish uses Debug configuration by default (fast, no trimming).

Running Without Docker (optional)
- API:
  - cd catalog-api && dotnet run
  - Opens on http://localhost:5239 (random) unless ASPNETCORE_URLS is set.
- Webapp:
  - cd webapp && dotnet run
  - Opens on http://localhost:5130 (random) unless ASPNETCORE_URLS is set.
- Keep DATA_CATALOG_URL pointing to the API:
  - On webapp: export DATA_CATALOG_URL=http://localhost:5239/api/videos

Useful Commands
- Rebuild only one service:
  - docker compose build webapp
  - docker compose up -d webapp
- Clean state (stop and remove):
  - docker compose down -v
- Inspect container:
  - docker inspect bwkt-webapp-webapp-1

Production Differences
- Use docker-compose.prod.yml overrides in production:
  - docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
- Overrides set:
  - ASPNETCORE_ENVIRONMENT=Production
  - Ports: Webapp → 80:8080, API → no host port
  - Build configuration: Release (via build args)
  - Persistent volume for /app/data in webapp

Troubleshooting
- Docker not starting: run scripts/docker-diagnose.sh and review docker-output.txt
- Port conflicts: change published ports in docker-compose.yml
- Static assets missing: ensure images were built via dotnet publish (compose does this)

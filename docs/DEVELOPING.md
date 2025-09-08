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
- Webapp reads its data exclusively from the Catalog API via:
  - DATA_CATALOG_URL=http://catalog-api:8080/api/videos
  (Set in docker-compose.yml.)
- Catalog API (uploads/submissions):
  - Optional env vars for dev:
    - `API_INGEST_TOKENS=TOKEN1` (allowlist token for `X-Api-Key`)
    - `DATA_SUBTITLES_ROOT=/app/data/subtitles` (default)
    - `DATA_SUBMISSIONS_PATH=/app/data/submissions.json` (default)
 - Dev note: The webapp no longer reads a local `data/videos.json`; it queries the Catalog API directly. The previous bind mounts are no longer required.

Secrets (.env + Compose)
- Create a local `.env` (git-ignored) next to `docker-compose.yml` or copy `.env.example`:
  - DISCORD_CLIENT_ID=your-discord-client-id
  - DISCORD_CLIENT_SECRET=your-discord-client-secret
  - Optional: OAUTH_CALLBACK_URL (not needed in Development)
- Compose auto-loads `.env` and injects these into the `webapp` container via `${VAR}` expansion.
- Rebuild/run: `docker compose up -d --build` (or use `docker compose watch`).

Catalog API test endpoints
- Subtitles:
  - Upload: `curl -F "videoId=abc123" -F "version=1" -F "file=@subs.srt;type=text/plain" http://localhost:5002/api/uploads/subtitles`
  - Serve: `curl http://localhost:5002/api/subtitles/abc123/1.srt`
- Submissions:
  - Submit (requires token): `curl -H "X-Api-Key: TOKEN1" -H "Content-Type: application/json" -d '{"youtube_id":"abc123","title":"My Video","tags":["z"],"subtitle_storage_key":"subtitles/abc123/v1.srt"}' http://localhost:5002/api/submissions/videos`
  - List: `curl http://localhost:5002/api/admin/submissions?status=pending`

Admin Tools
- Admin access: set `ADMIN_USER_IDS` (comma-separated Discord user IDs) in `.env` for the webapp.
- Ratings:
  - Broad UI: `/Admin` shows the 10 most recent rating events (author, value, video, version, timestamp), with a link to view all.
  - Narrow UI: `/Admin/Ratings` lists up to the last 1000 rating events in a paginated table. Use `?page=` and `?pageSize=`.
- Submissions:
  - `/Admin` shows Pending Submissions (Open → detail). Detail page has Approve/Reject.
  - Watch page displays “Submitted by … [on …]”.
- Hidden videos:
  - Admin can hide a video from the Watch page (“Hide…” prompts for a reason).
  - `/Admin` shows a “Hidden Videos” list (Open → detail) with Show/Delete actions.
  - Hidden videos are excluded from search results.

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
  - Webapp reads `OAUTH_CALLBACK_URL` from environment in Production; in Development it always uses `http(s)://<current-host>/account/callback`.

Troubleshooting
- Docker not starting: run scripts/docker-diagnose.sh and review docker-output.txt
- Port conflicts: change published ports in docker-compose.yml
- Static assets missing: ensure images were built via dotnet publish (compose does this)

Homepage ratings and sorting
- The homepage shows a compact ratings summary (🔴 🟡 🟢) on each card and sorts videos by a quality score computed from Catalog API ratings.
- Scoring uses a Wilson lower confidence bound of a “positive” rate where Positive = Green + 0.5 × Yellow and Total = Red + Yellow + Green (z = 1.96).
- The webapp calls the Catalog API ratings endpoint per video. In code, this is abstracted via `IRatingsClient` (default `HttpRatingsClient`).
- Env resolution for the Catalog API base:
  - Prefer `CATALOG_API_BASE_URL` (e.g., `http://catalog-api:8080/api`).
  - Otherwise derive from `DATA_CATALOG_URL` by trimming the trailing `/videos`.
- Tests inject a fake `IRatingsClient` for deterministic ordering; no external network is needed.

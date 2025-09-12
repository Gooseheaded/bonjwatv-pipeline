# BWKT Webapp

A simple ASP.NET Core Razor Pages app for browsing and watching YouTube videos with first‑party subtitles and tag-based filtering. The webapp reads all catalog data from the Catalog API (no local `videos.json`).

## Features

- Browse a grid of supported videos with thumbnails and titles
- Search by title or tags (Zerg, Protoss, Terran) with case-insensitive, multi-token queries
- Watch page with embedded YouTube player and synchronized subtitle overlay
- Adjustable subtitle font size (saved in browser storage)
- First‑party subtitles served via the app (`/subtitles/{id}/{version}.srt`) with on‑demand mirroring of legacy links
- Minimal front-end dependencies; custom SRT parser in `wwwroot/js/subtitles.js`
- Test-first workflow with xUnit unit and integration tests

## Architecture

- Data source: Catalog API provides videos, search, ratings, and subtitle endpoints.
- Webapp resolves the API base from environment:
  - Prefer `CATALOG_API_BASE_URL` (e.g., `http://catalog-api:8080/api`).
  - Otherwise derive from `DATA_CATALOG_URL` by trimming the trailing `/videos`.
- Subtitles flow: the app serves `/subtitles/{id}/{version}.srt`, proxying to the API. If first‑party is missing, it fetches the legacy URL, responds, and mirrors to the API for subsequent requests.

## Prerequisites

- Docker Engine + Compose (recommended for local dev), or
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) to run without Docker

## Getting Started

Option A — Docker Compose (recommended)
- From repo root:
  - `docker compose up --build`
  - Webapp: `http://localhost:5001`
  - Catalog API (Swagger): `http://localhost:5002/swagger`
- More details: see `docs/DEVELOPING.md`.

Option B — Run without Docker
- Start the API:
  - `cd catalog-api && dotnet run` (note the URL printed, e.g., `http://localhost:5239`)
- Start the webapp with API base configured:
  - In another shell: `cd webapp`
  - Set env and run:
    - `export DATA_CATALOG_URL=http://localhost:5239/api/videos`
    - or `export CATALOG_API_BASE_URL=http://localhost:5239/api`
    - `dotnet run`

## Configuration

- `CATALOG_API_BASE_URL`: Base URL for the Catalog API (preferred).
- `DATA_CATALOG_URL`: Legacy env pointing to the videos endpoint; used to derive the base if present.
- `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`: OAuth for login (webapp).
- `ADMIN_USER_IDS`: Comma-separated Discord user IDs with admin access.
- `LEGACY_SUBTITLE_API_TOKEN`: Token used by the webapp to mirror legacy subtitle URLs into the API.

Secrets for local dev can be set via `.env`; see `docs/DEVELOPING.md` for examples and port mappings.

## Testing

Run the xUnit test suite (unit + integration):
```bash
dotnet test tests/bwkt-webapp.Tests/bwkt-webapp.Tests.csproj
```

## References

- Development guide: `docs/DEVELOPING.md`
- Deployment guide: `docs/DEPLOYING.md`
- ADR 0002 (API-only webapp & on-demand subtitle mirroring): `docs/adrs/0002-api-only-webapp-and-on-demand-subtitle-mirroring.md`
- Project plan: `docs/webapp/PLAN.md`

## Contributing

Contributions are welcome! Please open issues or submit pull requests.

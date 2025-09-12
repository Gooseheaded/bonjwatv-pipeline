BWKT Monorepo

This repository hosts three related projects that are developed and deployed independently:

- webapp: ASP.NET Core Razor Pages UI that embeds YouTube and overlays subtitles.
- catalog-api: Minimal API that serves catalog data (videos, subtitles metadata) and will later handle auth, submissions, and moderation.
- translator: The data pipeline and tooling that builds videos.json, fetches/normalizes SRTs, and publishes artifacts for the catalog.

Goals

- Separate code deploys from content updates: update data (videos/subtitles) without redeploying the webapp.
- Provide a clean path to contributions (Discord login, submissions, moderation) via catalog-api.

Local Development

- Quick start with compose:
  1. docker compose up --build
  2. Visit catalog-api at http://localhost:5002/swagger and webapp at http://localhost:5001
- Manual:
  - cd catalog-api && dotnet run
  - cd webapp && dotnet run

Notes

- Catalog API owns the primary videos store. It can optionally import from a legacy `videos.json` at startup if the primary store is empty.
- The webapp reads catalog data exclusively from the API (no local `videos.json`). Configure via `CATALOG_API_BASE_URL` (preferred) or `DATA_CATALOG_URL`.

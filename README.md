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

- In dev, catalog-api reads ../webapp/data/videos.json. Later, translator will publish data artifacts that catalog-api imports or serves.
- The webapp can be configured to read from the API via the DATA_CATALOG_URL environment variable, with a local JSON fallback for development.


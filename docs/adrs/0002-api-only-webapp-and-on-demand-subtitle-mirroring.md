# ADR 0002: API-only Webapp and On-Demand Subtitle Mirroring

- Status: Proposed
- Date: 2025-09-08

## Context
- We want the webapp to consume a single source of truth (Catalog API) instead of bundling local JSON, to unify data and behavior across environments.
- Subtitles historically lived as third‑party links (Pastebin, etc.), leading to mixed content/CORS issues and link rot.
- Production reported 404/missing subtitles when relying on external URLs and ad‑hoc client fetches.

## Decision
- Make Catalog API the authoritative data provider for the webapp. All listings, search, and detail are served via API endpoints with paging and server‑side sorting.
- Serve subtitles through the webapp at `/subtitles/{id}/{version}.srt`, which proxies to the API first‑party store.
- If a first‑party subtitle is missing, the webapp fetches the legacy external `subtitleUrl`, returns it to the client, and mirrors the content to the API via a secured upload so subsequent requests are first‑party.
- Secure uploads to the API with `X-Api-Key` checked against `API_INGEST_TOKENS`. The webapp uses `LEGACY_SUBTITLE_API_TOKEN` to perform server‑to‑server mirroring.
- Persist ASP.NET Data Protection keys on disk so auth cookies survive restarts/redeploys.

## Alternatives
- Continue serving external subtitle URLs directly: rejected due to CORS/mixed‑content risk and external dependency fragility.
- Pre‑mirror all subtitles in a batch job: deferred; more operational effort and coordination, harder to ensure freshness without a pipeline.
- Keep local JSON in the webapp: rejected; duplicates the source of truth and complicates deployments.
- CDN rewrite/proxy rules: possible, but application‑level proxy keeps auth, logging, and mirroring logic close to business code.

## Consequences
- New env vars and secrets management:
  - API: `API_INGEST_TOKENS` (comma‑separated allowed keys) and persistent storage for `/app/data/subtitles`.
  - Webapp: `CATALOG_API_BASE_URL` (or `DATA_CATALOG_URL`), `LEGACY_SUBTITLE_API_TOKEN`, persisted Data Protection keys under `/app/data/keys`.
- First request for a legacy subtitle can incur extra latency while fetching + mirroring; subsequent requests are first‑party and cached.
- Recommend a background admin tool to bulk‑mirror remaining legacy subtitles and update `videos.json` entries to first‑party paths.
- API schema now includes `subtitleUrl` for detail and list items to support fallback decisions.

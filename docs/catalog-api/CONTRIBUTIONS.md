# Contributions & Data Separation Design

This document proposes the next development phase for BWKT Webapp: separating code from data and enabling community contributions (via Discord login) for videos, subtitles, and Korean slang entries.

## Objectives

- Decouple application deploys from content updates (code vs. data).
- Introduce a Catalog API and durable storage for metadata and files.
- Add Discord OAuth login and role-based permissions.
- Enable user submissions with moderation and audit trails.
- Keep the current webapp simple (Razor Pages) with minimal front-end dependencies.

## Non-Goals (for this phase)

- No complex real-time collaboration or live editing.
- No payment or donation features.
- No public write access without moderation.

## High-Level Plan (Phases)

1. Phase 0 — Read-Only Split
   - Stand up a minimal Catalog API exposing read endpoints for videos, subtitles metadata, and search.
   - Import existing `data/videos.json` into the API’s database.
   - Webapp reads data via `DATA_CATALOG_URL` with last-known-good fallback.
2. Phase 1 — Auth + Submissions
   - Discord OAuth login; issue session/JWT; store users and roles.
   - Submissions for videos, subtitles, and slang entries; moderation queue and approval flow.
   - Store SRTs and other files in object storage (MinIO/S3) via presigned URLs.
3. Phase 2 — Ops & Scale
   - Homelab stack (Compose) → optional k8s (k3s) migration.
   - Observability, backups, security hardening; optional CDN for read snapshots.

## Architecture Overview

- bwkt-webapp (Razor Pages): SSR UI, embeds YouTube player, fetches catalog data from API, authenticated user flows for submissions.
- catalog-api (ASP.NET Minimal API or MVC): owns metadata, auth, submissions, moderation, audit; issues presigned uploads; publishes read endpoints.
- Postgres (preferred) or SQLite (bootstrap): durable metadata store.
- Object Storage: MinIO (homelab) or S3-compatible for SRTs and large assets; optional CDN in front for public reads.
- Reverse Proxy: Nginx/Traefik for TLS and routing.

Environment variables (illustrative):
- Webapp: `DATA_CATALOG_URL`, `CATALOG_API_BASE_URL`, `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`, `OAUTH_CALLBACK_URL`.
- API: `DB_CONNECTION_STRING`, `JWT_SIGNING_KEY`, `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`, `STORAGE_ENDPOINT`, `STORAGE_BUCKET`, `STORAGE_ACCESS_KEY`, `STORAGE_SECRET_KEY`, `PUBLIC_CDN_BASE_URL`.

## Data Model (Initial)

- users
  - id (uuid), discord_id (string, unique), username, avatar_url, roles (member|moderator|admin), created_at
- videos
  - id (uuid), youtube_id, title, description, creator, release_date, tags (text[]), created_at, updated_at, created_by
- subtitles
  - id (uuid), video_id → videos.id, language (e.g., en, ko), storage_key (path in object storage), version (int), status (pending|approved|rejected), submitted_by, reviewed_by, created_at, updated_at
- slang_entries
  - id (uuid), term, meaning, notes, version (int), status, submitted_by, reviewed_by, created_at
- submissions (generic audit envelope)
  - id (uuid), type (video|subtitle|slang), payload_json, status (pending|approved|rejected), user_id, created_at, reviewed_at, reviewer_id
- audit_logs
  - id, entity_type, entity_id, action, actor_id, before_json, after_json, created_at

Notes:
- Prefer Postgres for arrays, full-text search, and migrations. SQLite acceptable initially; can add FTS via `FTS5` if needed.

## API Surface (v1)

Public (no auth needed for read):
- GET `/api/videos?page=&pageSize=&q=&tags=&sortBy=`
- GET `/api/videos/{id}`
- GET `/api/videos/{id}/subtitles` — list approved subtitle variants
- GET `/api/search?q=` — unified search across videos (and optionally subtitles text)

Authenticated (Discord OAuth → JWT/session):
- GET `/api/me` — current user profile and roles
- POST `/api/submissions/videos` — propose a new video (youtube_id, title, creator, description, tags, release_date)
- POST `/api/submissions/subtitles` — request presigned URL; then client uploads SRT; finalize submission with metadata {video_id, language, storage_key, checksum}
- POST `/api/submissions/slang` — submit/edit slang entry

Moderator/Admin:
- GET `/api/moderation/submissions?status=pending&type=`
- PATCH `/api/moderation/submissions/{id}` — approve/reject with reason
- POST `/api/videos/{id}/subtitles/{subtitleId}/approve` — direct approval path (optional)

Auth & Security:
- Discord OAuth2 login on webapp; webapp exchanges code with catalog-api to create/attach user and issue app JWT.
- Roles enforced via middleware/attributes; rate limits on write endpoints; CSRF protection for cookie sessions if used.

## Search Strategy

- Phase 0: API serves search from DB fields (title, creator, tags). For Postgres, use `tsvector` (title/creator) and filter tags; simple BM25-like ranking with `ts_rank`. If SQLite, start with LIKE-based search, add FTS5 later.
- Subtitles text in search:
  - Option A (API): Ingest SRT text into `subtitles_text` table and index with FTS (Postgres tsvector/SQLite FTS5).
  - Option B (Webapp local cache): Continue `SubtitlesCache` downloading SRTs locally and maintain a local FTS index for offline ranking; use API only for metadata. Prefer A long-term.

## File Storage Flow (SRTs)

- Client requests to submit subtitles → API returns presigned PUT URL and `storage_key`.
- Client uploads SRT directly to object storage.
- Client finalizes submission by POSTing metadata (video_id, language, storage_key, checksum) → submission created with status=pending.
- Moderator approves → API marks subtitles row approved; optional background job parses SRT to extract searchable text and updates search index.

## Webapp Changes

- Add `DATA_CATALOG_URL` and `CATALOG_API_BASE_URL` configs.
- Replace direct reads of `data/videos.json` with HTTP fetch to catalog-api.
- Keep dev-mode fallback to local JSON if env var missing.
- UI additions: login/logout via Discord, submissions pages/forms, moderation pages (role-gated), subtitle font settings unchanged.

## Migration Plan (from videos.json)

1. Build an importer in catalog-api: read `data/videos.json` and seed `videos` (+ optional `subtitles` pointers if present).
2. Deploy catalog-api (read-only) and point webapp to `DATA_CATALOG_URL`.
3. Verify parity (counts, fields). Keep local JSON hot-reload for dev only.
4. Enable Discord login and submissions (behind feature flag) → roll out moderation UI.
5. Decommission local JSON in production once API is stable.

## Deployment Topologies

- Compose (homelab, initial):
  - Services: webapp, catalog-api, postgres, minio, reverse-proxy.
  - Volumes: postgres data, minio data, webapp cache (optional).
- k8s (k3s, later):
  - Deployments: webapp, catalog-api; StatefulSet: postgres, minio.
  - Ingress + cert-manager; Secrets via Sealed Secrets or SOPS; PVCs for data.

Backups:
- Nightly Postgres dump (retain N days); object storage versioning or lifecycle rules.

Observability:
- Health endpoints `/healthz` on webapp/api; structured logs; basic metrics (requests, latency, errors).

## Security Considerations

- Validate SRT uploads (size limits, MIME, line count; optional antivirus in future).
- Sanitize and escape rendered subtitle text; prevent HTML/JS injection.
- Rate limit submission endpoints; CAPTCHA optional if abuse occurs.
- JWT signing key rotation; HTTPS everywhere; restrict internal services to private network.

## Testing Strategy

- Unit tests: data model, services, validators.
- Integration tests: API endpoints with in-memory auth and temporary DB.
- Webapp integration: end-to-end flow (login mock, submission post, moderation approve) via `WebApplicationFactory` or Playwright later.

## Milestones & Deliverables

- M0: Catalog API (read-only), importer, webapp pointed to `DATA_CATALOG_URL`.
- M1: Discord OAuth + user model; submissions APIs; presigned uploads; basic moderation UI.
- M2: Full-text search including subtitles; background parsing job; audit logs.
- M3: Ops hardening (backups, metrics), optional CDN, k8s manifests.

## Open Questions

- Do we index subtitles text in API now or defer to Phase 2?
- Which languages for subtitles beyond `en`? Need i18n strategy in UI?
- Do we keep the local `SubtitlesCache` in webapp or move parsing entirely to API?
- Preferred moderation model: per-item approval vs. batch rules?

---

Appendix: Minimal Endpoint Sketch (OpenAPI to be added)

- GET `/api/videos`
  - 200: `{ items: Video[], totalCount, page, pageSize }`
- POST `/api/submissions/subtitles/presign`
  - req: `{ videoId, language, size }`
  - 200: `{ uploadUrl, storageKey, expiresAt }`
- POST `/api/submissions/subtitles/finalize`
  - req: `{ videoId, language, storageKey, checksum }`
  - 201: `{ submissionId, status }`

Video: `{ id, youtubeId, title, creator, description, tags[], releaseDate }`

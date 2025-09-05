# Catalog API — Submissions Queue (Design)

This document defines how external contributors (e.g., the translator app) submit new videos to the catalog for admin review, and how admins approve/reject them.

## Goals
- Accept “new video” proposals via an authenticated API.
- Persist submissions as `pending` until reviewed.
- Admins browse a queue, inspect payload (incl. subtitles), and approve/reject.
- Approval appends a record to `videos.json` and sets a first‑party `subtitleUrl`.
- JSON persistence first; smooth path to DB later.

## Data Model (JSON v1)
- File: `DATA_SUBMISSIONS_PATH` (default `/app/data/submissions.json`)
- Submission:
  - `id` (uuid)
  - `type` = "video"
  - `status` = "pending" | "approved" | "rejected"
  - `submitted_at` (ISO8601)
  - `submitted_by` (string; user or token id)
  - `reviewed_at` (ISO8601, optional)
  - `reviewer_id` (string, optional)
  - `reason` (string, optional)
  - `payload` (object): `{ youtube_id, title, creator, description, tags[], release_date?, subtitle_storage_key? | subtitle_url? }`

## Endpoints
- Ingest (token-protected by `X-Api-Key`; allowlist via `API_INGEST_TOKENS` env)
  - `POST /api/submissions/videos`
    - Body: video payload (above)
    - 202 Accepted → `{ submission_id, status: "pending" }`

- Admin (internal only; webapp proxies with admin check)
  - `GET /api/admin/submissions?status=pending&type=video&page=&pageSize=`
  - `GET /api/admin/submissions/{id}`
  - `PATCH /api/admin/submissions/{id}` with `{ action: "approve" | "reject", reason? }`
    - approve: appends to `videos.json`; if `subtitle_url` present and no `subtitle_storage_key`, mirror to storage first (see Subtitles Storage doc) then write `subtitleUrl` to internal API URL.

## Approval → videos.json
- Write to `DATA_JSON_PATH` (default `/app/data/videos.json`).
- Merge unique by `videoId` (YouTube ID); update title/creator/etc. if necessary.
- `subtitleUrl` should be first‑party: `/api/subtitles/{videoId}/{version}.srt`.

## Security
- Ingest: token allowlist; size limits; basic payload validation; rate limit per token.
- Admin: no public port; webapp proxies and enforces `ADMIN_USER_IDS`.
- Logging/audit: store reviewer, timestamps, optional reason.

## Migration to DB (later)
- Tables: `submissions`, `videos`, `subtitles`, `users`.
- Preserve contracts; repos swap to DbContext; keep admin UI unchanged.

## Testing
- Ingest success/failure (bad token, invalid payload).
- Admin list/filter; approve writes videos.json; reject sets status and reason.
- Mirror external `subtitle_url` on approve; GET subtitle serves internal URL.


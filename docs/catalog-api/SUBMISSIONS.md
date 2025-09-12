# Catalog API — Submissions Queue (Design)

This document defines how external contributors (e.g., the translator app) submit new videos to the catalog for admin review, and how admins approve/reject them.

## Goals
- Accept “new video” proposals via an authenticated API.
- Persist submissions as `pending` until reviewed.
- Admins browse a queue, inspect payload (incl. subtitles), and approve/reject.
- Approval upserts the record into the API‑owned primary videos store and sets a first‑party `subtitleUrl`.
- JSON persistence first (file store), with a smooth path to DB later.

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
    - approve: upserts the video into the primary store. If `subtitle_storage_key` is absent and `subtitle_url` is provided, the API mirrors the SRT into first‑party storage, then writes `subtitleUrl` as `/api/subtitles/{videoId}/1.srt`.

## Approval → Primary Store
- Primary store path: `DATA_VIDEOS_STORE_PATH` (default `/app/data/catalog-videos.json`).
- Merge unique by YouTube ID (`v` field); update title/creator/etc. if necessary.
- `subtitleUrl` is first‑party: `/api/subtitles/{videoId}/{version}.srt` (version `1` currently).
- Legacy `videos.json` (`DATA_JSON_PATH`) is treated as an optional bootstrap source only. On startup, the API may import from it once if the primary store is empty; the API does not write back to `videos.json` at runtime.

## Security
- Ingest: token allowlist; size limits; basic payload validation; rate limit per token.
- Admin: no public port; webapp proxies and enforces `ADMIN_USER_IDS`.
- Logging/audit: store reviewer, timestamps, optional reason.

## API Usage (Examples)

- Submit a video (ingest token required):
  - `curl -H "X-Api-Key: TOKEN1" -H "Content-Type: application/json" -d '{"youtube_id":"abc123","title":"My Video","tags":["z"],"subtitle_storage_key":"subtitles/abc123/v1.srt"}' http://localhost:5002/api/submissions/videos`
  - Response: `{ "submission_id": "...", "status": "pending" }`

- List submissions (admin):
  - `curl http://localhost:5002/api/admin/submissions?status=pending&page=1&pageSize=20`

- Get submission detail (admin):
  - `curl http://localhost:5002/api/admin/submissions/<id>`

- Approve or reject (admin):
  - `curl -X PATCH -H "Content-Type: application/json" -d '{"action":"approve"}' http://localhost:5002/api/admin/submissions/<id>`
  - `curl -X PATCH -H "Content-Type: application/json" -d '{"action":"reject","reason":"Needs fixes"}' http://localhost:5002/api/admin/submissions/<id>`

## Configuration

- `DATA_SUBMISSIONS_PATH`: Path to submissions JSON (default `/app/data/submissions.json`).
- `API_INGEST_TOKENS`: Comma-separated list of allowed ingest tokens (match header `X-Api-Key`).
- `DATA_VIDEOS_STORE_PATH`: Path to the primary videos store JSON (default `/app/data/catalog-videos.json`).
- `DATA_JSON_PATH`: Optional legacy bootstrap JSON (default `/app/data/videos.json`); imported once if the primary store is empty.

## Migration to DB (later)
- Tables: `submissions`, `videos`, `subtitles`, `users`.
- Preserve contracts; repos swap to DbContext; keep admin UI unchanged.

## Testing
- Ingest success/failure (bad token, invalid payload).
- Admin list/filter; approve upserts into primary store; reject sets status and reason.
- Mirror external `subtitle_url` on approve; GET subtitle serves internal URL.

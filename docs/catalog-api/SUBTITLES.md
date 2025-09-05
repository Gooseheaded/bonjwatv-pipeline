# Catalog API — First‑Party Subtitles (Plan)

Serve and store subtitles in our infra with versioning. Start simple on filesystem; upgrade to object storage later without breaking contracts.

## S0 — Filesystem Storage (Now)
- Storage path: `/app/data/subtitles/{videoId}/v{version}.srt` (persisted by `api-data` volume).
- Upload (ingest token required):
  - `POST /api/uploads/subtitles` (multipart or raw text)
  - Validations: ≤1 MB, `text/plain`, basic SRT sanity. Returns `{ storage_key }` like `subtitles/abc123/v1.srt`.
- Serve:
  - `GET /api/subtitles/{videoId}/{version}.srt` → `text/plain; charset=utf-8`, `Cache-Control: public, max-age=3600`.
- Submissions integration:
  - Translator uploads SRT → gets `storage_key` → includes it in video submission.
  - On approve, `videos.json.subtitleUrl` is set to `/api/subtitles/{videoId}/{version}.srt`.

## S1 — Admin UX
- Submissions queue shows a “Preview Subtitles” link (first lines fetched from API).
- Admin can approve/reject with optional reason.

## S2 — Object Storage (Later)
- MinIO/S3 bucket; API issues presigned PUT; returns `storage_key`.
- GET either proxies or redirects to signed GET; keep `/api/subtitles/...` contract stable.
- Migrate existing files from `/app/data/subtitles` to the bucket.

## Backward Compatibility (Pastebin)
- If a submission provides `subtitle_url` (external), approve will mirror it into storage and set internal `subtitleUrl`.
- Optionally lazy-cache on first GET; prefer eager mirror at approval time.

## Security & Limits
- Uploads: token allowlist (`API_INGEST_TOKENS`), size/MIME checks, UTF‑8 normalize, line ending normalize.
- Serving: expose only approved versions; set `Content-Disposition: inline; filename="{videoId}-v{version}.srt"`.

## API Usage (Examples)

- Upload via multipart (preferred):
  - `curl -F "videoId=abc123" -F "version=1" -F "file=@subs.srt;type=text/plain" http://localhost:5002/api/uploads/subtitles`
  - Response: `{ "storage_key": "subtitles/abc123/v1.srt" }`

- Upload raw text data:
  - `curl -H "Content-Type: text/plain" --data-binary @subs.srt "http://localhost:5002/api/uploads/subtitles?videoId=abc123&version=1"`

- Serve a subtitle file:
  - `curl http://localhost:5002/api/subtitles/abc123/1.srt`

## Configuration

- `DATA_SUBTITLES_ROOT`: Root folder for stored subtitles (default `/app/data/subtitles`).
- `UPLOADS_MAX_SUBTITLE_BYTES`: Max accepted upload size in bytes (default `1048576`).

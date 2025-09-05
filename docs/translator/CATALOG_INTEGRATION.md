# Translator → Catalog Integration

Contributors using the translator app can submit new videos to the catalog with subtitles stored first‑party.

## CLI Flow (S0)
- Pre-req: API token from catalog maintainer (`API_INGEST_TOKENS` allowlist).
- Command: `submit_to_catalog.py --catalog-base http://catalog-api:8080 --api-key $TOKEN --videos-json path/to/videos_enriched.json`
- For each selected entry:
  1) Upload SRT (if local): `POST /api/uploads/subtitles` → `{ storage_key }`.
  2) Submit video proposal: `POST /api/submissions/videos` with `{ youtube_id, title, creator, description, tags, release_date?, subtitle_storage_key }`.
  3) Response: `{ submission_id, status: "pending" }`.

## Legacy/External URLs
- If SRT is hosted externally (e.g., Pastebin), include `subtitle_url` instead of `subtitle_storage_key`.
- On admin approval, the API mirrors the SRT into first‑party storage and sets `subtitleUrl` internally.

## Future (S2)
- Switch upload step to presigned URLs for object storage; keep submission payload unchanged.


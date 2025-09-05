# Translator → Catalog Integration

Contributors using the translator app can submit new videos to the catalog with subtitles stored first‑party.

## GUI Flow (S0)
- Pre-req: API token from catalog maintainer (`API_INGEST_TOKENS` allowlist on server; token is shared to contributors out-of-band).
- In the GUI (translator/gui/app.py):
  - Enable checkbox: "Submit results to Catalog after run".
  - Fill "Catalog API base" (e.g., `http://localhost:5002` or server URL).
  - Fill "Ingest token" with the provided token.
- Run the pipeline. After processing, the app uploads `en_{videoId}.srt` files and submits each video proposal automatically.

### Quick Scaffold for Testing
- Create a tiny test run with one video and a ready-made English SRT:
  - `python translator/scaffold_test_submission.py --video-id abc123 --title "Test Video" --base-dir metadata`
  - In the GUI, select `metadata/urls.txt` and check “Submit results to Catalog after run”.
  - Set Catalog API base to `http://localhost:5002` and paste your ingest token.

## Script Flow (alternate)
- Script: `translator/submit_to_catalog.py` (standalone, like other tools).
- Usage: `python translator/submit_to_catalog.py --catalog-base http://localhost:5002 --api-key $TOKEN --videos-json path/to/videos_enriched.json --subtitles-dir path/to/subtitles`
- For each selected entry:
  1) Upload SRT (if local): `POST /api/uploads/subtitles` → `{ storage_key }`.
  2) Submit video proposal: `POST /api/submissions/videos` with `{ youtube_id, title, creator, description, tags, release_date?, subtitle_storage_key }`.
  3) Response: `{ submission_id, status: "pending" }`.

## Legacy/External URLs
- If SRT is hosted externally (e.g., Pastebin), include `subtitle_url` instead of `subtitle_storage_key`.
- On admin approval, the API mirrors the SRT into first‑party storage and sets `subtitleUrl` internally.

## Future (S2)
- Switch upload step to presigned URLs for object storage; keep submission payload unchanged.
## Tokens and Security

- `API_INGEST_TOKENS` are bearer-style ingest tokens configured on the server (compose env) and kept out of git in `.env`.
- Maintainers generate and distribute tokens to trusted contributors (e.g., per-user or per-machine) and can rotate by editing `.env` and restarting the API.
- The translator GUI stores the token in the user’s local settings file; avoid sharing it.
- Server-side rate limiting and JWT-based auth can replace tokens later without changing the submit script contract.
### Quick Scaffold for Script Use
- Scaffold and submit directly:
  - `python translator/scaffold_test_submission.py --video-id abc123 --title "Test Video" --base-dir metadata`
  - `python translator/submit_to_catalog.py --catalog-base http://localhost:5002 --api-key $TOKEN --videos-json metadata/urls/videos_enriched.json --subtitles-dir metadata/urls/subtitles`

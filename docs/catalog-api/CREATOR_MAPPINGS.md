# Catalog API — Creator Canonicalization Endpoints

This spec defines admin endpoints and ingest/approval behavior for the creator canonicalization dictionary.

## Data Store
- File (JSON v1): `DATA_CREATOR_MAPPINGS_PATH` (default `/app/data/creator-mappings.json`).
- Record: `{ id: string (uuid), source: string, source_normalized: string, canonical: string, notes?: string, created_at: string, created_by?: string, updated_at: string }`.
- Normalization: Unicode NFKC → trim → collapse internal spaces; `source_normalized` is lowercase.
- Uniqueness: unique on `source_normalized`.

## Admin Endpoints (auth: admin only)
- GET `/api/admin/creators/mappings`
  - Query: `q?` (search over source/canonical), `page?`, `pageSize?` (defaults: 1, 50), `sort?` (updated_at desc).
  - 200: `{ items: Mapping[], totalCount, page, pageSize }`.

- POST `/api/admin/creators/mappings`
  - Body: `{ source: string, canonical: string, notes?: string }`.
  - 201: `Mapping` (with server fields populated).
  - 409: duplicate `source` (after normalization).

- PUT `/api/admin/creators/mappings/{id}`
  - Body: `{ source: string, canonical: string, notes?: string }` (full update).
  - 200: updated `Mapping`.
  - 409: duplicate `source` (after normalization) if changing source.

- DELETE `/api/admin/creators/mappings/{id}`
  - 204 on success.

- POST `/api/admin/creators/mappings/reapply`
  - Body: `{ scope?: "all" | "videos" | "submissions" }` (default `all`).
  - 202: `{ updated_videos: number, updated_submissions: number }` (counts).
  - Behavior: For each record, recompute `creator_canonical` from `creator_original` using current mappings; update only when different.

## Ingest & Approval Behavior
- Submissions (POST `.../api/submissions/videos`):
  - Input payload includes `creator`.
  - Server persists `creator_original = creator` and `creator_canonical = Resolve(creator) ?? creator`.
  - Store both in the submission envelope payload.

- Approval (Admin):
  - When upserting into the videos store, recompute canonical from `creator_original` with current mappings.
  - Videos store persists both `creator_original` and `creator_canonical`.

## Public Read DTOs
- List/Detail: expose `creator` = canonical value; omit original for public endpoints.
- Admin detail endpoints may include `creator_original` for audit.

## Validation
- `source`: 1..200 chars after trim; reject if contains control chars.
- `canonical`: 1..100 chars; same sanitation rules.
- Notes: 0..500 chars.

## Audit & Metrics
- Emit audit logs for create/update/delete/reapply (actor, before/after, counts affected).
- Expose counts of mappings and last reapply timestamp via an admin stats endpoint (optional).

## Error Codes
- 400 invalid payload, 401/403 unauthorized, 404 not found, 409 conflict (duplicate), 422 validation errors.


# ADR 0003: Creator Canonicalization Dictionary

- Status: Proposed
- Date: 2025-09-13

## Context
Incoming submissions often include creator names in Korean or mixed forms (e.g., "파도튜브[PADOTUBE]"). We want public UI to present a stable, concise English name (e.g., "Pado") while preserving the original for audit. Mapping must be admin‑managed, applied at ingest/approval time, and reapplicable to existing data without manual edits.

## Decision
- Introduce an admin‑managed creator canonicalization dictionary.
- Store mappings in a durable API data store (JSON first), keyed by a normalized `source` string, with a `canonical` target string.
- Persist both `creator_original` and `creator_canonical` on submissions and videos. Public DTOs expose `creator` = canonical; admin/detail surfaces include the original.
- Apply mapping:
  - On submission ingest: compute and store canonical alongside original.
  - On approval to videos store: recompute to reflect latest mapping.
  - Provide an admin "Reapply" operation to batch update existing videos and pending submissions.
- Provide full CRUD and audit for mappings via admin API endpoints and a Webapp Admin UI.

## Alternatives
- Free‑text edits by admins on each submission/video: rejected (labor intensive, inconsistent, no reuse).
- Regex/partial matching rules: deferred; explicit 1:1 source→canonical pairs are simpler and predictable for MVP.
- Mapping in the webapp only: rejected; mapping belongs to the Catalog API as source of truth for data.

## Consequences
- New data file: `DATA_CREATOR_MAPPINGS_PATH` (default `/app/data/creator-mappings.json`).
- API surface expands with admin endpoints and a reapply operation; requires admin authz and audit logging.
- UI shows canonical everywhere public; admin pages show both original and canonical.
- Backfill path exists; admins can reapply after changes.

## Implementation Sketch
- Normalization: Unicode NFKC → trim → collapse internal spaces; case‑insensitive comparisons on `source`. Enforce uniqueness on normalized `source`.
- Mapping record: `{ id, source, source_normalized, canonical, notes?, created_at, created_by, updated_at }`.
- Service: `ICreatorCanonicalizer.Resolve(source) -> (canonical | null)` and helpers to apply to submissions/videos.
- Endpoints (admin‑only):
  - `GET /api/admin/creators/mappings`
  - `POST /api/admin/creators/mappings`
  - `PUT /api/admin/creators/mappings/{id}`
  - `DELETE /api/admin/creators/mappings/{id}`
  - `POST /api/admin/creators/mappings/reapply`
- DTOs:
  - Public list/detail: `creator` = canonical; (optionally) include `creator_original` for authenticated admin views.

## Testing Plan (TDD)
- Unit (API):
  - Normalization cases (Unicode variants, whitespace, case).
  - Resolve precedence and exact match behavior.
  - CRUD validation and uniqueness on `source_normalized`.
- Integration (API):
  - Submission ingest persists both `creator_original` and computed `creator_canonical`.
  - Approval writes canonicalized creator into videos store; follows latest mapping.
  - Reapply updates affected records; idempotent when no changes.
  - Admin endpoints authz and audit events emitted.
- Contract (API↔Webapp):
  - Webapp clients parse canonical `creator` in list/detail; admin views read `creator_original`.
- Webapp UI:
  - Admin pages: list/create/edit/delete/reapply flows; permissions enforced; form validations.
  - Try‑it widget resolves a sample source via API.

## Status
- Proposed for implementation. Session 2025‑09‑13 captures rollout plan and next steps.


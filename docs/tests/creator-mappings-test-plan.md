# Test Plan — Creator Canonicalization

Comprehensive tests to drive development of creator mappings across Catalog API and Webapp.

## API — Unit Tests
- Normalization
  - Converts full‑width/compatibility forms to NFKC.
  - Trims leading/trailing whitespace; collapses internal whitespace runs to single space.
  - Lowercases for `source_normalized`; does not alter `canonical` casing.
- Resolve
  - Exact match on normalized `source` returns `canonical`.
  - Case/whitespace variants resolve identically.
  - Non‑existent source returns null.
- Validation
  - Reject empty/too‑long fields; reject control characters.
  - Uniqueness enforced on `source_normalized` (create/update).

## API — Integration Tests
- CRUD Endpoints (admin auth simulated)
  - Create: 201 with record; duplicate `source` → 409.
  - Update: 200 with updated values; change `source` to collide → 409.
  - Delete: 204; subsequent get excludes record.
  - List: pagination, sorting by updated_at desc; `q` filters by source/canonical substrings.
- Ingest
  - POST submissions/videos: payload `creator` persists as `creator_original`; `creator_canonical` computed via mapping (or echo if no mapping).
  - Edge: creator with extra spaces/case still resolves to same canonical.
- Approval
  - Approving a submission writes both fields to videos store; uses latest mapping (simulate mapping change between ingest and approval).
- Reapply
  - After changing mapping, `reapply` updates existing videos and pending submissions.
  - Idempotency: reapplying without mapping changes yields zero updates.
  - Counts: response matches actual updated rows.
- Public DTOs
  - List/detail endpoints expose `creator` = canonical; do not leak `creator_original` to public.

## Webapp — Unit/Component Tests
- Client
  - `HttpCreatorMappingsClient` calls correct endpoints and parses responses.
  - Error handling: surfaces 409 conflict and validation errors.
- Helpers
  - Local normalization helper mirrors API normalization (if implemented client‑side for try‑it widget).

## Webapp — Integration/UI Tests
- AuthZ
  - Non‑admin cannot access `/Admin/CreatorMappings*` (403/redirect).
- List
  - Renders items, pagination works, search filters.
- Create/Edit/Delete
  - Create shows in list; duplicate source shows validation error.
  - Edit updates row; Delete removes row after confirm.
- Reapply
  - Button calls endpoint; shows spinner; displays updated counts on success.
- Try‑it
  - Entering a known source shows expected canonical; unknown shows "no mapping".

## Cross‑Cutting
- Audit
  - API emits audit log entries for create/update/delete/reapply with actor and diffs.
- Performance
  - Reapply on 10k videos runs within acceptable time; test with in‑memory stores or seeded temp files.

## Fixtures & Utilities
- Sample mappings: e.g., `{"source":"파도튜브[PADOTUBE]","canonical":"Pado"}` plus variants.
- Sample submissions/videos with mixed creator forms for reapply tests.


---
date: 2025-09-13
tags: [sessions]
env: both
---

# Session — 2025-09-13 (Creator Mappings)

## Context
- Plan and document an Admin‑editable dictionary to canonicalize creator names (e.g., map "파도튜브[PADOTUBE]" → "Pado").
- Goal: apply canonical names at submission time and approval, show canonical publicly, preserve original for admin/audit.

## Changes
- Wrote ADR 0003 defining the approach, data model, endpoints, and testing plan.
- Drafted outlines for API endpoints and Webapp Admin UI skeleton to drive TDD implementation.

## Decisions
- Catalog API owns the mapping data and canonicalization logic.
- Persist both `creator_original` and `creator_canonical`; public DTOs use canonical; admin views include original.
- Normalization uses Unicode NFKC + trim + space collapse; exact match per normalized `source`.
- Provide a batch "Reapply" to backfill existing data after mapping changes.

## Next Steps
- Implement API: data file store, canonicalizer service, admin CRUD endpoints, reapply operation, DTO updates.
- Implement Webapp Admin pages: list/create/edit/delete/reapply and try‑it widget.
- Add tests (unit, integration, UI) per ADR test plan.

## Links
- ADR: ../adrs/0003-creator-canonicalization-dictionary.md


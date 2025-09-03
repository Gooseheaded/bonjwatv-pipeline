# ADR 0001: Session-based Documentation

- Status: Accepted
- Date: 2025-09-03

## Context
Static docs lacked recency and decision traceability, making it hard to assess what is current. We want time-stamped session notes to capture context and decisions, while keeping a small set of evergreen docs authoritative.

## Decision
- Adopt time-based session notes under `docs/sessions/`, newest-first, using a standard template.
- Maintain evergreen docs (`DEVELOPING.md`, `docs/DEPLOYING.md`, etc.) as the canonical source of stable procedures.
- When a session yields durable guidance, promote it into the relevant evergreen doc and reference that promotion in the session entry.
- Maintain a Sessions index and a lightweight script to scaffold new sessions and update the index.

## Consequences
- Better recency and decision traceability.
- Slight process overhead: authors must keep evergreen docs in sync and use the template.
- Clearer onboarding path: read the latest session(s) for context, then the evergreen docs for procedures.


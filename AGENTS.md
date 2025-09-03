# Agents Guide

This repo uses time-stamped session notes plus concise evergreen docs. When contributing as an AI agent or automation, follow these rules.

Session Notes
- When: write a session note for any focused working block (roughly ≥30 minutes), at minimum once per PR/feature, and after incidents or notable environment changes.
- How: run `scripts/new-session.sh [YYYY-MM-DD]` to scaffold; use `docs/sessions/_template.md` and fill Context, Changes, Decisions, Next Steps, Links.
- Link, don’t paste: store large outputs under `docs/logs/` and link them from the session.
- Ordering: newest-first index is auto-updated by the script (`docs/sessions/README.md`).

Evergreen Docs
- Promote stable guidance to `DEVELOPING.md` or `docs/DEPLOYING.md`. Link back from the originating session entry.
- For long-lived choices, add an ADR under `docs/adrs/` and reference it from the session.

ADRs (Architecture Decision Records)
- When: create an ADR for decisions that affect architecture, contracts/compatibility, security posture, data models/migrations, deployment strategy, or team conventions.
- Not for: small refactors or transient workarounds (keep those in sessions).
- How: run `scripts/new-adr.sh "Concise Title"` to scaffold the next ADR number from `docs/adrs/_template.md`.
- Structure: Context, Decision, Alternatives, Consequences, Status. Keep it brief and specific; link related sessions/PRs.

Commits & Changes
- Prefer small, purposeful changes; describe intent clearly in commit titles.
- Avoid destructive operations without explicit instruction. Back up or move files outside the repo when replacing directories.

Monorepo Conventions
- Projects live at the repo root: `webapp/`, `catalog-api/`, `translator/`.
- Use `scripts/monorepo-subtree-import.sh` to import external history (with `--replace-existing` when needed).
- Run `scripts/monorepo-after-merge.sh` to sync the solution and verify.

Run/Deploy
- Local dev: `docker compose up --build` (webapp on 5001, catalog-api on 5002 with Swagger in dev).
- Production: `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build` (webapp on 80, API internal only).

Quick Commands
- New session: `scripts/new-session.sh` (today) or `scripts/new-session.sh 2025-09-04`
- New ADR: `scripts/new-adr.sh "Short Decision Title"`

#!/usr/bin/env bash
set -euo pipefail

# Scaffold a new session note and update the sessions index.

DATE_ARG="${1:-}"
if [[ -z "$DATE_ARG" ]]; then
  DATE_ARG=$(date +%F)
fi

SESS_DIR="docs/sessions"
TEMPLATE="$SESS_DIR/_template.md"
OUT="$SESS_DIR/session-$DATE_ARG.md"
INDEX="$SESS_DIR/README.md"

if [[ ! -d "$SESS_DIR" ]]; then
  mkdir -p "$SESS_DIR"
fi

if [[ -e "$OUT" ]]; then
  echo "Session $OUT already exists." >&2
else
  if [[ -f "$TEMPLATE" ]]; then
    sed "s/{{DATE}}/$DATE_ARG/g" "$TEMPLATE" > "$OUT"
  else
    cat > "$OUT" <<EOF
# Session — $DATE_ARG

## Context

## Changes

## Decisions

## Next Steps

## Links

EOF
  fi
  echo "Created $OUT"
fi

# Rebuild index (newest first)
{
  echo "# Sessions Index"
  echo
  echo "Time-ordered session notes capturing decisions, changes, and next steps. Prefer newest-first for quick context."
  echo
  echo "## Sessions"
  echo
  find "$SESS_DIR" -maxdepth 1 -type f -name 'session-*.md' \
    -printf '%f\n' | sort -r | while read -r f; do
      d="${f#session-}"
      d="${d%.md}"
      echo "- [${d}](session-${d}.md)"
    done
  echo
  echo "Guidance"
  echo "- Use the template: \`_template.md\`."
  echo "- Create a new entry with \`scripts/new-session.sh\`."
  echo "- Promote durable guidance into evergreen docs (DEPLOYING.md, DEVELOPING.md) and/or ADRs."
} > "$INDEX"

echo "Updated $INDEX"

# Ensure docs/README.md references the sessions index and ADRs
DOCS_README="docs/README.md"
if ! grep -qi 'Sessions (newest first)' "$DOCS_README"; then
  {
    echo
    echo "Sessions & ADRs"
    echo
    echo "- Sessions (newest first): [docs/sessions/](sessions/README.md)"
    echo "- ADRs: [docs/adrs/](../docs/adrs/)"
  } >> "$DOCS_README"
  echo "Updated $DOCS_README with Sessions & ADRs section"
fi


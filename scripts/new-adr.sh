#!/usr/bin/env bash
set -euo pipefail

# Scaffold a new ADR from the template, incrementing the number.

TITLE_RAW="${1:-}"
if [[ -z "$TITLE_RAW" ]]; then
  echo "Usage: $0 \"Concise Title\"" >&2
  exit 2
fi

ADR_DIR="docs/adrs"
TEMPLATE="$ADR_DIR/_template.md"
DATE=$(date +%F)

mkdir -p "$ADR_DIR"

# Determine next ADR number (3-digit, zero-padded)
LAST=$(ls "$ADR_DIR" 2>/dev/null | grep -E '^[0-9]{4}-|^[0-9]{3,4}-|^[0-9]{4}_|^[0-9]{3}-' -o | sed 's/[^0-9].*$//' | sort -n | tail -n1 || true)
if [[ -z "$LAST" ]]; then
  LAST=1
else
  LAST=$((LAST+1))
fi

# Normalize to 4 digits for consistency
NUM=$(printf "%04d" "$LAST")
SLUG=$(echo "$TITLE_RAW" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g')
OUT="$ADR_DIR/${NUM}-${SLUG}.md"

if [[ -e "$OUT" ]]; then
  echo "ADR already exists: $OUT" >&2
  exit 1
fi

if [[ -f "$TEMPLATE" ]]; then
  sed -e "s/{{NUMBER}}/$NUM/g" -e "s/{{TITLE}}/$TITLE_RAW/g" -e "s/{{DATE}}/$DATE/g" "$TEMPLATE" > "$OUT"
else
  cat > "$OUT" <<EOF
# ADR $NUM: $TITLE_RAW

- Status: Proposed
- Date: $DATE

## Context

## Decision

## Alternatives

## Consequences

EOF
fi

echo "Created $OUT"

# Update ADR index
INDEX="$ADR_DIR/README.md"
{
  echo "# ADRs Index"
  echo
  echo "Architecture Decision Records (ADRs) documenting significant, long-lived decisions."
  echo
  echo "## ADRs"
  echo
  find "$ADR_DIR" -maxdepth 1 -type f -name '[0-9][0-9][0-9][0-9]-*.md' -printf '%f\n' | sort -n | while read -r f; do
    base="${f%.md}"
    echo "- [${base}](./${f})"
  done
  echo
  echo "Create a new ADR"
  echo "- Use the template \`_template.md\` and the helper script: \`scripts/new-adr.sh \"Concise Title\"\`"
} > "$INDEX"

echo "Updated $INDEX"


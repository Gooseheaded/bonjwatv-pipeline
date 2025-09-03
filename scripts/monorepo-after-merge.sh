#!/usr/bin/env bash
set -euo pipefail

# Post-merge tasks for the BWKT monorepo.
# - Ensures a solution exists and adds discovered .csproj files.
# - Shows a summary and next steps. Idempotent.

ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT"

SOLUTION="bwkt.sln"

ensure_dotnet() {
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet SDK is required but not installed." >&2
    exit 1
  fi
}

ensure_solution() {
  if [[ -f "$SOLUTION" ]]; then
    echo "Solution found: $SOLUTION"
  else
    echo "Creating solution: $SOLUTION"
    dotnet new sln -n "bwkt"
  fi
}

add_csproj_if_any() {
  local dir="$1"
  if [[ -d "$dir" ]]; then
    local csproj
    csproj=$(find "$dir" -maxdepth 2 -name '*.csproj' | head -n1 || true)
    if [[ -n "${csproj:-}" ]]; then
      echo "Adding $csproj to $SOLUTION (if not already present)"
      dotnet sln "$SOLUTION" add "$csproj" >/dev/null 2>&1 || true
    else
      echo "No .csproj found under $dir (skip)"
    fi
  fi
}

ensure_dotnet
ensure_solution

add_csproj_if_any "webapp"
add_csproj_if_any "catalog-api"
add_csproj_if_any "translator"

echo "Solution projects:"
dotnet sln "$SOLUTION" list || true

echo
echo "Next steps:"
echo "- Build locally: dotnet build $SOLUTION"
echo "- Run with Compose (dev): docker compose up --build"
echo "- Deploy with prod overrides: docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build"


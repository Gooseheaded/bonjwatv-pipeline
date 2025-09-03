#!/usr/bin/env bash
set -euo pipefail

# Import sibling repositories into this repo as a monorepo using git subtree.
# Idempotent: adds when missing, pulls to update when present.
#
# Usage examples:
#   scripts/monorepo-subtree-import.sh \
#     --webapp-url git@github.com:you/webapp.git --webapp-branch main \
#     --catalog-url git@github.com:you/catalog-api.git --catalog-branch main \
#     --translator-url git@github.com:you/translator.git --translator-branch main
#
# You can omit any project you don't want to import right now.

WEBAPP_URL=""
WEBAPP_BRANCH="main"
CATALOG_URL=""
CATALOG_BRANCH="main"
TRANSLATOR_URL=""
TRANSLATOR_BRANCH="main"
REPLACE_EXISTING=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --webapp-url) WEBAPP_URL="$2"; shift 2 ;;
    --webapp-branch) WEBAPP_BRANCH="$2"; shift 2 ;;
    --catalog-url) CATALOG_URL="$2"; shift 2 ;;
    --catalog-branch) CATALOG_BRANCH="$2"; shift 2 ;;
    --translator-url) TRANSLATOR_URL="$2"; shift 2 ;;
    --translator-branch) TRANSLATOR_BRANCH="$2"; shift 2 ;;
    --replace-existing) REPLACE_EXISTING=1; shift 1 ;;
    -h|--help)
      sed -n '1,60p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

require_git_repo() {
  if ! git rev-parse --git-dir >/dev/null 2>&1; then
    echo "ERROR: Not inside a git repository" >&2
    exit 1
  fi
}

ensure_initial_commit() {
  if ! git rev-parse --verify HEAD >/dev/null 2>&1; then
    echo "Creating initial empty commit to enable subtree operations ..."
    git commit --allow-empty -m "chore(monorepo): initial empty commit"
  fi
}

ensure_remote() {
  local name="$1" url="$2"
  if git remote get-url "$name" >/dev/null 2>&1; then
    # Update URL only if provided and different
    if [[ -n "$url" ]]; then
      current=$(git remote get-url "$name")
      if [[ "$current" != "$url" ]]; then
        git remote set-url "$name" "$url"
      fi
    fi
  else
    git remote add "$name" "$url"
  fi
}

add_or_pull_subtree() {
  local prefix="$1" remote="$2" branch="$3"
  echo "Fetching $remote ..."
  git fetch "$remote" --prune

  # Ensure we have at least one commit to avoid HEAD errors
  ensure_initial_commit

  if [[ -d "$prefix" && -n "$(ls -A "$prefix" 2>/dev/null || true)" ]]; then
    echo "Detected existing directory at $prefix"
    if [[ $REPLACE_EXISTING -eq 1 ]]; then
      local repo_root
      repo_root=$(git rev-parse --show-toplevel)
      local parent_dir
      parent_dir=$(dirname "$repo_root")
      local bak="$parent_dir/${prefix}.bak.$(date +%Y%m%d%H%M%S)"
      echo "--replace-existing set: moving $prefix -> $bak (outside repo)"
      mv "$prefix" "$bak"
      # Stage deletions (if any were tracked) and commit a checkpoint
      git add -A "$prefix" || true
      git commit -m "chore(monorepo): backup existing $prefix to $bak before subtree add" || git commit --allow-empty -m "chore(monorepo): checkpoint before subtree add"
      echo "Adding subtree at $prefix from $remote/$branch ..."
      git subtree add --prefix="$prefix" "$remote" "$branch" -m "subtree: add $remote/$branch into $prefix"
      echo "Done. Review differences with: git diff --stat HEAD~1..HEAD"
    else
      echo "Attempting subtree pull (assuming $prefix was previously added as a subtree) ..."
      if ! git subtree pull --prefix="$prefix" "$remote" "$branch" -m "subtree: pull $remote/$branch into $prefix"; then
        echo "ERROR: $prefix exists and subtree pull failed."
        echo "Hint: Re-run with --replace-existing to back up the current folder and add the subtree afresh."
        exit 1
      fi
    fi
  else
    echo "Adding subtree at $prefix from $remote/$branch ..."
    git subtree add --prefix="$prefix" "$remote" "$branch" -m "subtree: add $remote/$branch into $prefix"
  fi
}

require_git_repo
ensure_initial_commit

# Webapp
if [[ -n "$WEBAPP_URL" ]]; then
  ensure_remote webapp-origin "$WEBAPP_URL"
  add_or_pull_subtree "webapp" "webapp-origin" "$WEBAPP_BRANCH"
else
  echo "Skipping webapp (no --webapp-url provided)"
fi

# Catalog API
if [[ -n "$CATALOG_URL" ]]; then
  ensure_remote catalog-origin "$CATALOG_URL"
  add_or_pull_subtree "catalog-api" "catalog-origin" "$CATALOG_BRANCH"
else
  echo "Skipping catalog-api (no --catalog-url provided)"
fi

# Translator
if [[ -n "$TRANSLATOR_URL" ]]; then
  ensure_remote translator-origin "$TRANSLATOR_URL"
  add_or_pull_subtree "translator" "translator-origin" "$TRANSLATOR_BRANCH"
else
  echo "Skipping translator (no --translator-url provided)"
fi

echo "Done. Verify with: git log --graph --decorate --oneline --all"

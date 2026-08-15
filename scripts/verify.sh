#!/usr/bin/env bash
# Runs every check CI runs, in the same order, against the local tree.
# Usage: scripts/verify.sh [backend|frontend|extension]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# The .NET SDK is not always on PATH when installed per-user.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi

TARGET="${1:-all}"
FAILED=()

step() {
  local name="$1"; shift
  printf '\n\033[1;36m==> %s\033[0m\n' "$name"
  if "$@"; then
    printf '\033[1;32m    ok\033[0m\n'
  else
    printf '\033[1;31m    FAILED\033[0m\n'
    FAILED+=("$name")
  fi
}

in_dir() { local d="$1"; shift; ( cd "$d" && "$@" ); }

if [ "$TARGET" = "all" ] || [ "$TARGET" = "backend" ]; then
  step "backend: restore" dotnet restore
  step "backend: build"   dotnet build --no-restore -c Release
  step "backend: test"    dotnet test --no-build -c Release
fi

if [ "$TARGET" = "all" ] || [ "$TARGET" = "frontend" ]; then
  if [ -f frontend/package.json ]; then
    step "frontend: typecheck" in_dir frontend npm run typecheck
    step "frontend: lint"      in_dir frontend npm run lint
    step "frontend: build"     in_dir frontend npm run build
    step "frontend: test"      in_dir frontend npm run test -- --run
  fi
fi

if [ "$TARGET" = "all" ] || [ "$TARGET" = "extension" ]; then
  if [ -f extension/package.json ]; then
    step "extension: typecheck" in_dir extension npm run typecheck
    step "extension: build"     in_dir extension npm run build
    step "extension: test"      in_dir extension npm run test -- --run
  fi
fi

printf '\n'
if [ ${#FAILED[@]} -eq 0 ]; then
  printf '\033[1;32mAll checks passed.\033[0m\n'
  exit 0
fi
printf '\033[1;31m%d check(s) failed:\033[0m\n' "${#FAILED[@]}"
printf '  - %s\n' "${FAILED[@]}"
exit 1

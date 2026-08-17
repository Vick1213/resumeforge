#!/usr/bin/env bash
# Starts the backend and the frontend together, wired to your local config.
#
#   scripts/dev.sh              both (API :5217, web :5173)
#   scripts/dev.sh backend      API only
#   scripts/dev.sh frontend     web only
#   scripts/dev.sh set-key      store a provider API key (prompts, never echoes)
#   scripts/dev.sh env          show what the run would use, then exit
#
# Secrets and machine-local paths live in $RESUMEFORGE_ENV_FILE
# (default ~/.config/resumeforge/env), which is sourced if present. That file is
# outside the repository on purpose — nothing here ever writes a key into the tree.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

ENV_FILE="${RESUMEFORGE_ENV_FILE:-$HOME/.config/resumeforge/env}"

# The .NET SDK is not always on PATH when installed per-user.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi

note() { printf '\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m    %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m%s\033[0m\n' "$*" >&2; exit 1; }

if [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090
  set -a; . "$ENV_FILE"; set +a
fi

# Which provider the backend will pick, mirroring AiProviderCatalog's `auto` order.
resolved_provider() {
  local explicit="${ResumeForge__Ai__Provider:-}"
  if [ -n "$explicit" ] && [ "$explicit" != "auto" ]; then echo "$explicit"; return; fi
  if [ -n "${DEEPSEEK_API_KEY:-}" ]; then echo "deepseek"; return; fi
  if [ -n "${ANTHROPIC_API_KEY:-}" ]; then echo "anthropic"; return; fi
  echo "heuristic (offline — no API key set)"
}

set_key() {
  local var="${1:-DEEPSEEK_API_KEY}" key
  printf 'Paste your %s (input hidden): ' "$var"
  read -rs key; printf '\n'
  [ -n "$key" ] || die "Nothing entered; no change made."

  mkdir -p "$(dirname "$ENV_FILE")"
  touch "$ENV_FILE"; chmod 600 "$ENV_FILE"

  # Replace an existing line for this var, otherwise append.
  local tmp; tmp="$(mktemp)"; chmod 600 "$tmp"
  grep -v "^${var}=" "$ENV_FILE" > "$tmp" 2>/dev/null || true
  printf '%s=%s\n' "$var" "$key" >> "$tmp"
  mv "$tmp" "$ENV_FILE"; chmod 600 "$ENV_FILE"

  note "Stored $var in $ENV_FILE (mode 600)."
  warn "Provider now resolves to: $(set -a; . "$ENV_FILE"; set +a; resolved_provider)"
}

show_env() {
  note "Config for this run"
  printf '  env file      %s%s\n' "$ENV_FILE" "$([ -f "$ENV_FILE" ] || echo '  (missing)')"
  printf '  profile root  %s\n' "${ResumeForge__ProfileRoot:-<unset — falls back to ./profile>}"
  printf '  provider      %s\n' "$(resolved_provider)"
  printf '  dotnet        %s\n' "$(command -v dotnet || echo '<not found>')"
}

PIDS=()

# Both `dotnet run` and `npm run dev` spawn the process that actually holds the
# port as a grandchild, so signalling the direct child alone leaves the port
# bound. Each service therefore gets its own process group (setsid, which makes
# $! the group leader) and shutdown signals the whole group.
cleanup() {
  trap - INT TERM EXIT
  [ ${#PIDS[@]} -eq 0 ] && exit 0
  note "Shutting down"
  for pid in "${PIDS[@]}"; do kill -TERM -- "-$pid" 2>/dev/null; done
  wait "${PIDS[@]}" 2>/dev/null
  exit 0
}

start_backend() {
  command -v dotnet >/dev/null 2>&1 || die "dotnet not found. Install the .NET 10 SDK, or check ~/.dotnet exists."
  note "Backend  http://localhost:5217  (docs at /docs)"
  setsid dotnet run --project backend/src/ResumeForge.Api &
  PIDS+=($!)
}

start_frontend() {
  [ -d frontend/node_modules ] || { note "Installing frontend dependencies"; (cd frontend && npm install) || die "npm install failed."; }
  note "Frontend http://localhost:5173"
  setsid bash -c 'cd frontend && exec npm run dev' &
  PIDS+=($!)
}

case "${1:-all}" in
  set-key)  set_key "${2:-DEEPSEEK_API_KEY}"; exit 0 ;;
  env)      show_env; exit 0 ;;
  backend)  TARGET=backend ;;
  frontend) TARGET=frontend ;;
  all|"")   TARGET=all ;;
  -h|--help) sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *)        die "Unknown argument '$1'. Try: backend | frontend | set-key | env" ;;
esac

show_env
[ -n "${ResumeForge__ProfileRoot:-}" ] || warn "No profile root set — using the sample knowledge base in ./profile."
printf '\n'

trap cleanup INT TERM EXIT
if [ "$TARGET" = "all" ] || [ "$TARGET" = "backend" ];  then start_backend;  fi
if [ "$TARGET" = "all" ] || [ "$TARGET" = "frontend" ]; then start_frontend; fi

# Exit as soon as either process dies, so a crashed backend doesn't leave a
# frontend running against nothing.
wait -n
cleanup

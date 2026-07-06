#!/bin/bash
# SessionStart hook — provision a .NET development environment for Claude Code on the web.
#
# What it does (idempotent, non-interactive, safe to re-run):
#   1. Installs the .NET 10 SDK if it is not already present.
#         - Preferred: the official dotnet-install.sh into ~/.dotnet (no sudo).
#         - Fallback:  the Ubuntu archive via apt (reachable even when Microsoft
#                      download hosts are blocked by the environment's network policy).
#   2. Best-effort: installs the `wasm-tools` workload (required to build the Blazor
#      WASM app) and the front-end npm dependencies. These need Microsoft package
#      hosts / npm and are non-fatal, so the session still starts if they are blocked.
set -uo pipefail

# Only run inside Claude Code on the web (remote) sessions.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

log() { echo "[session-start] $*" >&2; }

DOTNET_CHANNEL="10.0"
PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(pwd)}"

# ---------------------------------------------------------------------------
# 1. .NET SDK
# ---------------------------------------------------------------------------
install_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    log "dotnet already present ($(dotnet --version 2>/dev/null)); skipping install"
    return 0
  fi

  # Preferred: official installer (no sudo, isolated in ~/.dotnet).
  log "installing .NET SDK $DOTNET_CHANNEL via dotnet-install.sh ..."
  if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh 2>/dev/null \
     && bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet" >/dev/null 2>&1; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
    {
      echo "export DOTNET_ROOT=\"$HOME/.dotnet\""
      echo "export PATH=\"$HOME/.dotnet:\$PATH\""
    } >> "$CLAUDE_ENV_FILE"
    log "dotnet installed to ~/.dotnet"
    return 0
  fi

  # Fallback: Ubuntu archive via apt (works when Microsoft hosts are blocked).
  log "dotnet-install.sh unavailable; falling back to apt (Ubuntu archive) ..."
  # Third-party PPAs are occasionally unreachable and break `apt-get update`; move them aside.
  sudo mkdir -p /etc/apt/disabled-ppas 2>/dev/null || true
  for f in /etc/apt/sources.list.d/*deadsnakes* /etc/apt/sources.list.d/*ondrej*; do
    [ -e "$f" ] && sudo mv "$f" /etc/apt/disabled-ppas/ 2>/dev/null || true
  done
  sudo apt-get update -o Acquire::http::Timeout=30 >/dev/null 2>&1 || true
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y dotnet-sdk-10.0 >/dev/null 2>&1 || true
  command -v dotnet >/dev/null 2>&1
}

if install_dotnet; then
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1" >> "$CLAUDE_ENV_FILE"
  echo "export DOTNET_NOLOGO=1" >> "$CLAUDE_ENV_FILE"
  log "dotnet ready: $(dotnet --version 2>/dev/null || echo unknown)"
else
  log "WARNING: could not install the .NET SDK automatically (check the environment's network policy)."
fi

# ---------------------------------------------------------------------------
# 2. Best-effort extras for building/running the Blazor WASM app (non-fatal).
# ---------------------------------------------------------------------------
if command -v dotnet >/dev/null 2>&1; then
  if dotnet workload list 2>/dev/null | grep -qiw wasm-tools; then
    log "wasm-tools workload already installed"
  else
    log "installing wasm-tools workload (needed to build the WASM app; needs Microsoft package hosts) ..."
    dotnet workload install wasm-tools >/dev/null 2>&1 \
      && log "wasm-tools installed" \
      || log "wasm-tools install skipped/failed — full WASM builds need a network policy that allows Microsoft package hosts"
  fi
fi

if command -v npm >/dev/null 2>&1 && [ -f "$PROJECT_DIR/src/EfCorePlayground/package.json" ]; then
  log "installing front-end npm dependencies (Vite/Monaco) ..."
  ( cd "$PROJECT_DIR/src/EfCorePlayground" && npm install --no-audit --no-fund >/dev/null 2>&1 ) \
    && log "npm dependencies installed" \
    || log "npm install skipped/failed"
fi

log "setup complete"
exit 0

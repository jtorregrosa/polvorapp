#!/usr/bin/env bash
# Prepares a Claude Code cloud session (Ubuntu VM) for PolvorApp; see docs/development.md.
#
#   bash scripts/cloud-setup.sh            the cloud environment's setup script: installs the
#                                          .NET 10 SDK and Node 24, which the image lacks
#   bash scripts/cloud-setup.sh --session  the SessionStart hook (.claude/settings.json): the same,
#                                          then puts Node 24 first on PATH for the session, starts
#                                          dockerd (Testcontainers, docker compose) and installs the
#                                          frontend dependencies
#
# Every step is idempotent and skips what is already there. Outside a cloud session (no
# CLAUDE_CODE_REMOTE=true) the hook does nothing, so local sessions are untouched.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NODE_MAJOR="$(tr -d '[:space:]v' < "$ROOT/frontend/.nvmrc")"
NODE_DIR="/opt/node${NODE_MAJOR}"
DOTNET_DIR="/opt/dotnet"
SESSION=false
[ "${1:-}" = "--session" ] && SESSION=true

if $SESSION && [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

SUDO=""
if [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1; then
  SUDO="sudo -n"
fi

log() { echo "[cloud-setup] $*" >&2; }

ensure_dotnet() {
  local wanted
  wanted="$(sed -n 's/.*"version": *"\([0-9]*\)\..*/\1/p' "$ROOT/backend/global.json" | head -1)"
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q "^${wanted}\."; then
    return
  fi
  log "installing the .NET ${wanted} SDK into ${DOTNET_DIR}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  $SUDO bash /tmp/dotnet-install.sh --channel "${wanted}.0" --install-dir "$DOTNET_DIR" >/dev/null
  $SUDO ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
}

ensure_node() {
  if [ -x "$NODE_DIR/bin/node" ]; then
    return
  fi
  log "installing Node ${NODE_MAJOR} into ${NODE_DIR}"
  local base="https://nodejs.org/dist/latest-v${NODE_MAJOR}.x"
  local tarball
  tarball="$(curl -fsSL "$base/SHASUMS256.txt" | grep -o "node-v${NODE_MAJOR}[^ ]*-linux-x64.tar.xz" | head -1)"
  [ -n "$tarball" ] || { log "no Node ${NODE_MAJOR} build found at $base"; exit 1; }
  curl -fsSL "$base/$tarball" -o "/tmp/$tarball"
  (cd /tmp && curl -fsSL "$base/SHASUMS256.txt" | grep " $tarball\$" | sha256sum -c - >/dev/null)
  $SUDO mkdir -p "$NODE_DIR"
  $SUDO tar -xJf "/tmp/$tarball" -C "$NODE_DIR" --strip-components=1
}

# The image puts Node 22 first on PATH; the session's later commands read CLAUDE_ENV_FILE.
use_node_for_session() {
  export PATH="$NODE_DIR/bin:$PATH" DOTNET_ROOT="$DOTNET_DIR"
  if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    {
      echo "export PATH=\"$NODE_DIR/bin:\$PATH\""
      echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    } >> "$CLAUDE_ENV_FILE"
  fi
}

ensure_dockerd() {
  if docker info >/dev/null 2>&1; then
    return
  fi
  log "starting dockerd"
  $SUDO sh -c 'nohup dockerd > /tmp/dockerd.log 2>&1 &'
  for _ in $(seq 1 30); do
    docker info >/dev/null 2>&1 && return
    sleep 1
  done
  log "dockerd did not start within 30 s; see /tmp/dockerd.log"
  exit 1
}

# npm ci only when the lockfile changed since the last install.
ensure_frontend_dependencies() {
  local stamp="$ROOT/frontend/node_modules/.package-lock.cloud-setup"
  if [ -f "$stamp" ] && cmp -s "$ROOT/frontend/package-lock.json" "$stamp"; then
    return
  fi
  log "installing the frontend dependencies with $(node --version)"
  (cd "$ROOT/frontend" && npm ci --no-audit --no-fund >/dev/null)
  cp "$ROOT/frontend/package-lock.json" "$stamp"
}

ensure_dotnet
ensure_node
if $SESSION; then
  use_node_for_session
  ensure_dockerd
  ensure_frontend_dependencies
fi
log "ready: .NET $(dotnet --version), Node $("$NODE_DIR/bin/node" --version)"

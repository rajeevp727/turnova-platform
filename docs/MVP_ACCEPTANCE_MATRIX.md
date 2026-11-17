#!/usr/bin/env bash
set -euo pipefail

app_dir="${1:-$PWD}"
mode="${2:-install}"

case "$mode" in
  install|--verify-only) ;;
  *)
    echo "Usage: prepare-mobile-dependencies.sh [app-directory] [install|--verify-only]" >&2
    exit 2
    ;;
esac

if [[ ! -d "$app_dir" ]]; then
  echo "Mobile app directory does not exist." >&2
  exit 2
fi

app_dir="$(cd "$app_dir" && pwd -P)"

(
  cd "$app_dir"

  if [[ ! -f package.json || ! -f package-lock.sha256 ]]; then
    echo "package.json and package-lock.sha256 are required." >&2
    exit 2
  fi

  # Reject malformed pins before invoking npm at all. Command substitution removes
  # trailing newlines but preserves embedded whitespace/newlines for the strict check.
  expected="$(cat package-lock.sha256)"
  if [[ ! "$expected" =~ ^[0-9a-f]{64}$ ]]; then
    echo "package-lock.sha256 must contain exactly one lowercase SHA-256 digest." >&2
    exit 2
  fi

  if [[ "$mode" == "install" ]]; then
    # Resolve a lockfile without executing package lifecycle scripts. The reviewed
    # checksum below must match before npm is allowed to install or run postinstall.
    npm install --package-lock-only --ignore-scripts --prefer-offline --no-fund --audit=false
  fi

  if [[ ! -f package-lock.json ]]; then
    echo "A generated package-lock.json is required." >&2
    exit 2
  fi

  actual="$(node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex"));' package-lock.json)"
  if [[ "$actual" != "$expected" ]]; then
    echo "Generated package-lock.json does not match the reviewed SHA-256 pin." >&2
    echo "Expected: $expected" >&2
    echo "Actual:   $actual" >&2
    exit 1
  fi

  if [[ "$mode" == "install" ]]; then
    npm ci --prefer-offline --no-fund --audit=false
  fi
)

#!/usr/bin/env bash
set -euo pipefail

app_dir="${1:-$PWD}"
mode="${2:-install}"
case "$mode" in
  install|--verify-only) ;;
  *) echo "Usage: prepare-mobile-dependencies.sh [app-directory] [install|--verify-only]" >&2; exit 2 ;;
esac

if [[ ! -d "$app_dir" ]]; then
  echo "Mobile app directory does not exist." >&2
  exit 2
fi
app_dir="$(cd "$app_dir" && pwd -P)"

(
  cd "$app_dir"

  assert_safe_link_target() {
    local name="$1"
    local candidate="$app_dir/$name"
    if [[ -L "$candidate" ]]; then
      node - "$app_dir" "$candidate" <<'NODE'
const fs = require("node:fs");
const path = require("node:path");

const base = fs.realpathSync(process.argv[2]);
const candidate = process.argv[3];
let target;
try {
  target = fs.realpathSync(candidate);
} catch {
  console.error(`Refusing broken symlink for ${path.basename(candidate)}.`);
  process.exit(2);
}

const relative = path.relative(base, target);
if (relative === ".." || relative.startsWith(".." + path.sep) || path.isAbsolute(relative)) {
  console.error(`Refusing ${path.basename(candidate)} symlink outside the mobile app directory.`);
  process.exit(2);
}
NODE
    fi
  }

  assert_single_linked_lockfile() {
    local candidate="$app_dir/package-lock.json"
    if [[ -e "$candidate" ]]; then
      node - "$candidate" <<'NODE'
const fs = require("node:fs");
const candidate = process.argv[2];
let stats;
try {
  stats = fs.statSync(candidate);
} catch {
  console.error("Unable to inspect the package-lock.json write target.");
  process.exit(2);
}
if (!stats.isFile() || stats.nlink !== 1) {
  console.error("Refusing package-lock.json unless it is a regular file with exactly one hard link.");
  process.exit(2);
}
NODE
    fi
  }

  for file in package.json package-lock.sha256 package-lock.json; do
    assert_safe_link_target "$file"
  done
  # npm's lockfile-only install writes package-lock.json. A hard link can
  # alias a path outside the app even when no symlink is present.
  assert_single_linked_lockfile

  # npm ci removes node_modules before installing. Never allow it to operate
  # through a symlink, even when the target is internal to the app.
  if [[ -L node_modules ]]; then
    echo "node_modules must not be a symlink because npm ci removes that path." >&2
    exit 2
  fi

  if [[ ! -f package.json || ! -f package-lock.sha256 ]]; then
    echo "package.json and package-lock.sha256 are required regular files or safe in-app symlinks." >&2
    exit 2
  fi

  line_count="$(wc -l < package-lock.sha256 | tr -d '[:space:]')"
  if [[ "$line_count" -gt 1 ]]; then
    echo "package-lock.sha256 must contain exactly one digest line." >&2
    exit 2
  fi
  expected="$(cat package-lock.sha256)"
  if [[ ! "$expected" =~ ^[0-9a-f]{64}$ ]]; then
    echo "package-lock.sha256 must contain exactly one lowercase SHA-256 digest." >&2
    exit 2
  fi

  if [[ "$mode" == "install" ]]; then
    # Resolve the lockfile without executing package lifecycle scripts. The
    # committed digest must match before npm is allowed to run postinstall.
    npm install --package-lock-only --ignore-scripts --prefer-offline --no-fund --audit=false
  fi

  assert_safe_link_target package-lock.json
  assert_single_linked_lockfile
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

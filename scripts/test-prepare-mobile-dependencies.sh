#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd -P)"
prepare_script="$repo_root/scripts/prepare-mobile-dependencies.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin" "$tmp/app" "$tmp/outside"

printf '{"name":"lockfile-fixture","lockfileVersion":3}\n' > "$tmp/expected-package-lock.json"
printf '{"name":"lockfile-fixture"}\n' > "$tmp/app/package.json"
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"
node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex") + "\n");' "$tmp/expected-package-lock.json" > "$tmp/app/package-lock.sha256"
: > "$tmp/npm-calls"

cat > "$tmp/bin/npm" <<'FAKE_NPM'
#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
  install)
    [[ "${2:-}" == "--package-lock-only" ]] || exit 91
    has_ignore_scripts=false
    for arg in "$@"; do
      if [[ "$arg" == "--ignore-scripts" ]]; then has_ignore_scripts=true; fi
    done
    [[ "$has_ignore_scripts" == true ]] || { echo "Lockfile generation must disable lifecycle scripts." >&2; exit 93; }
    printf 'install --package-lock-only --ignore-scripts\n' >> "$NPM_CALL_LOG"
    cp "$EXPECTED_LOCKFILE" package-lock.json
    ;;
  ci) printf 'ci\n' >> "$NPM_CALL_LOG" ;;
  *) exit 92 ;;
esac
FAKE_NPM
chmod +x "$tmp/bin/npm"
export EXPECTED_LOCKFILE="$tmp/expected-package-lock.json"
export NPM_CALL_LOG="$tmp/npm-calls"
export PATH="$tmp/bin:$PATH"

# Matching pin: script-disabled generation, checksum validation, then npm ci.
bash "$prepare_script" "$tmp/app" install
grep -qx 'install --package-lock-only --ignore-scripts' "$NPM_CALL_LOG"
grep -qx 'ci' "$NPM_CALL_LOG"

# Valid but mismatched pin: generation is allowed, npm ci must not run.
: > "$NPM_CALL_LOG"
printf '%064d\n' 0 > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then echo "Expected mismatched pin to fail." >&2; exit 1; fi
grep -qx 'install --package-lock-only --ignore-scripts' "$NPM_CALL_LOG"
if grep -qx 'ci' "$NPM_CALL_LOG"; then echo "npm ci ran despite mismatch." >&2; exit 1; fi

# Malformed and multi-line pins are rejected before npm.
: > "$NPM_CALL_LOG"
printf 'not-a-sha256\n' > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then echo "Expected malformed digest to fail." >&2; exit 1; fi
[[ ! -s "$NPM_CALL_LOG" ]]
printf '%064d\n\n' 0 > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then echo "Expected multi-line digest to fail." >&2; exit 1; fi
[[ ! -s "$NPM_CALL_LOG" ]]

# Verify-only does not invoke npm.
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"
node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex") + "\n");' "$tmp/expected-package-lock.json" > "$tmp/app/package-lock.sha256"
: > "$NPM_CALL_LOG"
bash "$prepare_script" "$tmp/app" --verify-only
[[ ! -s "$NPM_CALL_LOG" ]]

# Safe in-app symlink targets are preserved and accepted.
mv "$tmp/app/package.json" "$tmp/app/manifest.json"
ln -s manifest.json "$tmp/app/package.json"
mv "$tmp/app/package-lock.sha256" "$tmp/app/pin.sha256"
ln -s pin.sha256 "$tmp/app/package-lock.sha256"
mv "$tmp/app/package-lock.json" "$tmp/app/lock-target.json"
ln -s lock-target.json "$tmp/app/package-lock.json"
bash "$prepare_script" "$tmp/app" --verify-only
rm "$tmp/app/package.json" "$tmp/app/package-lock.sha256" "$tmp/app/package-lock.json"
mv "$tmp/app/manifest.json" "$tmp/app/package.json"
mv "$tmp/app/pin.sha256" "$tmp/app/package-lock.sha256"
mv "$tmp/app/lock-target.json" "$tmp/app/package-lock.json"

assert_rejected_before_npm() {
  local description="$1"
  : > "$NPM_CALL_LOG"
  if bash "$prepare_script" "$tmp/app" install; then
    echo "Expected $description to be rejected." >&2
    exit 1
  fi
  [[ ! -s "$NPM_CALL_LOG" ]] || {
    echo "npm was invoked before rejecting $description." >&2
    exit 1
  }
}

# External and broken package-lock symlinks are rejected before npm can write.
printf 'external-lock-must-remain-unchanged\n' > "$tmp/outside/lock.json"
cp "$tmp/outside/lock.json" "$tmp/outside/lock.original"
rm "$tmp/app/package-lock.json"
ln -s "$tmp/outside/lock.json" "$tmp/app/package-lock.json"
assert_rejected_before_npm "external package-lock symlink"
cmp "$tmp/outside/lock.original" "$tmp/outside/lock.json"
rm "$tmp/app/package-lock.json"
ln -s "$tmp/outside/missing-lock.json" "$tmp/app/package-lock.json"
assert_rejected_before_npm "broken package-lock symlink"
rm "$tmp/app/package-lock.json"
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"

# A hardlinked package-lock can alias a file outside the app without a symlink.
cp "$tmp/app/package-lock.json" "$tmp/outside/lock-hardlink.json"
rm "$tmp/app/package-lock.json"
ln "$tmp/outside/lock-hardlink.json" "$tmp/app/package-lock.json"
cp "$tmp/outside/lock-hardlink.json" "$tmp/outside/lock-hardlink.original"
assert_rejected_before_npm "hardlinked package-lock"
cmp "$tmp/outside/lock-hardlink.original" "$tmp/outside/lock-hardlink.json"
rm "$tmp/app/package-lock.json"
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"

# External package.json and digest symlinks are rejected before npm reads them.
mv "$tmp/app/package.json" "$tmp/app/manifest.original"
cp "$tmp/app/manifest.original" "$tmp/outside/manifest.json"
ln -s "$tmp/outside/manifest.json" "$tmp/app/package.json"
assert_rejected_before_npm "external package.json symlink"
rm "$tmp/app/package.json"
mv "$tmp/app/manifest.original" "$tmp/app/package.json"

mv "$tmp/app/package-lock.sha256" "$tmp/app/pin.original"
cp "$tmp/app/pin.original" "$tmp/outside/pin.sha256"
ln -s "$tmp/outside/pin.sha256" "$tmp/app/package-lock.sha256"
assert_rejected_before_npm "external package-lock.sha256 symlink"
rm "$tmp/app/package-lock.sha256"
mv "$tmp/app/pin.original" "$tmp/app/package-lock.sha256"

# npm ci removes node_modules, so symlinked node_modules is always rejected.
mkdir -p "$tmp/outside/node_modules"
printf 'preserve-me\n' > "$tmp/outside/node_modules/marker"
ln -s "$tmp/outside/node_modules" "$tmp/app/node_modules"
assert_rejected_before_npm "node_modules symlink"
grep -qx 'preserve-me' "$tmp/outside/node_modules/marker"
rm "$tmp/app/node_modules"

echo "Mobile dependency bootstrap tests passed (pin match/mismatch, malformed and multi-line pins, verify-only, safe internal symlinks, external/broken symlink rejection, and node_modules protection)."

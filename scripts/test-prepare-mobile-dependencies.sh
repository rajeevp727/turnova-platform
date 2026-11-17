#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd -P)"
prepare_script="$repo_root/scripts/prepare-mobile-dependencies.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin" "$tmp/app"
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
# Valid but mismatched pin: npm ci must not run.
: > "$NPM_CALL_LOG"
printf '%064d\n' 0 > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then echo "Expected mismatched pin to fail." >&2; exit 1; fi
grep -qx 'install --package-lock-only --ignore-scripts' "$NPM_CALL_LOG"
if grep -qx 'ci' "$NPM_CALL_LOG"; then echo "npm ci ran despite mismatch." >&2; exit 1; fi
# Malformed digest: no npm command at all.
: > "$NPM_CALL_LOG"
printf 'not-a-sha256\n' > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then echo "Expected malformed digest to fail." >&2; exit 1; fi
[[ ! -s "$NPM_CALL_LOG" ]]
# Verify-only: no npm command.
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"
node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex") + "\n");' "$tmp/expected-package-lock.json" > "$tmp/app/package-lock.sha256"
: > "$NPM_CALL_LOG"
bash "$prepare_script" "$tmp/app" --verify-only
[[ ! -s "$NPM_CALL_LOG" ]]
echo "Mobile dependency bootstrap tests passed (matching pin, mismatch fail-closed, malformed digest rejected before npm, verify-only, and --ignore-scripts asserted)."

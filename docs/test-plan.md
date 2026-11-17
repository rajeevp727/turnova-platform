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
      if [[ "$arg" == "--ignore-scripts" ]]; then
        has_ignore_scripts=true
      fi
    done
    [[ "$has_ignore_scripts" == true ]] || {
      echo "Lockfile generation must disable lifecycle scripts." >&2
      exit 93
    }
    printf 'install --package-lock-only --ignore-scripts\n' >> "$NPM_CALL_LOG"
    cp "$EXPECTED_LOCKFILE" package-lock.json
    ;;
  ci)
    printf 'ci\n' >> "$NPM_CALL_LOG"
    ;;
  *)
    exit 92
    ;;
esac
FAKE_NPM
chmod +x "$tmp/bin/npm"

export EXPECTED_LOCKFILE="$tmp/expected-package-lock.json"
export NPM_CALL_LOG="$tmp/npm-calls"
export PATH="$tmp/bin:$PATH"

# A matching generated lockfile must be produced with lifecycle scripts disabled,
# pass its reviewed digest, and only then reach npm ci.
bash "$prepare_script" "$tmp/app" install
grep -qx 'install --package-lock-only --ignore-scripts' "$NPM_CALL_LOG"
grep -qx 'ci' "$NPM_CALL_LOG"

# A syntactically valid but mismatched pin may resolve safely, but must never reach npm ci.
: > "$NPM_CALL_LOG"
printf '%064d\n' 0 > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then
  echo "Expected a mismatched lockfile pin to fail." >&2
  exit 1
fi
grep -qx 'install --package-lock-only --ignore-scripts' "$NPM_CALL_LOG"
if grep -qx 'ci' "$NPM_CALL_LOG"; then
  echo "npm ci ran despite a mismatched lockfile pin." >&2
  exit 1
fi

# A malformed digest must fail before invoking npm, including lockfile generation.
: > "$NPM_CALL_LOG"
printf 'not-a-sha256\n' > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then
  echo "Expected a malformed digest to fail." >&2
  exit 1
fi
if [[ -s "$NPM_CALL_LOG" ]]; then
  echo "npm was invoked despite a malformed digest." >&2
  exit 1
fi

# Verify-only mode must validate an existing lockfile without invoking npm.
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"
node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex") + "\n");' "$tmp/expected-package-lock.json" > "$tmp/app/package-lock.sha256"
: > "$NPM_CALL_LOG"
bash "$prepare_script" "$tmp/app" --verify-only
[[ ! -s "$NPM_CALL_LOG" ]]

echo "Mobile dependency bootstrap tests passed (matching pin, mismatch fail-closed, malformed digest rejected before npm, verify-only, and --ignore-scripts asserted)."

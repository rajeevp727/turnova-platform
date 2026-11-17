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

# A matching generated lockfile must pass verification and reach npm ci.
bash "$prepare_script" "$tmp/app" install
grep -qx 'ci' "$NPM_CALL_LOG"

# A mismatched pin must fail closed before npm ci.
: > "$NPM_CALL_LOG"
printf '%064d\n' 0 > "$tmp/app/package-lock.sha256"
if bash "$prepare_script" "$tmp/app" install; then
  echo "Expected a mismatched lockfile pin to fail." >&2
  exit 1
fi
if grep -q '^ci$' "$NPM_CALL_LOG"; then
  echo "npm ci ran despite a mismatched lockfile pin." >&2
  exit 1
fi

# Verify-only mode must validate an existing lockfile without invoking npm.
cp "$tmp/expected-package-lock.json" "$tmp/app/package-lock.json"
node -e 'const fs = require("node:fs"); const { createHash } = require("node:crypto"); process.stdout.write(createHash("sha256").update(fs.readFileSync(process.argv[1])).digest("hex") + "\n");' "$tmp/expected-package-lock.json" > "$tmp/app/package-lock.sha256"
: > "$NPM_CALL_LOG"
bash "$prepare_script" "$tmp/app" --verify-only
[[ ! -s "$NPM_CALL_LOG" ]]

echo "Mobile dependency bootstrap tests passed (matching pin, mismatch fail-closed, verify-only)."

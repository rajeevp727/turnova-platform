#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
detector="$root/scripts/detect-mobile-ci-scope.sh"
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

assert_scope() {
  local expected="$1"
  local description="$2"
  local actual
  actual="$(bash "$detector" "$tmp")"
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected mobile scope $expected for $description, received $actual." >&2
    exit 1
  fi
}

cat > "$tmp" <<'EOF'
src/backend/TurnOva/TurnOva.API/Program.cs
src/backend/TurnOva/TurnOva.Tests/AuthApiIntegrationTests.cs
docs/api/README.md
docs/MVP_PROGRESS.md
.github/workflows/ci.yml
scripts/verify-api-publish.sh
EOF
assert_scope false "API, docs and workflow-only changes"

printf '%s\n' 'src/mobile/rider-app/App.tsx' > "$tmp"
assert_scope true "Rider mobile source"

printf '%s\n' 'src/mobile/driver-app/package-lock.sha256' > "$tmp"
assert_scope true "Driver dependency pin"

printf '%s\n' 'scripts/prepare-mobile-dependencies.sh' > "$tmp"
assert_scope true "mobile dependency bootstrap"

printf '%s\n' 'scripts/verify_native_app_identity.py' > "$tmp"
assert_scope true "native artifact identity verifier"

printf '%s\n' 'scripts/ios-simulator-smoke.sh' > "$tmp"
assert_scope true "iOS simulator smoke tooling"

echo "Mobile CI phase-scope fixtures passed."


workflow="$root/.github/workflows/ci.yml"
grep -Fq 'turnova:android-phase-approved' "$workflow"
grep -Fq 'turnova:ios-phase-approved' "$workflow"
grep -Fq 'android_phase: ${{ steps.detect.outputs.android_phase }}' "$workflow"
android_gate_count="$(grep -Fc "needs.scope.outputs.android_phase == 'true'" "$workflow")"
ios_gate_count="$(grep -Fc "needs.scope.outputs.ios_phase == 'true'" "$workflow")"
if [[ "$android_gate_count" -lt 4 || "$ios_gate_count" -lt 2 ]]; then
  echo "Native platform jobs are not fully gated by API -> Android -> iOS approval labels." >&2
  exit 1
fi
grep -Fq 'if [[ "$GITHUB_EVENT_NAME" == "push" ]]; then' "$workflow"
echo "API -> Android -> iOS native phase gates passed."

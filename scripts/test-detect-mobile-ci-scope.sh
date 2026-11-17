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
grep -Fq 'turnova:api-phase-complete' "$workflow"
grep -Fq 'turnova:android-phase-approved' "$workflow"
grep -Fq 'turnova:android-phase-complete' "$workflow"
grep -Fq 'any(.pull_request.labels[]?; .name == "turnova:api-phase-complete") and any(.pull_request.labels[]?; .name == "turnova:android-phase-approved" or .name == "turnova:android-phase-complete")' "$workflow"
grep -Fq 'any(.pull_request.labels[]?; .name == "turnova:api-phase-complete") and any(.pull_request.labels[]?; .name == "turnova:android-phase-complete") and any(.pull_request.labels[]?; .name == "turnova:ios-phase-approved")' "$workflow"
grep -Fq 'Mobile code changes require turnova:api-phase-complete' "$workflow"
grep -Fq 'turnova:ios-phase-approved' "$workflow"
grep -Fq 'android_phase: ${{ steps.detect.outputs.android_phase }}' "$workflow"
android_gate_count="$(grep -Fc "needs.scope.outputs.android_phase == 'true'" "$workflow")"
ios_gate_count="$(grep -Fc "needs.scope.outputs.ios_phase == 'true'" "$workflow")"
if [[ "$android_gate_count" -lt 6 || "$ios_gate_count" -lt 3 ]]; then
  echo "Android validation/native jobs and iOS validation/native jobs are not fully gated by API -> Android complete -> iOS approval labels." >&2
  exit 1
fi
grep -Fq 'if [[ "$GITHUB_EVENT_NAME" == "push" ]]; then' "$workflow"
android_job="$(sed -n '/^  mobile:$/,/^  mobile-ios:$/p' "$workflow")"
if grep -Fq 'Export iOS production JavaScript bundle' <<< "$android_job"; then
  echo "Android validation job must not export an iOS bundle." >&2
  exit 1
fi
ios_job="$(sed -n '/^  mobile-ios:$/,/^  native-android:$/p' "$workflow")"
grep -Fq 'Export iOS production JavaScript bundle' <<< "$ios_job"
grep -Fq "needs.mobile-ios.result == 'success'" "$workflow"
grep -Fq "needs.mobile-auth-tests.result == 'success'" "$workflow"
grep -Fq "needs: [scope, mobile, mobile-auth-tests, native-android, android-emulator-smoke]" "$workflow"
grep -Fq "needs.android-emulator-smoke.result == 'success'" "$workflow"
echo "API -> Android -> Android-complete -> iOS validation/native phase gates passed."

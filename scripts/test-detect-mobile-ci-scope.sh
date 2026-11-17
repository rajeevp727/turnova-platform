#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
detector="$root/scripts/detect-mobile-ci-scope.sh"
tmp="$(mktemp)"
phase_tmp="$(mktemp)"
event_tmp="$(mktemp)"
trap 'rm -f "$tmp" "$phase_tmp" "$event_tmp"' EXIT

assert_ios_scope() {
  local expected="$1"
  local description="$2"
  local actual
  actual="$(bash "$root/scripts/detect-ios-ci-scope.sh" "$tmp")"
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected iOS-specific scope $expected for $description, received $actual." >&2
    exit 1
  fi
}

assert_phase() {
  local expected="$1"
  local description="$2"
  local event_name="$3"
  local actual
  actual="$(bash "$root/scripts/detect-turnova-platform-phase.sh" "$event_name" "$event_tmp" "$phase_tmp")"
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected phase result [$expected] for $description, received [$actual]." >&2
    exit 1
  fi
}

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
assert_ios_scope true "iOS simulator smoke tooling"
printf '%s\n' 'src/mobile/rider-app/App.tsx' > "$tmp"
assert_ios_scope false "shared Rider app source"
printf '%s\n' 'src/mobile/driver-app/ios/TurnOva.xcodeproj/project.pbxproj' > "$tmp"
assert_ios_scope true "iOS native project"

echo "Mobile CI file-scope fixtures passed."

phase_detector="$root/scripts/detect-turnova-platform-phase.sh"
printf '%s\n' '{"api":"in_progress","android":"not_started","ios":"not_started"}' > "$phase_tmp"
printf '%s\n' '{}' > "$event_tmp"
assert_phase "android_phase=false
ios_phase=false" "push while API phase is incomplete" "push"

printf '%s\n' '{"api":"complete","android":"in_progress","ios":"not_started"}' > "$phase_tmp"
assert_phase "android_phase=true
ios_phase=false" "push after API exit during Android phase" "push"

printf '%s\n' '{"api":"complete","android":"complete","ios":"in_progress"}' > "$phase_tmp"
assert_phase "android_phase=true
ios_phase=true" "push during iOS phase after Android exit" "push"

printf '%s\n' '{"api":"in_progress","android":"in_progress","ios":"not_started"}' > "$phase_tmp"
printf '%s\n' '{"pull_request":{"labels":[{"name":"turnova:api-phase-complete"},{"name":"turnova:android-phase-approved"},{"name":"turnova:android-phase-complete"},{"name":"turnova:ios-phase-approved"}]}}' > "$event_tmp"
assert_phase "android_phase=false
ios_phase=false" "labels cannot bypass incomplete API phase" "pull_request"

printf '%s\n' '{"api":"complete","android":"in_progress","ios":"not_started"}' > "$phase_tmp"
printf '%s\n' '{"pull_request":{"labels":[{"name":"turnova:api-phase-complete"},{"name":"turnova:android-phase-approved"}]}}' > "$event_tmp"
assert_phase "android_phase=true
ios_phase=false" "approved Android phase" "pull_request"

printf '%s\n' '{"api":"complete","android":"complete","ios":"in_progress"}' > "$phase_tmp"
printf '%s\n' '{"pull_request":{"labels":[{"name":"turnova:api-phase-complete"},{"name":"turnova:android-phase-complete"},{"name":"turnova:ios-phase-approved"}]}}' > "$event_tmp"
assert_phase "android_phase=true
ios_phase=true" "approved iOS phase after Android completion" "pull_request"

workflow="$root/.github/workflows/ci.yml"
grep -Fq 'scripts/detect-turnova-platform-phase.sh' "$workflow"
grep -Fq 'scripts/detect-ios-ci-scope.sh' "$workflow"
grep -Fq 'ios_changes: ${{ steps.detect.outputs.ios_changes }}' "$workflow"
grep -Fq 'Mobile code changes require the checked-in API phase state' "$workflow"
grep -Fq 'iOS-specific code changes require API complete, Android complete' "$workflow"
android_gate_count="$(grep -Fc "needs.scope.outputs.android_phase == 'true'" "$workflow")"
ios_gate_count="$(grep -Fc "needs.scope.outputs.ios_phase == 'true'" "$workflow")"
if [[ "$android_gate_count" -lt 6 || "$ios_gate_count" -lt 3 ]]; then
  echo "Android/iOS jobs are not gated by the phase-state detector." >&2
  exit 1
fi
android_job="$(sed -n '/^  mobile:$/,/^  mobile-ios:$/p' "$workflow")"
if grep -Fq 'Export iOS production JavaScript bundle' <<< "$android_job"; then
  echo "Android validation job must not export an iOS bundle." >&2
  exit 1
fi
ios_job="$(sed -n '/^  mobile-ios:$/,/^  native-android:$/p' "$workflow")"
grep -Fq 'Export iOS production JavaScript bundle' <<< "$ios_job"
grep -Fq "needs: [scope, mobile, mobile-auth-tests, native-android, android-emulator-smoke]" "$workflow"
grep -Fq "needs.android-emulator-smoke.result == 'success'" "$workflow"
echo "API -> Android -> Android-complete -> iOS phase gates passed."

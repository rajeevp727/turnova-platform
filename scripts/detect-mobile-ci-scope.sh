#!/usr/bin/env bash
set -euo pipefail

changed_files="${1:?Usage: detect-mobile-ci-scope.sh <changed-files.txt>}"
if [[ ! -f "$changed_files" ]]; then
  echo "Changed-files input does not exist: $changed_files" >&2
  exit 2
fi

# API-only changes must not start Android/iOS completion work. Mobile source and
# mobile-specific build/release tooling opt into the mobile matrix explicitly.
if grep -Eq '^(src/mobile/|scripts/(prepare-mobile-dependencies\.sh|test-prepare-mobile-dependencies\.sh|release-preflight\.cjs|generate-icons\.mjs|verify_native_app_identity\.py|android-emulator-smoke\.sh|ios-simulator-smoke\.sh|tests/test_verify_native_app_identity\.py|tests/test_android_emulator_smoke\.py|tests/test_ios_simulator_smoke\.py))' "$changed_files"; then
  printf 'true\n'
else
  printf 'false\n'
fi

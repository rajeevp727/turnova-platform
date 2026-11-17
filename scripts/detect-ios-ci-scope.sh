#!/usr/bin/env bash
set -euo pipefail

file_list="${1:?changed-file list is required}"
if [[ ! -f "$file_list" ]]; then
  echo "Changed-file list not found: $file_list" >&2
  exit 2
fi

if grep -Eq '^(src/mobile/(rider-app|driver-app)/ios/|src/mobile/shared/ios/|scripts/ios-|scripts/verify-ios)' "$file_list"; then
  echo true
else
  echo false
fi

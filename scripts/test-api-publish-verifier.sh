#!/usr/bin/env bash
set -euo pipefail

validator="${1:-scripts/verify-api-publish.sh}"
if [[ ! -f "$validator" ]]; then
  echo "Verifier script not found: $validator" >&2
  exit 1
fi

fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/runtimes/linux-x64/native"
printf 'api' > "$fixture/TurnOva.API.dll"
printf 'skia' > "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"
printf 'harfbuzz' > "$fixture/runtimes/linux-x64/native/libHarfBuzzSharp.so"

expect_failure() {
  local label="$1"
  if bash "$validator" "$fixture" >/dev/null 2>&1; then
    echo "Expected publish verifier to reject fixture: $label" >&2
    exit 1
  fi
}

bash -n "$validator"
bash "$validator" "$fixture" >/dev/null

rm "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"
expect_failure "missing Skia native library"
printf 'skia' > "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"

mkdir -p "$fixture/runtimes/osx-x64"
expect_failure "non-Linux runtime directory"
rmdir "$fixture/runtimes/osx-x64"

touch "$fixture/PdfSharp.WPFonts.dll"
expect_failure "optional Segoe font assembly"
rm "$fixture/PdfSharp.WPFonts.dll"

truncate -s $((120 * 1024 * 1024 + 1)) "$fixture/oversize.bin"
expect_failure "publish size above 120 MiB"
rm "$fixture/oversize.bin"

rm "$fixture/TurnOva.API.dll"
expect_failure "missing API assembly"

echo "Native publish verifier fixture tests passed."

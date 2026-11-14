#!/usr/bin/env bash
set -euo pipefail

validator="\${1:-scripts/verify-api-publish.sh}"
if [[ ! -f "$validator" ]]; then
  echo "Verifier script not found: $validator" >&2
  exit 1
fi

fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

write_valid_fixture() {
  rm -rf "$fixture"
  mkdir -p "$fixture/runtimes/linux-x64/native"
  printf 'api' > "$fixture/TurnOva.API.dll"
  printf '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0"}}' > "$fixture/TurnOva.API.deps.json"
  printf '{"runtimeOptions":{"tfm":"net10.0"}}' > "$fixture/TurnOva.API.runtimeconfig.json"
  printf 'skia' > "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"
  printf 'harfbuzz' > "$fixture/runtimes/linux-x64/native/libHarfBuzzSharp.so"
}

expect_failure() {
  local label="$1"
  local expected_message="$2"
  local output

  if output="$(bash "$validator" "$fixture" 2>&1)"; then
    echo "Expected publish verifier to reject fixture: $label" >&2
    exit 1
  fi

  if ! grep -Fq -- "$expected_message" <<<"$output"; then
    printf 'Verifier rejected fixture for an unexpected reason: %s\n' "$label" >&2
    printf 'Expected message: %s\nActual output:\n%s\n' "$expected_message" "$output" >&2
    exit 1
  fi
}

bash -n "$validator"
bash -n "$0"
write_valid_fixture
bash "$validator" "$fixture" >/dev/null

rm "$fixture/TurnOva.API.deps.json"
expect_failure "missing dependency metadata" "TurnOva.API.deps.json is missing or empty"
write_valid_fixture

: > "$fixture/TurnOva.API.runtimeconfig.json"
expect_failure "empty runtime configuration" "TurnOva.API.runtimeconfig.json is missing or empty"
write_valid_fixture

: > "$fixture/TurnOva.API.dll"
expect_failure "empty API assembly" "TurnOva.API.dll is missing or empty"
write_valid_fixture

rm "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"
expect_failure "missing Skia native library" "Required Linux x64 native rendering asset is missing: libSkiaSharp.so"
write_valid_fixture

: > "$fixture/runtimes/linux-x64/native/libSkiaSharp.so"
expect_failure "empty Skia native library" "Required Linux x64 native rendering asset is empty: libSkiaSharp.so"
write_valid_fixture

rm "$fixture/runtimes/linux-x64/native/libHarfBuzzSharp.so"
expect_failure "missing HarfBuzz native library" "Required Linux x64 native rendering asset is missing: libHarfBuzzSharp.so"
write_valid_fixture

: > "$fixture/runtimes/linux-x64/native/libHarfBuzzSharp.so"
expect_failure "empty HarfBuzz native library" "Required Linux x64 native rendering asset is empty: libHarfBuzzSharp.so"
write_valid_fixture

mkdir -p "$fixture/runtimes/linux-x64/lib/net10.0"
touch "$fixture/runtimes/linux-x64/lib/net10.0/PdfSharp.WPFonts.dll"
expect_failure "nested optional Segoe font assembly" "PdfSharp.WPFonts.dll contains optional Segoe WP font resources and must not be redistributed"
write_valid_fixture

mkdir -p "$fixture/runtimes/osx-x64"
expect_failure "non-Linux runtime directory" "Unexpected non-Linux-x64 runtime assets in publish output: osx-x64"
write_valid_fixture

truncate -s $((120 * 1024 * 1024 + 1)) "$fixture/oversize.bin"
expect_failure "publish size above 120 MiB" "Native Linux-x64 publish output exceeded 120 MiB"
write_valid_fixture

rm "$fixture/TurnOva.API.dll"
expect_failure "missing API assembly" "TurnOva.API.dll is missing or empty"

echo "Native publish verifier fixture tests passed (11 negative cases plus valid fixture)."

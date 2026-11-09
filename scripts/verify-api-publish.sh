#!/usr/bin/env bash
set -euo pipefail
publish_dir="${1:?Usage: verify-api-publish.sh <publish-directory>}"
if [[ ! -d "$publish_dir" ]]; then echo "Publish directory does not exist: $publish_dir" >&2; exit 1; fi
if [[ ! -s "$publish_dir/TurnOva.API.dll" ]]; then echo "TurnOva.API.dll is missing or empty from the publish output." >&2; exit 1; fi
native_files=()
while IFS= read -r -d '' path; do native_files+=("$path"); done < <(find "$publish_dir" -type f \( -name libSkiaSharp.so -o -name libHarfBuzzSharp.so \) -print0)
printf 'Linux native rendering assets found:\n'
for path in "${native_files[@]}"; do printf '  %s\n' "$path"; done
has_skia=false
has_harfbuzz=false
for path in "${native_files[@]}"; do
  case "$(basename "$path")" in
    libSkiaSharp.so) has_skia=true ;;
    libHarfBuzzSharp.so) has_harfbuzz=true ;;
  esac
done
if [[ "$has_skia" != true || "$has_harfbuzz" != true ]]; then echo "Both libSkiaSharp.so and libHarfBuzzSharp.so are required in the Linux x64 publish output." >&2; exit 1; fi
if [[ -n "$(find "$publish_dir" -maxdepth 1 -iname 'PdfSharp.WPFonts.dll' -print -quit)" ]]; then echo "PdfSharp.WPFonts.dll contains optional Segoe WP font resources and must not be redistributed." >&2; exit 1; fi
if [[ -d "$publish_dir/runtimes" ]]; then
  while IFS= read -r -d '' runtime_dir; do
    runtime_name="$(basename "$runtime_dir")"
    case "$runtime_name" in
      linux-x64|unix) ;;
      *) echo "Unexpected non-Linux-x64 runtime assets in publish output: $runtime_name" >&2; exit 1 ;;
    esac
  done < <(find "$publish_dir/runtimes" -mindepth 1 -maxdepth 1 -type d -print0)
fi
publish_bytes="$(du -sb "$publish_dir" | awk '{print $1}')"
max_bytes="$((120 * 1024 * 1024))"
echo "Linux-x64 framework-dependent publish size: $publish_bytes bytes (limit: $max_bytes)."
if (( publish_bytes > max_bytes )); then echo "Native Linux-x64 publish output exceeded 120 MiB." >&2; exit 1; fi
echo "Native API publish artifact validation passed."

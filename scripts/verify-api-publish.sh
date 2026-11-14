#!/usr/bin/env bash
set -euo pipefail

publish_dir="\${1:?Usage: verify-api-publish.sh <publish-directory>}"
if [[ ! -d "$publish_dir" ]]; then
  echo "Publish directory does not exist: $publish_dir" >&2
  exit 1
fi

required_files=(
  "TurnOva.API.dll"
  "TurnOva.API.deps.json"
  "TurnOva.API.runtimeconfig.json"
)
for relative_path in "\${required_files[@]}"; do
  if [[ ! -s "$publish_dir/$relative_path" ]]; then
    echo "$relative_path is missing or empty from the publish output." >&2
    exit 1
  fi
done

required_native_assets=(
  "libSkiaSharp.so"
  "libHarfBuzzSharp.so"
)
for asset_name in "\${required_native_assets[@]}"; do
  asset_path="$(find "$publish_dir" -type f -name "$asset_name" -print -quit)"
  if [[ -z "$asset_path" ]]; then
    echo "Required Linux x64 native rendering asset is missing: $asset_name" >&2
    exit 1
  fi
  if [[ ! -s "$asset_path" ]]; then
    echo "Required Linux x64 native rendering asset is empty: $asset_name" >&2
    exit 1
  fi
  printf 'Verified Linux native rendering asset: %s\n' "\${asset_path#"$publish_dir"/}"
done

forbidden_font="$(find "$publish_dir" -type f -iname 'PdfSharp.WPFonts.dll' -print -quit)"
if [[ -n "$forbidden_font" ]]; then
  echo "PdfSharp.WPFonts.dll contains optional Segoe WP font resources and must not be redistributed: \${forbidden_font#"$publish_dir"/}" >&2
  exit 1
fi

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
if (( publish_bytes > max_bytes )); then
  echo "Native Linux-x64 publish output exceeded 120 MiB." >&2
  exit 1
fi

echo "Native API publish artifact validation passed."

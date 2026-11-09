#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --app <path-to-app-bundle> --expected-id <reverse-dns-bundle-id> [--output-dir <path>]" >&2
}

APP_BUNDLE=""
EXPECTED_ID=""
OUTPUT_DIR="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/turnova-ios-simulator-smoke"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) [[ $# -ge 2 ]] || { usage; exit 2; }; APP_BUNDLE="$2"; shift 2 ;;
    --expected-id) [[ $# -ge 2 ]] || { usage; exit 2; }; EXPECTED_ID="$2"; shift 2 ;;
    --output-dir) [[ $# -ge 2 ]] || { usage; exit 2; }; OUTPUT_DIR="$2"; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

if [[ -z "$APP_BUNDLE" || -z "$EXPECTED_ID" ]]; then
  usage
  exit 2
fi
if [[ ! "$EXPECTED_ID" =~ ^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$ ]]; then
  echo "Expected iOS bundle identifier is malformed." >&2
  exit 2
fi
if [[ ! -d "$APP_BUNDLE" || ! -s "$APP_BUNDLE/Info.plist" ]]; then
  echo "Unsigned iOS Simulator .app bundle or Info.plist is missing." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
actual_id="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP_BUNDLE/Info.plist" 2>/dev/null || true)"
if [[ "$actual_id" != "$EXPECTED_ID" ]]; then
  echo "iOS Simulator app identity mismatch: expected $EXPECTED_ID, found $actual_id." >&2
  exit 1
fi

devices_json="$(xcrun simctl list devices available -j)"
selection="$(python3 -c '
import json
import sys

data = json.load(sys.stdin)
for runtime in sorted(data.get("devices", {}), reverse=True):
    for device in data["devices"][runtime]:
        if device.get("isAvailable") and device.get("name", "").startswith("iPhone"):
            print(device["udid"] + "\t" + device.get("state", "Shutdown"))
            raise SystemExit(0)
raise SystemExit("No available iPhone Simulator device was found.")
' <<< "$devices_json")"
IFS=$'\t' read -r DEVICE_UDID DEVICE_STATE <<< "$selection"
if [[ -z "$DEVICE_UDID" ]]; then
  echo "Unable to select an available iPhone Simulator device." >&2
  exit 1
fi

cleanup() {
  xcrun simctl shutdown "$DEVICE_UDID" >/dev/null 2>&1 || true
}
trap cleanup EXIT

if [[ "$DEVICE_STATE" != "Booted" ]]; then
  xcrun simctl boot "$DEVICE_UDID"
fi
xcrun simctl bootstatus "$DEVICE_UDID" -b

xcrun simctl install "$DEVICE_UDID" "$APP_BUNDLE"
installed_container="$(xcrun simctl get_app_container "$DEVICE_UDID" "$EXPECTED_ID" app)"
if [[ -z "$installed_container" || ! -d "$installed_container" ]]; then
  echo "Expected iOS Simulator app was not installed: $EXPECTED_ID" >&2
  exit 1
fi

launch_output="$(xcrun simctl launch "$DEVICE_UDID" "$EXPECTED_ID" 2>&1)"
printf '%s\n' "$launch_output" | tee "$OUTPUT_DIR/launch.txt"
pid="$(printf '%s\n' "$launch_output" | sed -nE 's/.*: ([0-9]+)$/\1/p' | tail -n 1)"
if [[ -z "$pid" ]]; then
  echo "simctl did not return a running process ID for $EXPECTED_ID." >&2
  exit 1
fi

sleep 8
if xcrun simctl spawn "$DEVICE_UDID" launchctl list > "$OUTPUT_DIR/launchctl-list.txt" 2>&1 &&
   grep -Fq "$EXPECTED_ID" "$OUTPUT_DIR/launchctl-list.txt"; then
  process_alive=true
elif xcrun simctl spawn "$DEVICE_UDID" launchctl procinfo "$pid" > "$OUTPUT_DIR/process-info.txt" 2>&1; then
  process_alive=true
else
  process_alive=false
fi
if [[ "$process_alive" != true ]]; then
  echo "iOS Simulator app process did not remain available after launch." >&2
  exit 1
fi

xcrun simctl io "$DEVICE_UDID" screenshot "$OUTPUT_DIR/launch.png"
if [[ ! -s "$OUTPUT_DIR/launch.png" ]]; then
  echo "iOS Simulator screenshot was not produced." >&2
  exit 1
fi
swift "$(dirname "$0")/ios-simulator-screen-text-check.swift" "$OUTPUT_DIR/launch.png" | tee "$OUTPUT_DIR/ui-text.txt"

python3 - "$EXPECTED_ID" "$DEVICE_UDID" "$pid" "$OUTPUT_DIR/result.json" <<'PY'
import json
import sys

bundle_id, device_udid, pid, output = sys.argv[1:]
with open(output, "w", encoding="utf-8") as stream:
    json.dump({
        "platform": "ios-simulator",
        "bundleId": bundle_id,
        "deviceUdid": device_udid,
        "processId": pid,
        "install": "passed",
        "launch": "passed",
        "processStableAfterStartup": True,
        "loginScreenTextVerified": True,
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised",
        "signing": "unsigned simulator build"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "iOS Simulator install/launch smoke passed for $EXPECTED_ID (PID $pid)."

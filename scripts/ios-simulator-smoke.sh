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
  echo "iOS Simulator .app bundle or Info.plist is missing." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
PLIST_BUDDY="${PLIST_BUDDY:-/usr/libexec/PlistBuddy}"
actual_id="$("$PLIST_BUDDY" -c 'Print :CFBundleIdentifier' "$APP_BUNDLE/Info.plist" 2>/dev/null || true)"
if [[ "$actual_id" != "$EXPECTED_ID" ]]; then
  echo "iOS Simulator app identity mismatch: expected $EXPECTED_ID, found $actual_id." >&2
  exit 1
fi

# SecureStore uses the simulator Keychain. The smoke must run an ad-hoc signed
# simulator copy with a test-only keychain group, not the unsigned compile artifact.
if ! codesign --verify --deep --strict "$APP_BUNDLE" >/dev/null 2>&1; then
  echo "iOS Simulator smoke app is not validly code-signed." >&2
  exit 1
fi
signature_info="$(codesign -dv --verbose=4 "$APP_BUNDLE" 2>&1 || true)"
if ! grep -Fq "Signature=adhoc" <<< "$signature_info"; then
  echo "iOS Simulator smoke requires an ad-hoc signature for SecureStore Keychain access." >&2
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
            print(device["udid"])
            raise SystemExit(0)
raise SystemExit("No available iPhone Simulator device was found.")
' <<< "$devices_json")"
DEVICE_UDID="$selection"
if [[ -z "$DEVICE_UDID" ]]; then
  echo "Unable to select an available iPhone Simulator device." >&2
  exit 1
fi

cleanup() {
  xcrun simctl shutdown "$DEVICE_UDID" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# A fresh install must not inherit Keychain tokens or app data from a prior run.
# iOS Keychain entries can outlive an app uninstall, so reset the simulator itself.
xcrun simctl shutdown "$DEVICE_UDID" >/dev/null 2>&1 || true
xcrun simctl erase "$DEVICE_UDID"
xcrun simctl boot "$DEVICE_UDID"
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
        "simulatorDataErased": True,
        "loginScreenTextVerified": True,
        "staleSessionErrorAbsent": True,
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised",
        "signing": "ad-hoc simulator signature (test-only)",
        "keychainAccessGroup": "bundle-scoped test entitlement"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "iOS Simulator install/launch smoke passed for $EXPECTED_ID (PID $pid)."

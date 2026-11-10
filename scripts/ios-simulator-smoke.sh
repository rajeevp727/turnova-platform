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

# Keep the native CI artifact unsigned (CODE_SIGNING_ALLOWED=NO). On the hosted
# iOS 26.2 simulator, an ad-hoc signature without an Apple-issued TeamIdentifier
# is rejected by launchd with error 162 ("Codesigning issue"). SecureStore cannot
# be validated by this unsigned artifact, so OCR explicitly requires the app's
# fail-closed storage-unavailable banner. Keychain success remains an external
# signed-device validation gate.
signature_info="$(codesign -dv --verbose=4 "$APP_BUNDLE" 2>&1 || true)"
if grep -Fq "Signature=" <<< "$signature_info"; then
  echo "iOS Simulator smoke requires the original unsigned CI artifact; signed/ad-hoc copies are not supported." >&2
  exit 1
fi

# This script erases the selected simulator to guarantee a clean Keychain/app-data
# state. Never allow a local developer invocation to erase a personal simulator.
if [[ "${CI:-}" != "true" ]]; then
  echo "iOS Simulator smoke is destructive and may run only on a CI runner (CI=true)." >&2
  exit 2
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

capture_simulator_diagnostics() {
  local predicate
  predicate="eventMessage CONTAINS[c] \"$EXPECTED_ID\" OR process == \"SpringBoard\" OR process == \"amfid\" OR process == \"runningboardd\" OR process == \"installd\""

  # Keep failure diagnostics inside the ephemeral CI artifact; never emit app data
  # or credentials. The simulator has not been given production API/auth inputs.
  codesign -dv --verbose=4 "$APP_BUNDLE" > "$OUTPUT_DIR/app-signature.txt" 2>&1 || true
  codesign -d --entitlements :- "$APP_BUNDLE" > "$OUTPUT_DIR/app-entitlements.plist" 2>&1 || true
  xcrun simctl spawn "$DEVICE_UDID" log show --last 5m --style compact --predicate "$predicate" \
    > "$OUTPUT_DIR/simulator-system.log" 2>&1 || true
  xcrun simctl spawn "$DEVICE_UDID" launchctl list \
    > "$OUTPUT_DIR/launchctl-list-failure.txt" 2>&1 || true
  xcrun simctl listapps "$DEVICE_UDID" > "$OUTPUT_DIR/installed-apps.txt" 2>&1 || true

  echo "Captured iOS Simulator launch diagnostics in $OUTPUT_DIR." >&2
  for diagnostic in \
    "$OUTPUT_DIR/simulator-system.log" \
    "$OUTPUT_DIR/launchctl-list-failure.txt" \
    "$OUTPUT_DIR/process-info.txt" \
    "$OUTPUT_DIR/installed-apps.txt" \
    "$OUTPUT_DIR/app-signature.txt" \
    "$OUTPUT_DIR/app-entitlements.plist"; do
    if [[ -s "$diagnostic" ]]; then
      echo "--- $(basename "$diagnostic") (last 120 lines) ---" >&2
      tail -n 120 "$diagnostic" >&2
    fi
  done
}

# A fresh install must not inherit Keychain tokens or app data from a prior run.
# iOS Keychain entries can outlive an app uninstall, so reset the simulator itself.
xcrun simctl shutdown "$DEVICE_UDID" >/dev/null 2>&1 || true
xcrun simctl erase "$DEVICE_UDID"
xcrun simctl boot "$DEVICE_UDID"
# simctl bootstatus has no portable caller-configurable timeout. Bound the process
# externally so first-boot data migration can take up to ten minutes without
# allowing a wedged CoreSimulator process to hang the CI job indefinitely.
SIMULATOR_BOOT_TIMEOUT_SECONDS="${TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS:-600}"
if [[ ! "$SIMULATOR_BOOT_TIMEOUT_SECONDS" =~ ^[1-9][0-9]{0,2}$ ]] ||
   (( SIMULATOR_BOOT_TIMEOUT_SECONDS > 600 )); then
  echo "TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS must be an integer from 1 to 600." >&2
  exit 2
fi
python3 - "$DEVICE_UDID" "$SIMULATOR_BOOT_TIMEOUT_SECONDS" <<'PY'
import subprocess
import sys

device_udid = sys.argv[1]
timeout_seconds = int(sys.argv[2])
try:
    result = subprocess.run(
        ["xcrun", "simctl", "bootstatus", device_udid, "-b"],
        check=False,
        timeout=timeout_seconds,
    )
except subprocess.TimeoutExpired:
    print(
        f"iOS Simulator did not finish first-boot migration within {timeout_seconds} seconds.",
        file=sys.stderr,
    )
    raise SystemExit(124)
raise SystemExit(result.returncode)
PY

echo "Installing $EXPECTED_ID into the clean iOS Simulator."
if ! install_output="$(xcrun simctl install "$DEVICE_UDID" "$APP_BUNDLE" 2>&1)"; then
  printf '%s\n' "$install_output" >&2
  echo "iOS Simulator installation failed for $EXPECTED_ID." >&2
  exit 1
fi
if [[ -n "$install_output" ]]; then
  printf '%s\n' "$install_output"
fi

echo "Verifying the installed iOS Simulator app container."
if ! installed_container="$(xcrun simctl get_app_container "$DEVICE_UDID" "$EXPECTED_ID" app 2>&1)"; then
  printf '%s\n' "$installed_container" >&2
  echo "Unable to locate the installed iOS Simulator app container for $EXPECTED_ID." >&2
  exit 1
fi
if [[ -z "$installed_container" || ! -d "$installed_container" ]]; then
  echo "Expected iOS Simulator app was not installed: $EXPECTED_ID" >&2
  exit 1
fi

echo "Launching $EXPECTED_ID in the iOS Simulator."
if ! launch_output="$(xcrun simctl launch "$DEVICE_UDID" "$EXPECTED_ID" 2>&1)"; then
  printf '%s\n' "$launch_output" >&2
  capture_simulator_diagnostics
  echo "iOS Simulator app launch failed for $EXPECTED_ID." >&2
  exit 1
fi
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
  capture_simulator_diagnostics
  echo "iOS Simulator app process did not remain available after launch." >&2
  echo "launchctl list output:" >&2
  cat "$OUTPUT_DIR/launchctl-list.txt" >&2 || true
  echo "process info output:" >&2
  cat "$OUTPUT_DIR/process-info.txt" >&2 || true
  exit 1
fi

# A cold iOS simulator can report a live process before React Native has rendered
# the login screen. Poll only the OCR check's explicit "not ready" result; stale
# session/token-cleanup errors and OCR/tool failures remain immediate failures.
LOGIN_READY_TIMEOUT_SECONDS="${TURNOVA_IOS_LOGIN_READY_TIMEOUT_SECONDS:-90}"
LOGIN_READY_POLL_INTERVAL_SECONDS="${TURNOVA_IOS_LOGIN_READY_POLL_INTERVAL_SECONDS:-5}"
if [[ ! "$LOGIN_READY_TIMEOUT_SECONDS" =~ ^[1-9][0-9]{0,2}$ ]] ||
   (( LOGIN_READY_TIMEOUT_SECONDS > 300 )); then
  echo "TURNOVA_IOS_LOGIN_READY_TIMEOUT_SECONDS must be an integer from 1 to 300." >&2
  exit 2
fi
if [[ ! "$LOGIN_READY_POLL_INTERVAL_SECONDS" =~ ^[1-9][0-9]?$ ]] ||
   (( LOGIN_READY_POLL_INTERVAL_SECONDS > 30 )); then
  echo "TURNOVA_IOS_LOGIN_READY_POLL_INTERVAL_SECONDS must be an integer from 1 to 30." >&2
  exit 2
fi

login_ready_deadline=$((SECONDS + LOGIN_READY_TIMEOUT_SECONDS))
login_screen_verified=false
ocr_attempt=0
while true; do
  ocr_attempt=$((ocr_attempt + 1))
  if ! xcrun simctl io "$DEVICE_UDID" screenshot "$OUTPUT_DIR/launch.png"; then
    echo "Unable to capture the iOS Simulator launch screenshot for $EXPECTED_ID." >&2
    capture_simulator_diagnostics
    exit 1
  fi
  if [[ ! -s "$OUTPUT_DIR/launch.png" ]]; then
    echo "iOS Simulator screenshot was not produced." >&2
    capture_simulator_diagnostics
    exit 1
  fi

  if swift "$(dirname "$0")/ios-simulator-screen-text-check.swift" "$OUTPUT_DIR/launch.png" --expect-secure-store-unavailable \
      > "$OUTPUT_DIR/ui-text.txt" 2> "$OUTPUT_DIR/ui-text-error.txt"; then
    login_screen_verified=true
    break
  else
    ocr_status=$?
  fi

  if (( ocr_status != 3 )); then
    echo "iOS login OCR failed with non-retryable status $ocr_status on attempt $ocr_attempt." >&2
    cat "$OUTPUT_DIR/ui-text.txt" >&2 || true
    cat "$OUTPUT_DIR/ui-text-error.txt" >&2 || true
    capture_simulator_diagnostics
    exit 1
  fi

  if (( SECONDS >= login_ready_deadline )); then
    echo "iOS Simulator did not reach a stable login screen within $LOGIN_READY_TIMEOUT_SECONDS seconds." >&2
    echo "Last OCR output:" >&2
    cat "$OUTPUT_DIR/ui-text.txt" >&2 || true
    cat "$OUTPUT_DIR/ui-text-error.txt" >&2 || true
    capture_simulator_diagnostics
    exit 1
  fi

  echo "iOS login screen is not ready yet (attempt $ocr_attempt); retrying in $LOGIN_READY_POLL_INTERVAL_SECONDS seconds."
  sleep "$LOGIN_READY_POLL_INTERVAL_SECONDS"
done

if [[ "$login_screen_verified" != true ]]; then
  echo "iOS login screen was not verified for $EXPECTED_ID." >&2
  capture_simulator_diagnostics
  exit 1
fi
cat "$OUTPUT_DIR/ui-text.txt"

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
        "secureStoreAvailable": False,
        "secureStoreUnavailableBannerVerified": True,
        "staleSessionExpiredErrorAbsent": True,
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised",
        "signing": "unsigned simulator artifact (CI-only)",
        "keychainAccessGroup": "not available; signed-device validation required"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "iOS Simulator install/launch smoke passed for $EXPECTED_ID (PID $pid)."

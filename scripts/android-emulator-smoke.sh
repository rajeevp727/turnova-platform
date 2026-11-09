#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --apk <path> --expected-id <reverse-dns-package> [--output-dir <path>]" >&2
}

APK_PATH=""
EXPECTED_ID=""
OUTPUT_DIR="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/turnova-android-emulator-smoke"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --apk) [[ $# -ge 2 ]] || { usage; exit 2; }; APK_PATH="$2"; shift 2 ;;
    --expected-id) [[ $# -ge 2 ]] || { usage; exit 2; }; EXPECTED_ID="$2"; shift 2 ;;
    --output-dir) [[ $# -ge 2 ]] || { usage; exit 2; }; OUTPUT_DIR="$2"; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

if [[ -z "$APK_PATH" || -z "$EXPECTED_ID" ]]; then
  usage
  exit 2
fi
if [[ ! "$EXPECTED_ID" =~ ^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$ ]]; then
  echo "Expected Android package ID is malformed." >&2
  exit 2
fi
if [[ ! -s "$APK_PATH" ]]; then
  echo "Standalone release APK is missing or empty: $APK_PATH" >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
if [[ -z "$SDK_ROOT" ]]; then
  for candidate in "$HOME/Android/Sdk" "/usr/local/lib/android/sdk"; do
    if [[ -d "$candidate" ]]; then SDK_ROOT="$candidate"; break; fi
  done
fi
if [[ -z "$SDK_ROOT" || ! -d "$SDK_ROOT" ]]; then
  echo "Android SDK root is unavailable on this runner." >&2
  exit 1
fi

SDKMANAGER="$SDK_ROOT/cmdline-tools/latest/bin/sdkmanager"
if [[ ! -x "$SDKMANAGER" ]]; then
  SDKMANAGER="$(find "$SDK_ROOT/cmdline-tools" -type f -name sdkmanager -perm -u+x 2>/dev/null | sort -V | tail -n 1 || true)"
fi
if [[ -z "$SDKMANAGER" || ! -x "$SDKMANAGER" ]]; then
  echo "Android SDK command-line tools (sdkmanager) are unavailable." >&2
  exit 1
fi
AVDMANAGER="$(dirname "$SDKMANAGER")/avdmanager"
if [[ ! -x "$AVDMANAGER" ]]; then
  echo "Android SDK avdmanager is unavailable." >&2
  exit 1
fi

export ANDROID_SDK_ROOT="$SDK_ROOT"
export ANDROID_HOME="$SDK_ROOT"
export ANDROID_AVD_HOME="${ANDROID_AVD_HOME:-$HOME/.android/avd}"
export PATH="$SDK_ROOT/platform-tools:$SDK_ROOT/emulator:$PATH"

SYSTEM_IMAGE="system-images;android-35;google_apis;x86_64"
AVD_NAME="turnova-release-smoke"
AVD_INI="$ANDROID_AVD_HOME/$AVD_NAME.ini"
AVD_DIR="$ANDROID_AVD_HOME/$AVD_NAME.avd"
EMULATOR_PID=""

cleanup() {
  "$SDK_ROOT/platform-tools/adb" emu kill >/dev/null 2>&1 || true
  if [[ -n "$EMULATOR_PID" ]]; then
    kill "$EMULATOR_PID" >/dev/null 2>&1 || true
    wait "$EMULATOR_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# Accept SDK licenses and install only the native emulator packages needed for this smoke.
yes | "$SDKMANAGER" --sdk_root="$SDK_ROOT" --licenses >/dev/null 2>&1 || true
"$SDKMANAGER" --sdk_root="$SDK_ROOT" --install \
  "platform-tools" "emulator" "platforms;android-35" "$SYSTEM_IMAGE"

EMULATOR_BIN="$SDK_ROOT/emulator/emulator"
ADB_BIN="$SDK_ROOT/platform-tools/adb"
if [[ ! -x "$EMULATOR_BIN" || ! -x "$ADB_BIN" ]]; then
  echo "Android emulator or platform-tools installation is incomplete." >&2
  exit 1
fi

mkdir -p "$ANDROID_AVD_HOME"
"$AVDMANAGER" delete avd -n "$AVD_NAME" >/dev/null 2>&1 || true
printf 'no\n' | "$AVDMANAGER" create avd -f -n "$AVD_NAME" -k "$SYSTEM_IMAGE" -d "pixel_2" 2>&1 | tee "$OUTPUT_DIR/avdmanager-create.log"

if [[ ! -s "$AVD_INI" || ! -d "$AVD_DIR" ]]; then
  echo "Android AVD was not created in ANDROID_AVD_HOME=$ANDROID_AVD_HOME." >&2
  find "$HOME/.android" -maxdepth 3 -name "$AVD_NAME.ini" -o -name "$AVD_NAME.avd" >&2 || true
  exit 1
fi

"$EMULATOR_BIN" -avd "$AVD_NAME" -no-window -no-audio -no-boot-anim \
  -gpu swiftshader_indirect -no-snapshot -wipe-data -no-metrics \
  -memory 2048 -cores 2 >"$OUTPUT_DIR/emulator.log" 2>&1 &
EMULATOR_PID=$!

device_ready=false
for _ in $(seq 1 180); do
  if "$ADB_BIN" devices | awk '$1 ~ /^emulator-/ && $2 == "device" { found = 1 } END { exit !found }'; then
    device_ready=true
    break
  fi
  if ! kill -0 "$EMULATOR_PID" 2>/dev/null; then
    echo "Android emulator process exited before registering with ADB. Emulator diagnostics:" >&2
    cat "$OUTPUT_DIR/emulator.log" >&2 || true
    exit 1
  fi
  sleep 1
done
if [[ "$device_ready" != true ]]; then
  echo "Android emulator did not register with ADB within three minutes. Emulator diagnostics:" >&2
  cat "$OUTPUT_DIR/emulator.log" >&2 || true
  exit 1
fi
booted=false
for _ in $(seq 1 180); do
  if [[ "$("$ADB_BIN" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; then
    booted=true
    break
  fi
  sleep 2
done
if [[ "$booted" != true ]]; then
  echo "Android emulator did not complete boot within six minutes." >&2
  exit 1
fi

"$ADB_BIN" shell settings put global window_animation_scale 0
"$ADB_BIN" shell settings put global transition_animation_scale 0
"$ADB_BIN" shell settings put global animator_duration_scale 0
"$ADB_BIN" shell input keyevent 82 >/dev/null 2>&1 || true

"$ADB_BIN" install -r "$APK_PATH" | tee "$OUTPUT_DIR/install.txt"
installed_path="$("$ADB_BIN" shell pm path "$EXPECTED_ID" | tr -d '\r')"
if [[ -z "$installed_path" || "$installed_path" != package:* ]]; then
  echo "Expected package was not installed: $EXPECTED_ID" >&2
  exit 1
fi

resolution="$("$ADB_BIN" shell cmd package resolve-activity --brief "$EXPECTED_ID" | tr -d '\r')"
activity="$(printf '%s\n' "$resolution" | awk -v prefix="$EXPECTED_ID/" 'index($0, prefix) == 1 { resolved = $0 } END { print resolved }')"
if [[ -z "$activity" ]]; then
  echo "No launchable activity resolved for $EXPECTED_ID." >&2
  printf '%s\n' "$resolution" >&2
  exit 1
fi

launch_output="$("$ADB_BIN" shell am start -W -n "$activity" 2>&1 | tr -d '\r')"
printf '%s\n' "$launch_output" | tee "$OUTPUT_DIR/launch.txt"
if ! printf '%s\n' "$launch_output" | grep -Fq "Status: ok"; then
  echo "Android ActivityManager did not report a successful launch." >&2
  exit 1
fi

sleep 8
pid_before="$("$ADB_BIN" shell pidof "$EXPECTED_ID" 2>/dev/null | tr -d '\r' | awk '{ print $1 }')"
if [[ -z "$pid_before" ]]; then
  echo "App process exited after launch: $EXPECTED_ID" >&2
  exit 1
fi

"$ADB_BIN" shell dumpsys activity activities | tr -d '\r' > "$OUTPUT_DIR/activities.txt"
if ! grep -Fq "$EXPECTED_ID/" "$OUTPUT_DIR/activities.txt"; then
  echo "Launched activity is not present in the emulator activity stack." >&2
  exit 1
fi

"$ADB_BIN" logcat -d -v threadtime > "$OUTPUT_DIR/logcat.txt"
if awk -v package="$EXPECTED_ID" '
  /FATAL EXCEPTION/ { in_fatal = 1; scanned = 0; next }
  in_fatal {
    if (index($0, "Process: " package ",") > 0 || index($0, "Process: " package " ") > 0) found = 1
    scanned++
    if (scanned > 40) in_fatal = 0
  }
  END { exit found ? 0 : 1 }
' "$OUTPUT_DIR/logcat.txt"; then
  echo "Android runtime recorded a fatal exception for $EXPECTED_ID." >&2
  exit 1
fi
if awk -v pid="$pid_before" '$3 == pid && /Unhandled JS Exception|Invariant Violation:|Unable to load script/' "$OUTPUT_DIR/logcat.txt"; then
  echo "React Native reported an unhandled startup exception in $EXPECTED_ID (PID $pid_before)." >&2
  exit 1
fi

sleep 5
pid_after="$("$ADB_BIN" shell pidof "$EXPECTED_ID" 2>/dev/null | tr -d '\r' | awk '{ print $1 }')"
if [[ -z "$pid_after" || "$pid_after" != "$pid_before" ]]; then
  echo "App process did not remain stable after startup (before=$pid_before, after=$pid_after)." >&2
  exit 1
fi

"$ADB_BIN" exec-out screencap -p > "$OUTPUT_DIR/launch.png"
if [[ ! -s "$OUTPUT_DIR/launch.png" ]]; then
  echo "Android emulator screenshot was not produced." >&2
  exit 1
fi

python3 - "$EXPECTED_ID" "$activity" "$pid_after" "$SYSTEM_IMAGE" "$OUTPUT_DIR/result.json" <<'PY'
import json
import sys

package_id, activity, pid, system_image, output = sys.argv[1:]
with open(output, "w", encoding="utf-8") as stream:
    json.dump({
        "platform": "android-emulator",
        "packageId": package_id,
        "launchActivity": activity,
        "processId": pid,
        "systemImage": system_image,
        "install": "passed",
        "launch": "passed",
        "processStableAfterStartup": True,
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "Android emulator install/launch smoke passed for $EXPECTED_ID ($activity, PID $pid_after)."

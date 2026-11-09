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

# Accept SDK licenses and install the stable platform/emulator packages first.
yes | "$SDKMANAGER" --sdk_root="$SDK_ROOT" --licenses >/dev/null 2>&1 || true
"$SDKMANAGER" --sdk_root="$SDK_ROOT" --install \
  "platform-tools" "emulator" "platforms;android-35"

# Google-hosted SDK package downloads occasionally arrive as a truncated/corrupt
# archive ("ZipFile unknown archive"). Retry only the API 35 system image, verify
# the extracted package before AVD creation, and fail closed after three attempts.
SYSTEM_IMAGE_DIR="$SDK_ROOT/system-images/android-35/google_apis/x86_64"
SYSTEM_IMAGE_RETRY_DELAY_SECONDS="${TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS:-2}"
if [[ ! "$SYSTEM_IMAGE_RETRY_DELAY_SECONDS" =~ ^(0|[1-9]|[12][0-9]|30)$ ]]; then
  echo "TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS must be an integer from 0 to 30." >&2
  exit 2
fi

install_system_image_with_retry() {
  local attempt=1
  local max_attempts=3
  local status=1
  local log_file

  while (( attempt <= max_attempts )); do
    log_file="$OUTPUT_DIR/sdkmanager-system-image-attempt-$attempt.log"
    if "$SDKMANAGER" --sdk_root="$SDK_ROOT" --install "$SYSTEM_IMAGE" >"$log_file" 2>&1; then
      if [[ -s "$SYSTEM_IMAGE_DIR/package.xml" && -s "$SYSTEM_IMAGE_DIR/system.img" ]]; then
        cat "$log_file"
        echo "Android API 35 system image installed and verified on attempt $attempt."
        return 0
      fi
      status=1
      echo "sdkmanager returned success but the system image package is incomplete." >>"$log_file"
    else
      status=$?
    fi

    echo "Android system image installation attempt $attempt/$max_attempts failed (exit $status):" >&2
    tail -n 80 "$log_file" >&2 || true

    # A failed sdkmanager download can leave non-empty but corrupt files behind.
    # Always remove this isolated package directory before the next attempt.
    if [[ -d "$SYSTEM_IMAGE_DIR" ]]; then
      rm -rf "$SYSTEM_IMAGE_DIR"
    fi

    if (( attempt < max_attempts )); then
      echo "Retrying the Android API 35 system image download." >&2
      sleep "$SYSTEM_IMAGE_RETRY_DELAY_SECONDS"
    fi
    attempt=$((attempt + 1))
  done

  echo "Android API 35 system image installation failed after $max_attempts attempts." >&2
  return "$status"
}

install_system_image_with_retry

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

capture_ui_hierarchy() {
  "$ADB_BIN" shell uiautomator dump /sdcard/turnova-ui-hierarchy.xml > "$OUTPUT_DIR/uiautomator-dump.txt" 2>&1
  "$ADB_BIN" shell cat /sdcard/turnova-ui-hierarchy.xml | tr -d '\r' > "$OUTPUT_DIR/ui-hierarchy.xml"
  if [[ ! -s "$OUTPUT_DIR/ui-hierarchy.xml" ]]; then
    echo "Android UI hierarchy dump is missing or empty." >&2
    cat "$OUTPUT_DIR/uiautomator-dump.txt" >&2 || true
    exit 1
  fi
}

capture_ui_hierarchy
pixel_launcher_anr_wait_attempts=0
while grep -Fq "Pixel Launcher isn't responding" "$OUTPUT_DIR/ui-hierarchy.xml"; do
  if (( pixel_launcher_anr_wait_attempts >= 2 )); then
    echo "Pixel Launcher ANR dialog persisted after two bounded Wait attempts." >&2
    cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
    exit 1
  fi

  pixel_launcher_anr_wait_attempts=$((pixel_launcher_anr_wait_attempts + 1))
  cp "$OUTPUT_DIR/ui-hierarchy.xml" "$OUTPUT_DIR/pixel-launcher-anr-$pixel_launcher_anr_wait_attempts.xml"

  if ! wait_coordinates="$(python3 - "$OUTPUT_DIR/ui-hierarchy.xml" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET

try:
    root = ET.parse(sys.argv[1]).getroot()
except (OSError, ET.ParseError):
    sys.exit(2)

title = next(
    (
        node.attrib.get("text")
        for node in root.iter("node")
        if node.attrib.get("resource-id") == "android:id/alertTitle"
    ),
    None,
)
if title != "Pixel Launcher isn't responding":
    sys.exit(3)

wait_button = next(
    (
        node
        for node in root.iter("node")
        if node.attrib.get("resource-id") == "android:id/aerr_wait"
        and node.attrib.get("text") == "Wait"
    ),
    None,
)
if wait_button is None:
    sys.exit(4)

match = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", wait_button.attrib.get("bounds", ""))
if match is None:
    sys.exit(5)

left, top, right, bottom = map(int, match.groups())
print((left + right) // 2, (top + bottom) // 2)
PY
  )"; then
    echo "Pixel Launcher ANR was detected but its safe Wait control could not be resolved." >&2
    cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
    exit 1
  fi

  if [[ ! "$wait_coordinates" =~ ^[0-9]+[[:space:]][0-9]+$ ]]; then
    echo "Pixel Launcher ANR Wait control returned invalid coordinates." >&2
    exit 1
  fi

  read -r wait_x wait_y <<< "$wait_coordinates"
  echo "Pixel Launcher ANR detected; selecting Wait at ($wait_x,$wait_y), attempt $pixel_launcher_anr_wait_attempts/2." | tee -a "$OUTPUT_DIR/system-ui-recovery.txt"
  "$ADB_BIN" shell input tap "$wait_x" "$wait_y"
  sleep 5
  capture_ui_hierarchy
done

if grep -Fq "isn't responding" "$OUTPUT_DIR/ui-hierarchy.xml"; then
  echo "Unexpected Android app/system ANR dialog remains after Pixel Launcher recovery." >&2
  cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
  exit 1
fi

for expected_text in "Welcome back" "Email address" "Password" "Sign in"; do
  if ! grep -Fq "$expected_text" "$OUTPUT_DIR/ui-hierarchy.xml"; then
    echo "Android login screen is missing expected visible/accessibility text: $expected_text" >&2
    cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
    exit 1
  fi
done
for forbidden_text in "Unable to restore your session" "Unable to restore session" "Session expired" "Unable to clear secure session storage"; do
  if grep -Fq "$forbidden_text" "$OUTPUT_DIR/ui-hierarchy.xml"; then
    echo "Android fresh-install login UI contains an unexpected session/storage error: $forbidden_text" >&2
    exit 1
  fi
done

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
if awk -v pid="$pid_before" '$3 == pid && /Unhandled JS Exception|Invariant Violation:|Unable to load script/ { found = 1 } END { exit found ? 0 : 1 }' "$OUTPUT_DIR/logcat.txt"; then
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

python3 - "$EXPECTED_ID" "$activity" "$pid_after" "$SYSTEM_IMAGE" "$pixel_launcher_anr_wait_attempts" "$OUTPUT_DIR/result.json" <<'PY'
import json
import sys

package_id, activity, pid, system_image, anr_wait_attempts, output = sys.argv[1:]
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
        "loginScreenTextVerified": True,
        "staleSessionErrorAbsent": True,
        "systemUiRecovery": {
            "pixelLauncherAnrWaitAttempts": int(anr_wait_attempts),
            "pixelLauncherAnrRecovered": int(anr_wait_attempts) > 0
        },
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "Android emulator install/launch smoke passed for $EXPECTED_ID ($activity, PID $pid_after)."

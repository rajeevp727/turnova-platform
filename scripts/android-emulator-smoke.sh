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

# This smoke installs SDK packages and launches an emulator. Keep it on the
# ephemeral CI runner; local developer SDK/emulator state must not be mutated.
if [[ "${CI:-}" != "true" ]]; then
  echo "Android emulator smoke installs SDK packages and may run only on a CI runner (CI=true)." >&2
  exit 2
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
export ANDROID_AVD_HOME="${ANDROID_AVD_HOME:-${RUNNER_TEMP:-${TMPDIR:-/tmp}}/turnova-android-avd}"
export PATH="$SDK_ROOT/platform-tools:$SDK_ROOT/emulator:$PATH"

SYSTEM_IMAGE="system-images;android-35;google_apis;x86_64"
AVD_NAME="turnova-release-smoke-${PPID}-${RANDOM}-${RANDOM}"
AVD_INI="$ANDROID_AVD_HOME/$AVD_NAME.ini"
AVD_DIR="$ANDROID_AVD_HOME/$AVD_NAME.avd"
EMULATOR_PID=""
AVD_CREATED=false

cleanup() {
  if [[ -n "$EMULATOR_PID" ]]; then
    kill "$EMULATOR_PID" >/dev/null 2>&1 || true
    wait "$EMULATOR_PID" 2>/dev/null || true
  fi
  # Remove only the uniquely named AVD created by this invocation. Never delete
  # a pre-existing developer AVD from a shared ANDROID_AVD_HOME.
  if [[ "$AVD_CREATED" == true ]]; then
    rm -rf "$AVD_DIR" "$AVD_INI"
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
SYSTEM_IMAGE_PREEXISTED=false
if [[ -e "$SYSTEM_IMAGE_DIR" || -L "$SYSTEM_IMAGE_DIR" ]]; then
  if [[ -s "$SYSTEM_IMAGE_DIR/package.xml" && -s "$SYSTEM_IMAGE_DIR/system.img" ]]; then
    SYSTEM_IMAGE_PREEXISTED=true
  else
    echo "Existing Android API 35 system image is incomplete; refusing to modify or remove it." >&2
    exit 1
  fi
fi
SYSTEM_IMAGE_RETRY_DELAY_SECONDS="${TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS:-2}"
if [[ ! "$SYSTEM_IMAGE_RETRY_DELAY_SECONDS" =~ ^(0|[1-9]|[12][0-9]|30)$ ]]; then
  echo "TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS must be an integer from 0 to 30." >&2
  exit 2
fi

install_system_image_with_retry() {
  if [[ "$SYSTEM_IMAGE_PREEXISTED" == true ]]; then
    echo "Using the pre-existing Android API 35 system image without modifying it."
    return 0
  fi

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
    if [[ "$SYSTEM_IMAGE_PREEXISTED" != true && -d "$SYSTEM_IMAGE_DIR" ]]; then
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
if [[ -e "$AVD_INI" || -e "$AVD_DIR" ]]; then
  echo "Refusing to replace an existing Android AVD at $AVD_INI or $AVD_DIR." >&2
  exit 1
fi
AVD_CREATED=true
printf 'no\n' | "$AVDMANAGER" create avd -f -n "$AVD_NAME" -k "$SYSTEM_IMAGE" -d "pixel_2" 2>&1 | tee "$OUTPUT_DIR/avdmanager-create.log"

if [[ ! -s "$AVD_INI" || ! -d "$AVD_DIR" ]]; then
  echo "Android AVD was not created in ANDROID_AVD_HOME=$ANDROID_AVD_HOME." >&2
  find "$ANDROID_AVD_HOME" -maxdepth 3 -name "$AVD_NAME.ini" -o -name "$AVD_NAME.avd" >&2 || true
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
"$ADB_BIN" exec-out screencap -p > "$OUTPUT_DIR/initial.png" || true
system_anr_wait_attempts=0
pixel_launcher_anr_wait_attempts=0
: > "$OUTPUT_DIR/system-anr-packages-recovered.txt"
: > "$OUTPUT_DIR/system-anr-packages-closed.txt"
: > "$OUTPUT_DIR/system-anr-titles-recovered.txt"

read_anr_title() {
  python3 - "$OUTPUT_DIR/ui-hierarchy.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET

try:
    root = ET.parse(sys.argv[1]).getroot()
except (OSError, ET.ParseError):
    sys.exit(2)

title = next(
    (
        node.attrib.get("text", "")
        for node in root.iter("node")
        if node.attrib.get("resource-id") == "android:id/alertTitle"
    ),
    "",
)
print(title if "isn't responding" in title else "")
PY
}

button_coordinates() {
  python3 - "$OUTPUT_DIR/ui-hierarchy.xml" "$1" "$2" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET

try:
    root = ET.parse(sys.argv[1]).getroot()
except (OSError, ET.ParseError):
    sys.exit(2)

resource_id, expected_text = sys.argv[2:]
button = next(
    (
        node
        for node in root.iter("node")
        if node.attrib.get("resource-id") == resource_id
        and node.attrib.get("text") == expected_text
    ),
    None,
)
if button is None:
    sys.exit(3)
if button.attrib.get("clickable") != "true" or button.attrib.get("enabled") != "true":
    sys.exit(4)

match = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", button.attrib.get("bounds", ""))
if match is None:
    sys.exit(5)

left, top, right, bottom = map(int, match.groups())
print((left + right) // 2, (top + bottom) // 2)
PY
}

assert_target_app_foreground() {
  local current_pid resumed_line
  current_pid="$("$ADB_BIN" shell pidof "$EXPECTED_ID" 2>/dev/null | tr -d '\r' | awk '{ print $1 }')"
  if [[ -z "$current_pid" || "$current_pid" != "$pid_before" ]]; then
    echo "Target app process is not stable during Android system-UI recovery (before=$pid_before, after=$current_pid)." >&2
    exit 1
  fi

  "$ADB_BIN" shell dumpsys activity activities | tr -d '\r' > "$OUTPUT_DIR/activities-current.txt"
  resumed_line="$(awk '/topResumedActivity=|mResumedActivity:/ { line = $0 } END { print line }' "$OUTPUT_DIR/activities-current.txt")"
  if [[ "$resumed_line" != *"$EXPECTED_ID/"* ]]; then
    echo "Target app is not the resumed foreground activity during Android system-UI recovery." >&2
    printf '%s\n' "$resumed_line" >&2
    exit 1
  fi
}

capture_app_runtime_logcat() {
  "$ADB_BIN" logcat -d -v threadtime > "$OUTPUT_DIR/logcat.txt"

  if grep -Fq "ANR in $EXPECTED_ID" "$OUTPUT_DIR/logcat.txt"; then
    echo "Android runtime recorded an ANR for the TurnOva app $EXPECTED_ID." >&2
    exit 1
  fi
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
}

# The app must be the resumed foreground activity before any system-dialog
# recovery. This prevents a launcher recovery from masking an app launch failure.
assert_target_app_foreground
capture_app_runtime_logcat

while true; do
  anr_title="$(read_anr_title)"
  [[ -z "$anr_title" ]] && break

  case "$anr_title" in
    "Pixel Launcher isn't responding")
      system_package="com.google.android.apps.nexuslauncher"
      ;;
    "com.google.android.googlesdksetup isn't responding")
      system_package="com.google.android.googlesdksetup"
      ;;
    *)
      echo "Unexpected Android app/system ANR dialog: $anr_title" >&2
      cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
      exit 1
      ;;
  esac

  if grep -Fxq "$system_package" "$OUTPUT_DIR/system-anr-packages-recovered.txt"; then
    echo "Android system ANR dialog for $system_package persisted after bounded recovery." >&2
    cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
    exit 1
  fi

  assert_target_app_foreground
  cp "$OUTPUT_DIR/ui-hierarchy.xml" "$OUTPUT_DIR/system-anr-$system_anr_wait_attempts.xml"

  # Give a known, non-TurnOva system process one bounded chance to recover.
  # If it remains blocked, close only the explicitly allow-listed system app.
  if wait_coordinates="$(button_coordinates "android:id/aerr_wait" "Wait" 2>/dev/null)"; then
    if [[ ! "$wait_coordinates" =~ ^[0-9]+[[:space:]][0-9]+$ ]]; then
      echo "Android system ANR Wait control returned invalid coordinates." >&2
      exit 1
    fi
    read -r wait_x wait_y <<< "$wait_coordinates"
    system_anr_wait_attempts=$((system_anr_wait_attempts + 1))
    if [[ "$system_package" == "com.google.android.apps.nexuslauncher" ]]; then
      pixel_launcher_anr_wait_attempts=$((pixel_launcher_anr_wait_attempts + 1))
    fi
    echo "Known Android system ANR ($system_package); selecting Wait at ($wait_x,$wait_y)." | tee -a "$OUTPUT_DIR/system-ui-recovery.txt"
    "$ADB_BIN" shell input tap "$wait_x" "$wait_y"
    sleep 5
    capture_ui_hierarchy
  fi

  next_anr_title="$(read_anr_title)"
  if [[ "$next_anr_title" == "$anr_title" ]]; then
    if ! close_coordinates="$(button_coordinates "android:id/aerr_close" "Close app" 2>/dev/null)"; then
      echo "Known Android system ANR ($system_package) persisted, but its safe Close app control could not be resolved." >&2
      cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
      exit 1
    fi
    if [[ ! "$close_coordinates" =~ ^[0-9]+[[:space:]][0-9]+$ ]]; then
      echo "Android system ANR Close app control returned invalid coordinates." >&2
      exit 1
    fi

    read -r close_x close_y <<< "$close_coordinates"
    echo "Known Android system ANR ($system_package) persisted; closing only that allow-listed system process at ($close_x,$close_y)." | tee -a "$OUTPUT_DIR/system-ui-recovery.txt"
    "$ADB_BIN" shell input tap "$close_x" "$close_y"
    printf '%s\n' "$system_package" >> "$OUTPUT_DIR/system-anr-packages-closed.txt"
    sleep 3
    capture_ui_hierarchy
    next_anr_title="$(read_anr_title)"
    if [[ "$next_anr_title" == "$anr_title" ]]; then
      echo "Android system ANR dialog for $system_package remained after bounded Wait/Close recovery." >&2
      cat "$OUTPUT_DIR/ui-hierarchy.xml" >&2
      exit 1
    fi
  fi

  printf '%s\n' "$system_package" >> "$OUTPUT_DIR/system-anr-packages-recovered.txt"
  printf '%s\n' "$anr_title" >> "$OUTPUT_DIR/system-anr-titles-recovered.txt"
  assert_target_app_foreground
  capture_app_runtime_logcat
done

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

capture_app_runtime_logcat

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

python3 - "$EXPECTED_ID" "$activity" "$pid_after" "$SYSTEM_IMAGE" "$pixel_launcher_anr_wait_attempts" "$system_anr_wait_attempts" "$OUTPUT_DIR/system-anr-packages-recovered.txt" "$OUTPUT_DIR/system-anr-packages-closed.txt" "$OUTPUT_DIR/system-anr-titles-recovered.txt" "$OUTPUT_DIR/result.json" <<'PY'
import json
import sys

(
    package_id,
    activity,
    pid,
    system_image,
    pixel_launcher_wait_attempts,
    system_anr_wait_attempts,
    recovered_path,
    closed_path,
    titles_path,
    output,
) = sys.argv[1:]

def read_lines(path):
    with open(path, encoding="utf-8") as stream:
        return [line.strip() for line in stream if line.strip()]

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
            "pixelLauncherAnrWaitAttempts": int(pixel_launcher_wait_attempts),
            "pixelLauncherAnrRecovered": "com.google.android.apps.nexuslauncher" in read_lines(recovered_path),
            "waitAttempts": int(system_anr_wait_attempts),
            "systemAnrPackagesRecovered": read_lines(recovered_path),
            "systemAnrPackagesClosed": read_lines(closed_path),
            "systemAnrTitlesRecovered": read_lines(titles_path),
            "targetAppRemainedForegroundAndStable": True
        },
        "screenshot": "launch.png",
        "productionApiOrAuthentication": "not exercised"
    }, stream, indent=2)
    stream.write("\n")
PY

echo "Android emulator install/launch smoke passed for $EXPECTED_ID ($activity, PID $pid_after)."

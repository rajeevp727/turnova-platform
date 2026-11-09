#!/usr/bin/env python3
"""Deterministic harness tests for the Android release-APK emulator smoke."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SMOKE_SCRIPT = REPO_ROOT / "scripts" / "android-emulator-smoke.sh"


def write_executable(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    path.chmod(0o755)


class AndroidEmulatorSmokeHarnessTests(unittest.TestCase):
    def test_avd_home_install_launch_and_diagnostics_with_fake_sdk(self) -> None:
        with tempfile.TemporaryDirectory(prefix="turnova-android-smoke-") as temporary:
            root = Path(temporary)
            sdk = root / "sdk"
            command_line = sdk / "cmdline-tools" / "latest" / "bin"
            platform_tools = sdk / "platform-tools"
            emulator_dir = sdk / "emulator"
            home = root / "home"
            avd_home = root / "custom-avd-home"
            output = root / "output"
            apk = root / "turnova-rider-release.apk"
            apk.write_bytes(b"deterministic-apk-fixture")

            write_executable(
                command_line / "sdkmanager",
                r"""#!/usr/bin/env bash
set -euo pipefail
case "$*" in
  *--licenses*) exit 0 ;;
  *--install*)
    if [[ "$*" == *"system-images;android-35;google_apis;x86_64"* ]]; then
      attempts_file="$ANDROID_SDK_ROOT/system-image-install-attempts"
      attempts=0
      [[ ! -f "$attempts_file" ]] || read -r attempts < "$attempts_file"
      attempts=$((attempts + 1))
      printf '%s\n' "$attempts" > "$attempts_file"
      package_dir="$ANDROID_SDK_ROOT/system-images/android-35/google_apis/x86_64"
      if (( attempts <= ${FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS:-0} )); then
        mkdir -p "$package_dir"
        printf '<localPackage path="partial-corrupt-package"/>\n' > "$package_dir/package.xml"
        printf 'partial-corrupt-system-image' > "$package_dir/system.img"
        printf 'corrupt' > "$package_dir/corrupt.marker"
        echo "Warning: Google APIs system image download failed: ZipFile unknown archive" >&2
        exit 1
      fi
      if [[ -e "$package_dir/corrupt.marker" ]]; then
        echo "Corrupt partial system image survived failed-attempt cleanup." >&2
        exit 9
      fi
      mkdir -p "$package_dir"
      printf '<localPackage path="system-images;android-35;google_apis;x86_64"/>\n' > "$package_dir/package.xml"
      printf 'deterministic-system-image-fixture' > "$package_dir/system.img"
      echo "Installed system image package fixture."
    fi
    exit 0
    ;;
  *) echo "unexpected sdkmanager arguments: $*" >&2; exit 2 ;;
esac
""",
            )
            write_executable(
                command_line / "avdmanager",
                r"""#!/usr/bin/env bash
set -euo pipefail
case "$1 $2" in
  "delete avd") exit 0 ;;
  "create avd")
    name=""
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == "-n" && $# -ge 2 ]]; then name="$2"; shift 2; else shift; fi
    done
    test -n "$name"
    test -n "$ANDROID_AVD_HOME"
    mkdir -p "$ANDROID_AVD_HOME/$name.avd"
    printf 'path=%s\n' "$ANDROID_AVD_HOME/$name.avd" > "$ANDROID_AVD_HOME/$name.ini"
    echo "Created $name in $ANDROID_AVD_HOME"
    ;;
  *) echo "unexpected avdmanager arguments: $*" >&2; exit 2 ;;
esac
""",
            )
            write_executable(
                emulator_dir / "emulator",
                r"""#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == "-avd" ]]
[[ -s "$ANDROID_AVD_HOME/$2.ini" ]]
exec /bin/sleep 300
""",
            )
            write_executable(
                platform_tools / "adb",
                r"""#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  devices)
    printf 'List of devices attached\nemulator-5554 device\n'
    ;;
  emu)
    exit 0
    ;;
  install)
    echo "Success"
    ;;
  shell)
    shift
    case "$1" in
      getprop) echo "1" ;;
      settings) exit 0 ;;
      input)
        if [[ "${2:-}" == "tap" ]]; then
          printf '%s %s\n' "${3:-}" "${4:-}" >> "$FAKE_INPUT_LOG"
        fi
        ;;
      uiautomator) echo "UI hierarchy dumped to: /sdcard/turnova-ui-hierarchy.xml" ;;
      cat)
        dump_count=0
        if [[ -f "$FAKE_UI_DUMP_COUNT_FILE" ]]; then
          read -r dump_count < "$FAKE_UI_DUMP_COUNT_FILE"
        fi
        dump_count=$((dump_count + 1))
        printf '%s\n' "$dump_count" > "$FAKE_UI_DUMP_COUNT_FILE"
        if [[ "${FAKE_OTHER_ANR_ALWAYS:-0}" == "1" ]]; then
          printf '%s\n' "<hierarchy><node resource-id=\"android:id/alertTitle\" text=\"TurnOva Rider isn't responding\"/><node resource-id=\"android:id/aerr_wait\" text=\"Wait\" bounds=\"[70,1024][1010,1150]\" clickable=\"true\" enabled=\"true\"/></hierarchy>"
        elif [[ "${FAKE_PIXEL_LAUNCHER_ANR_ALWAYS:-0}" == "1" || ( "${FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP:-0}" == "1" && "$dump_count" == "1" ) ]]; then
          if [[ "${FAKE_PIXEL_LAUNCHER_WAIT_DISABLED:-0}" == "1" ]]; then
            wait_state='clickable="false" enabled="false"'
          else
            wait_state='clickable="true" enabled="true"'
          fi
          printf '%s\n' "<hierarchy><node resource-id=\"android:id/alertTitle\" text=\"Pixel Launcher isn't responding\"/><node resource-id=\"android:id/aerr_wait\" text=\"Wait\" bounds=\"[70,1024][1010,1150]\" ${wait_state}/></hierarchy>"
        elif [[ "${FAKE_RESTORE_ERROR:-0}" == "1" ]]; then
          printf '<hierarchy><node text="Welcome back"/><node hint="Email address"/><node hint="Password"/><node text="Sign in"/><node text="Unable to restore your session. Check your connection or secure storage and retry."/></hierarchy>\n'
        else
          printf '<hierarchy><node text="Welcome back"/><node hint="Email address"/><node hint="Password"/><node text="Sign in"/></hierarchy>\n'
        fi
        ;;
      pm) echo "package:/data/app/turnova/base.apk" ;;
      cmd) echo "com.turnova.rider/.MainActivity" ;;
      am) printf 'Status: ok\nActivity: com.turnova.rider/.MainActivity\n' ;;
      pidof) echo "4242" ;;
      dumpsys) echo "mResumedActivity: ActivityRecord { com.turnova.rider/.MainActivity }" ;;
      *) echo "unexpected adb shell arguments: $*" >&2; exit 2 ;;
    esac
    ;;
  logcat)
    if [[ "$FAKE_TARGET_EXCEPTION" == "1" ]]; then
      printf '2026-11-09 11:00:00.000 4242 4242 E ReactNativeJS: Unhandled JS Exception: target fixture\n'
    else
      printf '2026-11-09 11:00:00.000 1111 1111 E ReactNativeJS: Unhandled JS Exception: unrelated fixture\n'
    fi
    ;;
  exec-out)
    printf '\211PNG\r\n\032\n'
    ;;
  *)
    echo "unexpected adb arguments: $*" >&2
    exit 2
    ;;
esac
""",
            )
            write_executable(
                platform_tools / "sleep",
                r"""#!/usr/bin/env bash
# Short smoke waits are unnecessary with the fake device.
exit 0
""",
            )

            environment = os.environ.copy()
            environment.update(
                {
                    "HOME": str(home),
                    "ANDROID_SDK_ROOT": str(sdk),
                    "ANDROID_HOME": str(sdk),
                    "ANDROID_AVD_HOME": str(avd_home),
                    "RUNNER_TEMP": str(root / "runner-temp"),
                    "FAKE_TARGET_EXCEPTION": "0",
                    "FAKE_RESTORE_ERROR": "0",
                    "FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP": "0",
                    "FAKE_PIXEL_LAUNCHER_ANR_ALWAYS": "0",
                    "FAKE_PIXEL_LAUNCHER_WAIT_DISABLED": "0",
                    "FAKE_OTHER_ANR_ALWAYS": "0",
                    "FAKE_UI_DUMP_COUNT_FILE": str(output / "ui-dump-count"),
                    "FAKE_INPUT_LOG": str(output / "input-taps.txt"),
                    "FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS": "1",
                    "TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS": "0",
                }
            )

            command = [
                "bash",
                str(SMOKE_SCRIPT),
                "--apk",
                str(apk),
                "--expected-id",
                "com.turnova.rider",
                "--output-dir",
                str(output),
            ]
            result = subprocess.run(
                command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )

            self.assertEqual(
                result.returncode,
                0,
                msg=f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertTrue((avd_home / "turnova-release-smoke.ini").is_file())
            self.assertTrue((avd_home / "turnova-release-smoke.avd").is_dir())
            self.assertTrue((output / "avdmanager-create.log").is_file())
            attempts_file = sdk / "system-image-install-attempts"
            self.assertEqual(attempts_file.read_text(encoding="utf-8").strip(), "2")
            self.assertIn("unknown archive", (output / "sdkmanager-system-image-attempt-1.log").read_text(encoding="utf-8"))
            self.assertIn("system image", (output / "sdkmanager-system-image-attempt-2.log").read_text(encoding="utf-8").lower())
            self.assertGreater((output / "launch.png").stat().st_size, 0)
            report = json.loads((output / "result.json").read_text(encoding="utf-8"))
            self.assertEqual(report["packageId"], "com.turnova.rider")
            self.assertEqual(report["install"], "passed")
            self.assertEqual(report["launch"], "passed")
            self.assertTrue(report["processStableAfterStartup"])
            self.assertTrue(report["loginScreenTextVerified"])
            ui_hierarchy = (output / "ui-hierarchy.xml").read_text(encoding="utf-8")
            for expected_text in ("Welcome back", "Email address", "Password", "Sign in"):
                self.assertIn(expected_text, ui_hierarchy)
            self.assertEqual(report["productionApiOrAuthentication"], "not exercised")
            self.assertTrue(report["staleSessionErrorAbsent"])
            self.assertEqual(report["systemUiRecovery"]["pixelLauncherAnrWaitAttempts"], 0)

            # A transient Pixel Launcher ANR is handled only through its explicit Wait
            # control; the app must still render the login surface and produce evidence.
            recovered_output = root / "anr-recovered-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(recovered_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(recovered_output / "input-taps.txt")
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "1"
            recovered_command = command[:-1] + [str(recovered_output)]
            recovered_anr = subprocess.run(
                recovered_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(
                recovered_anr.returncode,
                0,
                msg=f"stdout:\n{recovered_anr.stdout}\nstderr:\n{recovered_anr.stderr}",
            )
            recovered_report = json.loads((recovered_output / "result.json").read_text(encoding="utf-8"))
            self.assertEqual(recovered_report["systemUiRecovery"]["pixelLauncherAnrWaitAttempts"], 1)
            self.assertTrue(recovered_report["systemUiRecovery"]["pixelLauncherAnrRecovered"])
            self.assertIn("540 1087", (recovered_output / "input-taps.txt").read_text(encoding="utf-8"))
            self.assertTrue((recovered_output / "pixel-launcher-anr-1.xml").is_file())
            recovered_ui = (recovered_output / "ui-hierarchy.xml").read_text(encoding="utf-8")
            for expected_text in ("Welcome back", "Email address", "Password", "Sign in"):
                self.assertIn(expected_text, recovered_ui)

            # A persistent launcher ANR fails closed after two Wait attempts and
            # cannot leave a success result behind.
            persistent_output = root / "anr-persistent-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(persistent_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(persistent_output / "input-taps.txt")
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "1"
            persistent_command = command[:-1] + [str(persistent_output)]
            persistent_anr = subprocess.run(
                persistent_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(persistent_anr.returncode, 1)
            self.assertIn("persisted after two bounded Wait attempts", persistent_anr.stderr)
            self.assertEqual(
                (persistent_output / "input-taps.txt").read_text(encoding="utf-8").splitlines(),
                ["540 1087", "540 1087"],
            )
            self.assertFalse((persistent_output / "result.json").exists())

            # Never tap a system dialog control unless the hierarchy proves it is
            # enabled and clickable; fail closed without interacting with a disabled control.
            disabled_wait_output = root / "anr-disabled-wait-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(disabled_wait_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(disabled_wait_output / "input-taps.txt")
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "1"
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "1"
            disabled_wait_command = command[:-1] + [str(disabled_wait_output)]
            disabled_wait = subprocess.run(
                disabled_wait_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(disabled_wait.returncode, 1)
            self.assertIn("safe Wait control could not be resolved", disabled_wait.stderr)
            self.assertFalse((disabled_wait_output / "input-taps.txt").exists())
            self.assertFalse((disabled_wait_output / "result.json").exists())

            # A different app/system ANR must never be dismissed as a launcher issue.
            other_anr_output = root / "other-anr-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(other_anr_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(other_anr_output / "input-taps.txt")
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "0"
            environment["FAKE_OTHER_ANR_ALWAYS"] = "1"
            other_anr_command = command[:-1] + [str(other_anr_output)]
            other_anr = subprocess.run(
                other_anr_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(other_anr.returncode, 1)
            self.assertIn("Unexpected Android app/system ANR dialog remains", other_anr.stderr)
            self.assertFalse((other_anr_output / "input-taps.txt").exists())
            self.assertFalse((other_anr_output / "result.json").exists())

            environment["FAKE_OTHER_ANR_ALWAYS"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "0"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(output / "input-taps.txt")
            environment["FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS"] = "0"
            environment["FAKE_RESTORE_ERROR"] = "1"
            stale_session_error = subprocess.run(
                command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(stale_session_error.returncode, 1)
            self.assertIn("unexpected session/storage error", stale_session_error.stderr)

            environment["FAKE_RESTORE_ERROR"] = "0"
            environment["FAKE_TARGET_EXCEPTION"] = "1"
            target_exception = subprocess.run(
                command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(target_exception.returncode, 1)
            self.assertIn("React Native reported an unhandled startup exception", target_exception.stderr)

            # A permanently corrupt SDK archive must stop before AVD creation or app install.
            system_image_dir = sdk / "system-images" / "android-35" / "google_apis" / "x86_64"
            shutil.rmtree(system_image_dir)
            attempts_file.unlink()
            failed_avd_home = root / "failed-avd-home"
            failure_output = root / "failed-output"
            environment["ANDROID_AVD_HOME"] = str(failed_avd_home)
            environment["FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS"] = "3"
            failure_command = command[:-1] + [str(failure_output)]
            repeated_archive_failure = subprocess.run(
                failure_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(repeated_archive_failure.returncode, 1)
            self.assertIn("failed after 3 attempts", repeated_archive_failure.stderr)
            self.assertEqual(attempts_file.read_text(encoding="utf-8").strip(), "3")
            self.assertFalse((failed_avd_home / "turnova-release-smoke.ini").exists())
            self.assertFalse(system_image_dir.exists())
            self.assertFalse((failure_output / "result.json").exists())

    def test_invalid_package_identifier_is_rejected_before_sdk_access(self) -> None:
        with tempfile.TemporaryDirectory(prefix="turnova-invalid-package-") as temporary:
            root = Path(temporary)
            apk = root / "fixture.apk"
            apk.write_bytes(b"fixture")
            result = subprocess.run(
                [
                    "bash",
                    str(SMOKE_SCRIPT),
                    "--apk",
                    str(apk),
                    "--expected-id",
                    "com.turnova;rm",
                ],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
            self.assertEqual(result.returncode, 2)
            self.assertIn("malformed", result.stderr.lower())


if __name__ == "__main__":
    unittest.main()

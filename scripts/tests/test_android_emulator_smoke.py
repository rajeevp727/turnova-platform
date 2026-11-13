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
    def test_launch_status_check_avoids_pipefail_sigpipe_false_failure(self) -> None:
        script = SMOKE_SCRIPT.read_text(encoding="utf-8")

        # grep -q may close a pipe as soon as it finds the match. With pipefail,
        # printf can then fail with SIGPIPE even though the expected text exists.
        self.assertIn('if [[ "$launch_output" != *"Status: ok"* ]]; then', script)
        self.assertNotRegex(
            script,
            r'printf\\s+[^\\n]*"\\$launch_output"[^\\n]*\\|\\s*grep\\s+-Fq\\s+"Status: ok"',
        )

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
    printf 'global adb emu kill\n' >> "$FAKE_ADB_EMU_KILL_LOG"
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
          if [[ "${3:-} ${4:-}" == "540 961" && -n "${FAKE_SYSTEM_ANR_CLOSED_FILE:-}" ]]; then
            printf 'closed\n' > "$FAKE_SYSTEM_ANR_CLOSED_FILE"
          fi
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
        closed=false
        if [[ -n "${FAKE_SYSTEM_ANR_CLOSED_FILE:-}" && -f "$FAKE_SYSTEM_ANR_CLOSED_FILE" ]]; then closed=true; fi
        title=""
        if [[ "${FAKE_OTHER_ANR_ALWAYS:-0}" == "1" ]]; then
          title="TurnOva Rider isn't responding"
        elif [[ "${FAKE_UNKNOWN_SYSTEM_ANR_ALWAYS:-0}" == "1" ]]; then
          title="Android System isn't responding"
        elif [[ "${FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE:-0}" == "1" && "$closed" != true && "$dump_count" -le 2 ]]; then
          title="com.google.android.googlesdksetup isn't responding"
        elif [[ "${FAKE_PIXEL_LAUNCHER_ANR_ALWAYS:-0}" == "1" ]]; then
          title="Pixel Launcher isn't responding"
        elif [[ "${FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE:-0}" == "1" && "$closed" != true && "$dump_count" -le 2 ]]; then
          title="Pixel Launcher isn't responding"
        elif [[ "${FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP:-0}" == "1" && "$dump_count" == "1" ]]; then
          title="Pixel Launcher isn't responding"
        fi
        if [[ -n "$title" ]]; then
          if [[ "${FAKE_PIXEL_LAUNCHER_WAIT_DISABLED:-0}" == "1" ]]; then
            wait_state='clickable="false" enabled="false"'
          else
            wait_state='clickable="true" enabled="true"'
          fi
          if [[ "${FAKE_SYSTEM_ANR_CLOSE_DISABLED:-0}" == "1" ]]; then
            close_state='clickable="false" enabled="false"'
          else
            close_state='clickable="true" enabled="true"'
          fi
          printf '%s\n' "<hierarchy><node resource-id=\"android:id/alertTitle\" text=\"$title\"/><node resource-id=\"android:id/aerr_close\" text=\"Close app\" bounds=\"[70,898][1010,1024]\" ${close_state}/><node resource-id=\"android:id/aerr_wait\" text=\"Wait\" bounds=\"[70,1024][1010,1150]\" ${wait_state}/></hierarchy>"
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
      dumpsys)
        if [[ "${FAKE_APP_NOT_FOREGROUND:-0}" == "1" ]]; then
          printf 'mResumedActivity: ActivityRecord { com.google.android.apps.nexuslauncher/.NexusLauncherActivity }\n* Hist #0: com.turnova.rider/.MainActivity\n'
        else
          echo "mResumedActivity: ActivityRecord { com.turnova.rider/.MainActivity }"
        fi
        ;;
      *) echo "unexpected adb shell arguments: $*" >&2; exit 2 ;;
    esac
    ;;
  logcat)
    if [[ "${FAKE_TARGET_ANR:-0}" == "1" ]]; then
      printf '2026-11-09 11:00:00.000 4242 4242 E ActivityManager: ANR in com.turnova.rider\n'
    elif [[ "$FAKE_TARGET_EXCEPTION" == "1" ]]; then
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
                    "CI": "true",
                    "ANDROID_SDK_ROOT": str(sdk),
                    "ANDROID_HOME": str(sdk),
                    "ANDROID_AVD_HOME": str(avd_home),
                    "RUNNER_TEMP": str(root / "runner-temp"),
                    "FAKE_TARGET_EXCEPTION": "0",
                    "FAKE_RESTORE_ERROR": "0",
                    "FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP": "0",
                    "FAKE_PIXEL_LAUNCHER_ANR_ALWAYS": "0",
                    "FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE": "0",
                    "FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE": "0",
                    "FAKE_PIXEL_LAUNCHER_WAIT_DISABLED": "0",
                    "FAKE_SYSTEM_ANR_CLOSE_DISABLED": "0",
                    "FAKE_UNKNOWN_SYSTEM_ANR_ALWAYS": "0",
                    "FAKE_APP_NOT_FOREGROUND": "0",
                    "FAKE_OTHER_ANR_ALWAYS": "0",
                    "FAKE_TARGET_ANR": "0",
                    "FAKE_SYSTEM_ANR_CLOSED_FILE": str(output / "system-anr-closed"),
                    "FAKE_UI_DUMP_COUNT_FILE": str(output / "ui-dump-count"),
                    "FAKE_INPUT_LOG": str(output / "input-taps.txt"),
                    "FAKE_ADB_EMU_KILL_LOG": str(output / "global-adb-emu-kill.txt"),
                    "FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS": "1",
                    "TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS": "0",
                }
            )

            # Existing AVDs in both explicit and conventional homes must survive.
            custom_existing_dir = avd_home / "turnova-release-smoke-existing.avd"
            custom_existing_dir.mkdir(parents=True)
            custom_avd_ini = avd_home / "turnova-release-smoke-existing.ini"
            custom_avd_ini.write_text("developer AVD must remain untouched", encoding="utf-8")
            custom_avd_sentinel = custom_existing_dir / "user-data.txt"
            custom_avd_sentinel.write_text("preserve", encoding="utf-8")
            user_avd_home = home / ".android" / "avd"
            user_avd_dir = user_avd_home / "turnova-release-smoke-existing.avd"
            user_avd_dir.mkdir(parents=True)
            user_avd_ini = user_avd_home / "turnova-release-smoke-existing.ini"
            user_avd_ini.write_text("developer AVD must remain untouched", encoding="utf-8")
            user_avd_sentinel = user_avd_dir / "user-data.txt"
            user_avd_sentinel.write_text("preserve", encoding="utf-8")

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
            create_log = (output / "avdmanager-create.log").read_text(encoding="utf-8")
            self.assertIn("Created turnova-release-smoke-", create_log)
            self.assertIn(str(avd_home), create_log)
            created_avd_name = create_log.split("Created ", 1)[1].split(" in ", 1)[0]
            self.assertFalse((avd_home / f"{created_avd_name}.ini").exists())
            self.assertFalse((avd_home / f"{created_avd_name}.avd").exists())
            self.assertTrue(custom_avd_ini.is_file())
            self.assertEqual(custom_avd_sentinel.read_text(encoding="utf-8"), "preserve")
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
            self.assertFalse((output / "global-adb-emu-kill.txt").exists())
            self.assertTrue(user_avd_ini.is_file())
            self.assertEqual(user_avd_sentinel.read_text(encoding="utf-8"), "preserve")

            # A complete system image that predates this invocation is reused as-is.
            # The retry cleanup must never delete or reinstall a user's existing SDK package.
            system_image_dir = sdk / "system-images" / "android-35" / "google_apis" / "x86_64"
            system_image_marker = system_image_dir / "user-package-sentinel.txt"
            system_image_marker.write_text("preserve-existing-sdk-image", encoding="utf-8")
            attempts_before_reuse = int(attempts_file.read_text(encoding="utf-8").strip())
            reuse_output = root / "reuse-existing-system-image-output"
            reuse_command = command[:-1] + [str(reuse_output)]
            reused_existing_image = subprocess.run(
                reuse_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(
                reused_existing_image.returncode,
                0,
                msg=f"stdout:\\n{reused_existing_image.stdout}\\nstderr:\\n{reused_existing_image.stderr}",
            )
            self.assertIn("pre-existing Android API 35 system image", reused_existing_image.stdout)
            self.assertEqual(int(attempts_file.read_text(encoding="utf-8").strip()), attempts_before_reuse)
            self.assertEqual(system_image_marker.read_text(encoding="utf-8"), "preserve-existing-sdk-image")

            # An incomplete image that predates the invocation is not treated as a
            # partial download owned by this script; fail closed and preserve it.
            shutil.rmtree(system_image_dir)
            system_image_dir.mkdir(parents=True)
            (system_image_dir / "package.xml").write_text("<localPackage/>", encoding="utf-8")
            incomplete_sentinel = system_image_dir / "user-data.txt"
            incomplete_sentinel.write_text("preserve-incomplete-user-package", encoding="utf-8")
            attempts_before_incomplete = int(attempts_file.read_text(encoding="utf-8").strip())
            incomplete_output = root / "incomplete-existing-system-image-output"
            incomplete_command = command[:-1] + [str(incomplete_output)]
            incomplete_existing_image = subprocess.run(
                incomplete_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(incomplete_existing_image.returncode, 1)
            self.assertIn("refusing to modify or remove it", incomplete_existing_image.stderr)
            self.assertEqual(int(attempts_file.read_text(encoding="utf-8").strip()), attempts_before_incomplete)
            self.assertEqual(incomplete_sentinel.read_text(encoding="utf-8"), "preserve-incomplete-user-package")
            self.assertFalse(incomplete_output.joinpath("result.json").exists())
            shutil.rmtree(system_image_dir)

            # With no explicit ANDROID_AVD_HOME, use an isolated RUNNER_TEMP path
            # instead of the developer's conventional ~/.android/avd directory.
            isolated_runner_temp = root / "isolated-runner-temp"
            isolated_output = root / "isolated-default-avd-output"
            environment.pop("ANDROID_AVD_HOME")
            environment["RUNNER_TEMP"] = str(isolated_runner_temp)
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(isolated_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(isolated_output / "input-taps.txt")
            isolated_command = command[:-1] + [str(isolated_output)]
            isolated_result = subprocess.run(
                isolated_command,
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(
                isolated_result.returncode,
                0,
                msg=f"stdout:\n{isolated_result.stdout}\nstderr:\n{isolated_result.stderr}",
            )
            isolated_create_log = (isolated_output / "avdmanager-create.log").read_text(encoding="utf-8")
            self.assertIn(str(isolated_runner_temp / "turnova-android-avd"), isolated_create_log)
            self.assertTrue(user_avd_ini.is_file())
            self.assertEqual(user_avd_sentinel.read_text(encoding="utf-8"), "preserve")
            environment["ANDROID_AVD_HOME"] = str(avd_home)
            environment["RUNNER_TEMP"] = str(root / "runner-temp")
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(output / "input-taps.txt")

            # A transient allow-listed Pixel Launcher ANR is handled through one bounded Wait.
            recovered_output = root / "anr-recovered-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(recovered_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(recovered_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(recovered_output / "system-anr-closed")
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "1"
            recovered_command = command[:-1] + [str(recovered_output)]
            recovered_anr = subprocess.run(
                recovered_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(recovered_anr.returncode, 0, msg=f"stdout:\n{recovered_anr.stdout}\nstderr:\n{recovered_anr.stderr}")
            recovered_report = json.loads((recovered_output / "result.json").read_text(encoding="utf-8"))
            self.assertEqual(recovered_report["systemUiRecovery"]["pixelLauncherAnrWaitAttempts"], 1)
            self.assertTrue(recovered_report["systemUiRecovery"]["pixelLauncherAnrRecovered"])
            self.assertIn("com.google.android.apps.nexuslauncher", recovered_report["systemUiRecovery"]["systemAnrPackagesRecovered"])
            self.assertIn("540 1087", (recovered_output / "input-taps.txt").read_text(encoding="utf-8"))
            self.assertTrue((recovered_output / "system-anr-1.xml").is_file())
            recovered_ui = (recovered_output / "ui-hierarchy.xml").read_text(encoding="utf-8")
            for expected_text in ("Welcome back", "Email address", "Password", "Sign in"):
                self.assertIn(expected_text, recovered_ui)

            # A persistent launcher ANR is closed only while the target app remains
            # foreground and stable, then the login surface is verified.
            close_output = root / "anr-close-recovered-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(close_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(close_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(close_output / "system-anr-closed")
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "1"
            close_command = command[:-1] + [str(close_output)]
            close_recovered = subprocess.run(
                close_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(close_recovered.returncode, 0, msg=f"stdout:\n{close_recovered.stdout}\nstderr:\n{close_recovered.stderr}")
            close_report = json.loads((close_output / "result.json").read_text(encoding="utf-8"))
            self.assertEqual(close_report["systemUiRecovery"]["systemAnrPackagesClosed"], ["com.google.android.apps.nexuslauncher"])
            self.assertTrue(close_report["systemUiRecovery"]["targetAppRemainedForegroundAndStable"])
            self.assertEqual(
                (close_output / "input-taps.txt").read_text(encoding="utf-8").splitlines(),
                ["540 1087", "540 961"],
            )

            # The second observed system ANR is Google SDK Setup. It receives the same
            # bounded Wait/Close treatment only while TurnOva remains foreground.
            google_output = root / "google-setup-anr-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(google_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(google_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(google_output / "system-anr-closed")
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "0"
            environment["FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE"] = "1"
            google_command = command[:-1] + [str(google_output)]
            google_recovered = subprocess.run(
                google_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(google_recovered.returncode, 0, msg=f"stdout:\n{google_recovered.stdout}\nstderr:\n{google_recovered.stderr}")
            google_report = json.loads((google_output / "result.json").read_text(encoding="utf-8"))
            self.assertEqual(google_report["systemUiRecovery"]["systemAnrPackagesClosed"], ["com.google.android.googlesdksetup"])
            self.assertIn("com.google.android.googlesdksetup isn't responding", google_report["systemUiRecovery"]["systemAnrTitlesRecovered"])

            # A system ANR that persists after bounded Wait and Close fails closed.
            persistent_output = root / "anr-persistent-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(persistent_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(persistent_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(persistent_output / "system-anr-closed")
            environment["FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "1"
            persistent_command = command[:-1] + [str(persistent_output)]
            persistent_anr = subprocess.run(
                persistent_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(persistent_anr.returncode, 1)
            self.assertIn("remained after bounded Wait/Close recovery", persistent_anr.stderr)
            self.assertEqual(
                (persistent_output / "input-taps.txt").read_text(encoding="utf-8").splitlines(),
                ["540 1087", "540 961"],
            )
            self.assertFalse((persistent_output / "result.json").exists())

            # A disabled Close control is never tapped after the bounded Wait.
            disabled_close_output = root / "anr-disabled-close-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(disabled_close_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(disabled_close_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(disabled_close_output / "system-anr-closed")
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "1"
            environment["FAKE_SYSTEM_ANR_CLOSE_DISABLED"] = "1"
            disabled_close_command = command[:-1] + [str(disabled_close_output)]
            disabled_close = subprocess.run(
                disabled_close_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(disabled_close.returncode, 1)
            self.assertIn("safe Close app control could not be resolved", disabled_close.stderr)
            self.assertEqual(
                (disabled_close_output / "input-taps.txt").read_text(encoding="utf-8").splitlines(),
                ["540 1087"],
            )
            self.assertFalse((disabled_close_output / "result.json").exists())

            # A disabled Wait control must fail closed before Close is considered.
            disabled_wait_output = root / "anr-disabled-wait-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(disabled_wait_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(disabled_wait_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(disabled_wait_output / "system-anr-closed")
            environment["FAKE_SYSTEM_ANR_CLOSE_DISABLED"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "1"
            disabled_wait_command = command[:-1] + [str(disabled_wait_output)]
            disabled_wait = subprocess.run(
                disabled_wait_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(disabled_wait.returncode, 1)
            self.assertIn("no safe enabled Wait control", disabled_wait.stderr)
            self.assertFalse((disabled_wait_output / "input-taps.txt").exists())
            self.assertFalse((disabled_wait_output / "result.json").exists())
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "0"

            # A target-app ANR and an unknown system ANR are never dismissed.
            for scenario, target_flag, unknown_flag, expected in (
                ("app-anr-output", "1", "0", "TurnOva Rider isn't responding"),
                ("unknown-system-anr-output", "0", "1", "Android System isn't responding"),
            ):
                other_output = root / scenario
                environment["FAKE_UI_DUMP_COUNT_FILE"] = str(other_output / "ui-dump-count")
                environment["FAKE_INPUT_LOG"] = str(other_output / "input-taps.txt")
                environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(other_output / "system-anr-closed")
                environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "0"
                environment["FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE"] = "0"
                environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "0"
                environment["FAKE_SYSTEM_ANR_CLOSE_DISABLED"] = "0"
                environment["FAKE_OTHER_ANR_ALWAYS"] = target_flag
                environment["FAKE_UNKNOWN_SYSTEM_ANR_ALWAYS"] = unknown_flag
                other_command = command[:-1] + [str(other_output)]
                other_anr = subprocess.run(
                    other_command, cwd=REPO_ROOT, env=environment,
                    capture_output=True, text=True, timeout=30, check=False,
                )
                self.assertEqual(other_anr.returncode, 1)
                self.assertIn(f"Unexpected Android app/system ANR dialog: {expected}", other_anr.stderr)
                self.assertFalse((other_output / "input-taps.txt").exists())
                self.assertFalse((other_output / "result.json").exists())

            # System UI recovery is forbidden unless TurnOva remains the resumed
            # foreground activity with the same process ID.
            foreground_output = root / "app-not-foreground-output"
            environment["FAKE_UI_DUMP_COUNT_FILE"] = str(foreground_output / "ui-dump-count")
            environment["FAKE_INPUT_LOG"] = str(foreground_output / "input-taps.txt")
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(foreground_output / "system-anr-closed")
            environment["FAKE_APP_NOT_FOREGROUND"] = "1"
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "1"
            foreground_command = command[:-1] + [str(foreground_output)]
            foreground_result = subprocess.run(
                foreground_command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(foreground_result.returncode, 1)
            self.assertIn("Target app is not the resumed foreground activity", foreground_result.stderr)
            self.assertFalse((foreground_output / "input-taps.txt").exists())
            self.assertFalse((foreground_output / "result.json").exists())
            environment["FAKE_APP_NOT_FOREGROUND"] = "0"

            environment["FAKE_OTHER_ANR_ALWAYS"] = "0"
            environment["FAKE_UNKNOWN_SYSTEM_ANR_ALWAYS"] = "0"
            environment["FAKE_SYSTEM_ANR_CLOSE_DISABLED"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_WAIT_DISABLED"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_FIRST_DUMP"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_ALWAYS"] = "0"
            environment["FAKE_PIXEL_LAUNCHER_ANR_WAIT_THEN_CLOSE"] = "0"
            environment["FAKE_GOOGLE_SETUP_ANR_WAIT_THEN_CLOSE"] = "0"
            environment["FAKE_SYSTEM_ANR_CLOSED_FILE"] = str(output / "system-anr-closed")
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
                command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(target_exception.returncode, 1)
            self.assertIn("React Native reported an unhandled startup exception", target_exception.stderr)

            environment["FAKE_TARGET_EXCEPTION"] = "0"
            environment["FAKE_TARGET_ANR"] = "1"
            target_anr = subprocess.run(
                command, cwd=REPO_ROOT, env=environment,
                capture_output=True, text=True, timeout=30, check=False,
            )
            self.assertEqual(target_anr.returncode, 1)
            self.assertIn("Android runtime recorded an ANR for the TurnOva app", target_anr.stderr)
            environment["FAKE_TARGET_ANR"] = "0"

            # A permanently corrupt SDK archive created by this invocation must stop
            # before AVD creation or app install after cleaning only its own partial files.
            if system_image_dir.exists():
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
            self.assertFalse(list(failed_avd_home.glob("turnova-release-smoke-*.ini")))
            self.assertFalse(list(failed_avd_home.glob("turnova-release-smoke-*.avd")))
            self.assertFalse(system_image_dir.exists())
            self.assertFalse((failure_output / "result.json").exists())

    def test_refuses_to_modify_an_android_sdk_outside_ci(self) -> None:
        with tempfile.TemporaryDirectory(prefix="turnova-android-non-ci-") as temporary:
            root = Path(temporary)
            apk = root / "fixture.apk"
            apk.write_bytes(b"fixture")
            output = root / "output"
            environment = os.environ.copy()
            environment.pop("CI", None)

            result = subprocess.run(
                [
                    "bash",
                    str(SMOKE_SCRIPT),
                    "--apk",
                    str(apk),
                    "--expected-id",
                    "com.turnova.rider",
                    "--output-dir",
                    str(output),
                ],
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )

            self.assertEqual(result.returncode, 2)
            self.assertIn("may run only on a CI runner", result.stderr)
            self.assertFalse(output.exists())

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

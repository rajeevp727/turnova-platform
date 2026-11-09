#!/usr/bin/env python3
"""Deterministic harness tests for the Android release-APK emulator smoke."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import shutil
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
      if (( attempts <= ${FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS:-0} )); then
        echo "Warning: Google APIs system image download failed: ZipFile unknown archive" >&2
        exit 1
      fi
      package_dir="$ANDROID_SDK_ROOT/system-images/android-35/google_apis/x86_64"
      mkdir -p "$package_dir"
      printf '<localPackage path="system-images;android-35;google_apis;x86_64"/>\n' > "$package_dir/package.xml"
      printf 'deterministic-system-image-fixture' > "$package_dir/system.img"
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
      settings|input) exit 0 ;;
      uiautomator) echo "UI hierarchy dumped to: /sdcard/turnova-ui-hierarchy.xml" ;;
      cat)
        if [[ "${FAKE_RESTORE_ERROR:-0}" == "1" ]]; then
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
                    "FAKE_RESTORE_ERROR": "0",\n                    "FAKE_SYSTEM_IMAGE_FAIL_ATTEMPTS": "1",\n                    "TURNOVA_ANDROID_SYSTEM_IMAGE_RETRY_DELAY_SECONDS": "0",
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

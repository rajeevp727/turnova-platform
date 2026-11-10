#!/usr/bin/env python3
"""Deterministic harness tests for the iOS Simulator release smoke."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SMOKE_SCRIPT = REPO_ROOT / "scripts" / "ios-simulator-smoke.sh"


def write_executable(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    path.chmod(0o755)


class IOSSimulatorSmokeHarnessTests(unittest.TestCase):
    def run_smoke(
        self,
        restore_error: bool = True,
        restore_error_without_your: bool = False,
        adhoc_signature: bool = False,
        stale_session_error: bool = False,
        bootstatus_fail: bool = False,
        bootstatus_sleep: bool = False,
        boot_timeout_seconds: int | None = None,
        erase_sleep: bool = False,
        boot_sleep: bool = False,
        operation_timeout_seconds: int | None = None,
        install_fail: bool = False,
        launch_fail: bool = False,
        process_unstable: bool = False,
        ocr_not_ready_attempts: int = 0,
        ocr_never_ready: bool = False,
        login_ready_timeout_seconds: int | None = None,
        login_ready_poll_interval_seconds: int | None = None,
        ci: bool = True,
    ) -> tuple[subprocess.CompletedProcess[str], Path, list[str]]:
        temporary = tempfile.TemporaryDirectory(prefix="turnova-ios-smoke-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        app_bundle = root / "TurnOva.app"
        app_bundle.mkdir()
        (app_bundle / "Info.plist").write_text("fixture plist", encoding="utf-8")
        app_container = root / "installed-app"
        app_container.mkdir()
        bin_dir = root / "bin"
        output = root / "output"
        xcrun_log = root / "xcrun.log"
        plist_buddy = bin_dir / "PlistBuddy"

        write_executable(
            bin_dir / "xcrun",
            r"""#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == "simctl" ]] || exit 2
shift
printf '%s\n' "$*" >> "$FAKE_XCRUN_LOG"
case "$1" in
  list)
    cat <<'JSON'
{"devices":{"com.apple.CoreSimulator.SimRuntime.iOS-18-0":[{"name":"iPhone 16","udid":"TEST-DEVICE","isAvailable":true,"state":"Booted"}]}}
JSON
    ;;
  shutdown|erase|boot)
    case "$1" in
      erase) [[ "${FAKE_ERASE_SLEEP:-0}" != "1" ]] || exec sleep 3 ;;
      boot) [[ "${FAKE_BOOT_SLEEP:-0}" != "1" ]] || exec sleep 3 ;;
    esac
    exit 0
    ;;
  install)
    [[ "${FAKE_INSTALL_FAIL:-0}" != "1" ]] || {
      echo "simctl install fixture failure" >&2
      exit 1
    }
    exit 0
    ;;
  bootstatus)
    [[ "$*" == "bootstatus TEST-DEVICE -b" ]] || {
      echo "bootstatus command arguments were unexpected: $*" >&2
      exit 2
    }
    [[ "${FAKE_BOOTSTATUS_FAIL:-0}" != "1" ]] || exit 1
    [[ "${FAKE_BOOTSTATUS_SLEEP:-0}" != "1" ]] || exec sleep 3
    ;;
  get_app_container)
    echo "$FAKE_APP_CONTAINER"
    ;;
  launch)
    [[ "${FAKE_LAUNCH_FAIL:-0}" != "1" ]] || {
      echo "simctl launch fixture failure" >&2
      exit 1
    }
    echo "$FAKE_BUNDLE_ID: 4242"
    ;;
  spawn)
    case "$3:$4" in
      log:show)
        echo "SpringBoard launch denied for $FAKE_BUNDLE_ID"
        ;;
      launchctl:list)
        if [[ "${FAKE_PROCESS_UNSTABLE:-0}" == "1" ]]; then
          echo "com.apple.springboard"
        else
          echo "$FAKE_BUNDLE_ID"
        fi
        ;;
      launchctl:procinfo)
        echo "process not found" >&2
        exit 1
        ;;
      *)
        echo "unexpected simctl spawn arguments: $*" >&2
        exit 2
        ;;
    esac
    ;;
  listapps)
    echo "Installed app: $FAKE_BUNDLE_ID"
    ;;
  io)
    printf '\211PNG\r\n\032\n' > "$4"
    ;;
  *)
    echo "unexpected simctl arguments: $*" >&2
    exit 2
    ;;
esac
""",
        )
        write_executable(
            plist_buddy,
            r"""#!/usr/bin/env bash
set -euo pipefail
echo "$FAKE_BUNDLE_ID"
""",
        )
        write_executable(
            bin_dir / "codesign",
            r"""#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  -dv)
    if [[ "$FAKE_ADHOC_SIGNATURE" == "1" ]]; then
      echo "Signature=adhoc"
    else
      echo "code object is not signed at all" >&2
      exit 1
    fi
    ;;
  -d)
    echo "code object is not signed at all" >&2
    exit 1
    ;;
  *) echo "unexpected codesign arguments: $*" >&2; exit 2 ;;
esac
""",
        )
        write_executable(
            bin_dir / "swift",
            r"""#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" != *"--expect-secure-store-unavailable"* ]]; then
  echo "expected SecureStore-unavailable OCR mode was not requested" >&2
  exit 2
fi
count=0
if [[ -f "$FAKE_OCR_COUNT_FILE" ]]; then
  count="$(cat "$FAKE_OCR_COUNT_FILE")"
fi
count=$((count + 1))
printf '%s\n' "$count" > "$FAKE_OCR_COUNT_FILE"
if [[ "$FAKE_OCR_NEVER_READY" == "1" ]] || (( count <= FAKE_OCR_NOT_READY_ATTEMPTS )); then
  echo "SpringBoard home screen or loading placeholder"
  echo "iOS login screen is not ready yet" >&2
  exit 3
fi
echo "Welcome back Email address Password Sign in"
if [[ "$FAKE_RESTORE_ERROR" == "1" ]]; then
  if [[ "$FAKE_RESTORE_ERROR_WITHOUT_YOUR" == "1" ]]; then
    printf "Unable to restore\\nsession. Check your connection or secure storage and retry.\\n"
  else
    printf "Unable to restore your\\nsession. Check your connection or secure storage and retry.\\n"
  fi
fi
if [[ "$FAKE_RESTORE_ERROR" != "1" ]]; then
  echo "Expected unsigned-simulator SecureStore failure banner was not visible." >&2
  exit 3
fi
if [[ "$FAKE_STALE_SESSION" == "1" ]]; then
  printf "Session\\nexpired. Please sign in again.\\n"
  exit 4
fi
""",
        )

        environment = os.environ.copy()
        environment.update(
            {
                "PATH": f"{bin_dir}:{os.environ.get('PATH', '')}",
                "CI": "true",
                "PLIST_BUDDY": str(plist_buddy),
                "FAKE_XCRUN_LOG": str(xcrun_log),
                "FAKE_APP_CONTAINER": str(app_container),
                "FAKE_BUNDLE_ID": "com.turnova.rider",
                "FAKE_RESTORE_ERROR": "1" if restore_error else "0",
                "FAKE_RESTORE_ERROR_WITHOUT_YOUR": "1" if restore_error_without_your else "0",
                "FAKE_ADHOC_SIGNATURE": "1" if adhoc_signature else "0",
                "FAKE_STALE_SESSION": "1" if stale_session_error else "0",
                "FAKE_BOOTSTATUS_FAIL": "1" if bootstatus_fail else "0",
                "FAKE_BOOTSTATUS_SLEEP": "1" if bootstatus_sleep else "0",
                "FAKE_ERASE_SLEEP": "1" if erase_sleep else "0",
                "FAKE_BOOT_SLEEP": "1" if boot_sleep else "0",
                "FAKE_INSTALL_FAIL": "1" if install_fail else "0",
                "FAKE_LAUNCH_FAIL": "1" if launch_fail else "0",
                "FAKE_PROCESS_UNSTABLE": "1" if process_unstable else "0",
                "FAKE_OCR_COUNT_FILE": str(output / "ocr-count.txt"),
                "FAKE_OCR_NOT_READY_ATTEMPTS": str(ocr_not_ready_attempts),
                "FAKE_OCR_NEVER_READY": "1" if ocr_never_ready else "0",
                "RUNNER_TEMP": str(root / "runner-temp"),
            }
        )
        if not ci:
            environment.pop("CI", None)
        if boot_timeout_seconds is not None:
            environment["TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS"] = str(boot_timeout_seconds)
        if operation_timeout_seconds is not None:
            environment["TURNOVA_SIMULATOR_OPERATION_TIMEOUT_SECONDS"] = str(operation_timeout_seconds)
        if login_ready_timeout_seconds is not None:
            environment["TURNOVA_IOS_LOGIN_READY_TIMEOUT_SECONDS"] = str(login_ready_timeout_seconds)
        if login_ready_poll_interval_seconds is not None:
            environment["TURNOVA_IOS_LOGIN_READY_POLL_INTERVAL_SECONDS"] = str(login_ready_poll_interval_seconds)

        command = [
            "bash",
            str(SMOKE_SCRIPT),
            "--app",
            str(app_bundle),
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
        commands = xcrun_log.read_text(encoding="utf-8").splitlines() if xcrun_log.exists() else []
        return result, output, commands

    def test_erases_simulator_before_install_and_reports_expected_unsigned_secure_store_failure(self) -> None:
        result, output, commands = self.run_smoke()

        self.assertEqual(result.returncode, 0, msg=f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        shutdown_index = next(i for i, item in enumerate(commands) if item.startswith("shutdown "))
        erase_index = next(i for i, item in enumerate(commands) if item.startswith("erase "))
        boot_index = next(i for i, item in enumerate(commands) if item.startswith("boot "))
        install_index = next(i for i, item in enumerate(commands) if item.startswith("install "))
        self.assertLess(shutdown_index, erase_index)
        self.assertLess(erase_index, boot_index)
        self.assertLess(boot_index, install_index)
        bootstatus_index = next(i for i, item in enumerate(commands) if item.startswith("bootstatus "))
        self.assertLess(boot_index, bootstatus_index)
        self.assertLess(bootstatus_index, install_index)
        self.assertEqual(commands[bootstatus_index], "bootstatus TEST-DEVICE -b")

        report = json.loads((output / "result.json").read_text(encoding="utf-8"))
        self.assertEqual(report["bundleId"], "com.turnova.rider")
        self.assertEqual(report["install"], "passed")
        self.assertEqual(report["launch"], "passed")
        self.assertTrue(report["processStableAfterStartup"])
        self.assertTrue(report["simulatorDataErased"])
        self.assertTrue(report["loginScreenTextVerified"])
        self.assertFalse(report["secureStoreAvailable"])
        self.assertTrue(report["secureStoreUnavailableBannerVerified"])
        self.assertTrue(report["staleSessionExpiredErrorAbsent"])
        self.assertEqual(report["productionApiOrAuthentication"], "not exercised")
        self.assertEqual(report["signing"], "unsigned simulator artifact (CI-only)")
        self.assertEqual(report["keychainAccessGroup"], "not available; signed-device validation required")
        self.assertGreater((output / "launch.png").stat().st_size, 0)

    def test_rider_secure_store_banner_copy_without_your_is_accepted(self) -> None:
        result, output, _ = self.run_smoke(restore_error_without_your=True)

        self.assertEqual(result.returncode, 0, msg=f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        report = json.loads((output / "result.json").read_text(encoding="utf-8"))
        self.assertTrue(report["secureStoreUnavailableBannerVerified"])

    def test_ocr_normalizes_line_wrapped_forbidden_session_errors(self) -> None:
        ocr_source = (REPO_ROOT / "scripts" / "ios-simulator-screen-text-check.swift").read_text(encoding="utf-8")

        normalization = ocr_source.index('replacingOccurrences(of: "\\\\s+", with: " ", options: .regularExpression)')
        forbidden_checks = ocr_source.index("let unexpectedText = forbiddenText.filter")
        self.assertIn('"Session expired"', ocr_source)
        self.assertLess(normalization, forbidden_checks)

    def test_ocr_matcher_contains_both_rider_and_driver_banner_variants(self) -> None:
        ocr_source = (REPO_ROOT / "scripts" / "ios-simulator-screen-text-check.swift").read_text(encoding="utf-8")

        self.assertIn('"Unable to restore your session"', ocr_source)
        self.assertIn('"Unable to restore session"', ocr_source)

    def test_waits_for_login_ui_after_slow_cold_start(self) -> None:
        result, output, commands = self.run_smoke(ocr_not_ready_attempts=2)

        self.assertEqual(result.returncode, 0, msg=f"stdout:\\n{result.stdout}\\nstderr:\\n{result.stderr}")
        self.assertEqual((output / "ocr-count.txt").read_text(encoding="utf-8").strip(), "3")
        screenshot_commands = [item for item in commands if item.startswith("io TEST-DEVICE screenshot ")]
        self.assertEqual(len(screenshot_commands), 3)
        report = json.loads((output / "result.json").read_text(encoding="utf-8"))
        self.assertTrue(report["loginScreenTextVerified"])

    def test_login_readiness_timeout_captures_diagnostics_and_fails_closed(self) -> None:
        result, output, commands = self.run_smoke(
            ocr_never_ready=True,
            login_ready_timeout_seconds=1,
            login_ready_poll_interval_seconds=1,
        )

        self.assertEqual(result.returncode, 1)
        self.assertIn("did not reach a stable login screen within 1 seconds", result.stderr)
        self.assertIn("spawn TEST-DEVICE log show ", " ".join(commands))
        self.assertTrue((output / "simulator-system.log").is_file())
        self.assertTrue((output / "launchctl-list-failure.txt").is_file())
        self.assertTrue((output / "installed-apps.txt").is_file())
        self.assertFalse((output / "result.json").exists())

    def test_missing_expected_secure_store_failure_fails_the_smoke(self) -> None:
        result, output, _ = self.run_smoke(
            restore_error=False,
            login_ready_timeout_seconds=1,
            login_ready_poll_interval_seconds=1,
        )

        self.assertEqual(result.returncode, 1)
        self.assertIn("did not reach a stable login screen", result.stderr)
        self.assertIn("Expected unsigned-simulator SecureStore failure banner was not visible", result.stderr)
        self.assertFalse((output / "result.json").exists())

    def test_stale_session_expired_error_still_fails_the_smoke(self) -> None:
        result, output, commands = self.run_smoke(stale_session_error=True)

        self.assertEqual(result.returncode, 1)
        recognized = (output / "ui-text.txt").read_text(encoding="utf-8")
        self.assertIn("Session", recognized)
        self.assertIn("expired", recognized)
        self.assertEqual(len([item for item in commands if item.startswith("io TEST-DEVICE screenshot ")]), 1)
        self.assertFalse((output / "result.json").exists())

    def test_simulator_bootstatus_failure_stops_before_install(self) -> None:
        result, output, commands = self.run_smoke(bootstatus_fail=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("bootstatus TEST-DEVICE -b", commands)
        self.assertFalse(any(item.startswith("install ") for item in commands))
        self.assertFalse((output / "result.json").exists())

    def test_simulator_bootstatus_timeout_stops_before_install(self) -> None:
        result, output, commands = self.run_smoke(bootstatus_sleep=True, boot_timeout_seconds=1)

        self.assertEqual(result.returncode, 124)
        self.assertIn("did not finish first-boot migration within 1 seconds", result.stderr)
        self.assertIn("bootstatus TEST-DEVICE -b", commands)
        self.assertFalse(any(item.startswith("install ") for item in commands))
        self.assertFalse((output / "result.json").exists())

    def test_simulator_erase_timeout_stops_before_boot_and_install(self) -> None:
        result, output, commands = self.run_smoke(erase_sleep=True, operation_timeout_seconds=1)

        self.assertEqual(result.returncode, 124)
        self.assertIn("iOS Simulator erase timed out after 1 seconds", result.stderr)
        self.assertIn("erase TEST-DEVICE", commands)
        self.assertFalse(any(item.startswith("boot TEST-DEVICE") for item in commands))
        self.assertFalse(any(item.startswith("install TEST-DEVICE") for item in commands))
        self.assertFalse((output / "result.json").exists())

    def test_simulator_boot_timeout_stops_before_bootstatus_and_install(self) -> None:
        result, output, commands = self.run_smoke(boot_sleep=True, operation_timeout_seconds=1)

        self.assertEqual(result.returncode, 124)
        self.assertIn("iOS Simulator boot timed out after 1 seconds", result.stderr)
        self.assertIn("boot TEST-DEVICE", commands)
        self.assertFalse(any(item.startswith("bootstatus TEST-DEVICE") for item in commands))
        self.assertFalse(any(item.startswith("install TEST-DEVICE") for item in commands))
        self.assertFalse((output / "result.json").exists())

    def test_simulator_operation_timeout_configuration_is_bounded_before_device_access(self) -> None:
        result, output, commands = self.run_smoke(operation_timeout_seconds=0)

        self.assertEqual(result.returncode, 2)
        self.assertIn("must be an integer from 1 to 300", result.stderr)
        self.assertEqual(commands, [])
        self.assertFalse((output / "result.json").exists())

    def test_default_boot_timeout_is_600_seconds_and_allows_slow_first_boot(self) -> None:
        self.assertIn('TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS:-600', SMOKE_SCRIPT.read_text(encoding="utf-8"))
        result, output, commands = self.run_smoke(bootstatus_sleep=True)

        self.assertEqual(result.returncode, 0, msg=f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        self.assertIn("bootstatus TEST-DEVICE -b", commands)
        self.assertTrue((output / "result.json").exists())

    def test_install_failure_reports_captured_simctl_error(self) -> None:
        result, output, commands = self.run_smoke(install_fail=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("simctl install fixture failure", result.stderr)
        self.assertIn("installation failed", result.stderr)
        self.assertFalse((output / "launch.txt").exists())
        self.assertFalse((output / "result.json").exists())

    def test_launch_failure_reports_captured_simctl_error(self) -> None:
        result, output, commands = self.run_smoke(launch_fail=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("simctl launch fixture failure", result.stderr)
        self.assertIn("app launch failed", result.stderr)
        self.assertTrue(any(item.startswith("spawn TEST-DEVICE log show ") for item in commands))
        self.assertIn("spawn TEST-DEVICE launchctl list", commands)
        self.assertIn("listapps TEST-DEVICE", commands)
        self.assertIn(
            "SpringBoard launch denied for com.turnova.rider",
            (output / "simulator-system.log").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "com.turnova.rider",
            (output / "launchctl-list-failure.txt").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "Installed app: com.turnova.rider",
            (output / "installed-apps.txt").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "code object is not signed at all",
            (output / "app-signature.txt").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "code object is not signed at all",
            (output / "app-entitlements.plist").read_text(encoding="utf-8"),
        )
        self.assertFalse((output / "launch.txt").exists())
        self.assertFalse((output / "result.json").exists())

    def test_process_exit_captures_failure_diagnostics_without_success_report(self) -> None:
        result, output, _ = self.run_smoke(process_unstable=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("process did not remain available", result.stderr)
        self.assertIn(
            "SpringBoard launch denied for com.turnova.rider",
            (output / "simulator-system.log").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "process not found",
            (output / "process-info.txt").read_text(encoding="utf-8"),
        )
        self.assertIn(
            "Installed app: com.turnova.rider",
            (output / "installed-apps.txt").read_text(encoding="utf-8"),
        )
        self.assertFalse((output / "result.json").exists())
        self.assertFalse((output / "launch.png").exists())

    def test_rejects_adhoc_signed_app_before_simulator_access(self) -> None:
        result, output, commands = self.run_smoke(adhoc_signature=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("requires the original unsigned CI artifact", result.stderr)
        self.assertEqual(commands, [])
        self.assertFalse((output / "result.json").exists())

    def test_refuses_to_erase_a_local_simulator_outside_ci(self) -> None:
        result, output, commands = self.run_smoke(ci=False)

        self.assertEqual(result.returncode, 2)
        self.assertIn("may run only on a CI runner", result.stderr)
        self.assertEqual(commands, [])
        self.assertFalse((output / "result.json").exists())
        self.assertFalse((output / "launch.png").exists())

    def test_invalid_bundle_identifier_is_rejected_before_simulator_access(self) -> None:
        with tempfile.TemporaryDirectory(prefix="turnova-ios-invalid-id-") as temporary:
            root = Path(temporary)
            app_bundle = root / "TurnOva.app"
            app_bundle.mkdir()
            (app_bundle / "Info.plist").write_text("fixture plist", encoding="utf-8")
            xcrun_log = root / "xcrun.log"
            result = subprocess.run(
                [
                    "bash",
                    str(SMOKE_SCRIPT),
                    "--app",
                    str(app_bundle),
                    "--expected-id",
                    "com.turnova;rm",
                    "--output-dir",
                    str(root / "output"),
                ],
                cwd=REPO_ROOT,
                env={**os.environ, "FAKE_XCRUN_LOG": str(xcrun_log)},
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
            self.assertEqual(result.returncode, 2)
            self.assertIn("malformed", result.stderr.lower())
            self.assertFalse(xcrun_log.exists())


if __name__ == "__main__":
    unittest.main()

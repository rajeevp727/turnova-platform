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
        restore_error: bool = False,
        adhoc_signature: bool = True,
        bootstatus_fail: bool = False,
        bootstatus_sleep: bool = False,
        boot_timeout_seconds: int | None = None,
        install_fail: bool = False,
        launch_fail: bool = False,
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
    echo "$FAKE_BUNDLE_ID"
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
  --verify) exit 0 ;;
  -dv)
    if [[ "$FAKE_ADHOC_SIGNATURE" == "1" ]]; then
      echo "Signature=adhoc"
    else
      echo "Signature=Apple Development"
    fi
    ;;
  *) echo "unexpected codesign arguments: $*" >&2; exit 2 ;;
esac
""",
        )
        write_executable(
            bin_dir / "swift",
            r"""#!/usr/bin/env bash
set -euo pipefail
echo "Welcome back Email address Password Sign in"
if [[ "$FAKE_RESTORE_ERROR" == "1" ]]; then
  printf "Unable to restore your\nsession. Check your connection or secure storage and retry.\n"
  exit 1
fi
""",
        )

        environment = os.environ.copy()
        environment.update(
            {
                "PATH": f"{bin_dir}:{os.environ.get('PATH', '')}",
                "PLIST_BUDDY": str(plist_buddy),
                "FAKE_XCRUN_LOG": str(xcrun_log),
                "FAKE_APP_CONTAINER": str(app_container),
                "FAKE_BUNDLE_ID": "com.turnova.rider",
                "FAKE_RESTORE_ERROR": "1" if restore_error else "0",
                "FAKE_ADHOC_SIGNATURE": "1" if adhoc_signature else "0",
                "FAKE_BOOTSTATUS_FAIL": "1" if bootstatus_fail else "0",
                "FAKE_BOOTSTATUS_SLEEP": "1" if bootstatus_sleep else "0",
                "FAKE_INSTALL_FAIL": "1" if install_fail else "0",
                "FAKE_LAUNCH_FAIL": "1" if launch_fail else "0",
                "RUNNER_TEMP": str(root / "runner-temp"),
            }
        )
        if boot_timeout_seconds is not None:
            environment["TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS"] = str(boot_timeout_seconds)

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

    def test_erases_simulator_before_install_and_reports_clean_login(self) -> None:
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
        self.assertTrue(report["staleSessionErrorAbsent"])
        self.assertEqual(report["productionApiOrAuthentication"], "not exercised")
        self.assertEqual(report["signing"], "ad-hoc simulator signature (test-only)")
        self.assertEqual(report["keychainAccessGroup"], "bundle-scoped test entitlement")
        self.assertGreater((output / "launch.png").stat().st_size, 0)

    def test_stale_session_restore_error_fails_the_smoke(self) -> None:
        result, output, _ = self.run_smoke(restore_error=True)

        self.assertEqual(result.returncode, 1)
        self.assertIn("Unable to restore your\nsession", result.stdout)
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
        self.assertFalse((output / "launch.txt").exists())
        self.assertFalse((output / "result.json").exists())

    def test_rejects_non_adhoc_signed_app_before_simulator_access(self) -> None:
        result, output, commands = self.run_smoke(adhoc_signature=False)

        self.assertEqual(result.returncode, 1)
        self.assertIn("requires an ad-hoc signature", result.stderr)
        self.assertEqual(commands, [])
        self.assertFalse((output / "result.json").exists())

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

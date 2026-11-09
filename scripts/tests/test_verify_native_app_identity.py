import plistlib
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import subprocess

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from verify_native_app_identity import (
    parse_aapt_package_id,
    validate_expected_id,
    verify_android_apk,
    verify_ios_info_plist,
)


class NativeArtifactIdentityTests(unittest.TestCase):
    def test_aapt_output_extracts_exactly_one_package_identity(self):
        output = """package: name='com.turnova.rider' versionCode='7' versionName='0.1.0'
sdkVersion:'24'
targetSdkVersion:'35'
"""
        self.assertEqual("com.turnova.rider", parse_aapt_package_id(output))

    def test_aapt_output_rejects_missing_or_ambiguous_package_identity(self):
        with self.assertRaisesRegex(ValueError, "exactly one"):
            parse_aapt_package_id("sdkVersion:'24'\n")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            parse_aapt_package_id(
                "package: name='com.turnova.rider' versionCode='1'\n"
                "package: name='com.turnova.driver' versionCode='1'\n"
            )

    def test_aapt_output_rejects_malformed_package_identity(self):
        with self.assertRaisesRegex(ValueError, "reverse-DNS"):
            parse_aapt_package_id("package: name='com..rider' versionCode='1'\n")

    def test_expected_identity_must_be_reverse_dns(self):
        for value in ("", "com", "com.example.placeholder", "com/turnova/rider"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_expected_id(value)

    def test_android_verifier_compares_the_identity_returned_by_aapt(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "app-release.apk"
            apk.write_bytes(b"fixture apk")
            completed = subprocess.CompletedProcess(
                args=["aapt"], returncode=0,
                stdout="package: name='com.turnova.driver' versionCode='1'\n",
                stderr="",
            )
            with patch("verify_native_app_identity.subprocess.run", return_value=completed) as run:
                self.assertEqual("com.turnova.driver", verify_android_apk(apk, "com.turnova.driver", "/sdk/aapt"))
                run.assert_called_once_with(
                    ["/sdk/aapt", "dump", "badging", str(apk)],
                    check=False, capture_output=True, text=True,
                )

    def test_android_verifier_fails_closed_on_a_different_embedded_package(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "app-release.apk"
            apk.write_bytes(b"fixture apk")
            completed = subprocess.CompletedProcess(
                args=["aapt"], returncode=0,
                stdout="package: name='com.example.wrong' versionCode='1'\n",
                stderr="",
            )
            with patch("verify_native_app_identity.subprocess.run", return_value=completed):
                with self.assertRaisesRegex(ValueError, "mismatch"):
                    verify_android_apk(apk, "com.turnova.driver", "/sdk/aapt")

    def test_android_verifier_does_not_accept_aapt_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "app-release.apk"
            apk.write_bytes(b"fixture apk")
            completed = subprocess.CompletedProcess(args=["aapt"], returncode=1, stdout="", stderr="bad apk")
            with patch("verify_native_app_identity.subprocess.run", return_value=completed):
                with self.assertRaisesRegex(RuntimeError, "aapt exit 1"):
                    verify_android_apk(apk, "com.turnova.rider", "/sdk/aapt")

    def test_android_verifier_rejects_missing_or_empty_apk(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "missing.apk"
            with self.assertRaisesRegex(ValueError, "missing or empty"):
                verify_android_apk(apk, "com.turnova.rider", "/sdk/aapt")
            apk.touch()
            with self.assertRaisesRegex(ValueError, "missing or empty"):
                verify_android_apk(apk, "com.turnova.rider", "/sdk/aapt")

    def test_ios_verifier_reads_xml_and_binary_plists(self):
        for fmt in (plistlib.FMT_XML, plistlib.FMT_BINARY):
            with self.subTest(format=fmt), tempfile.TemporaryDirectory() as directory:
                info = Path(directory) / "Info.plist"
                with info.open("wb") as stream:
                    plistlib.dump({"CFBundleIdentifier": "com.turnova.rider"}, stream, fmt=fmt)
                self.assertEqual("com.turnova.rider", verify_ios_info_plist(info, "com.turnova.rider"))

    def test_ios_verifier_fails_closed_on_missing_or_mismatched_bundle_id(self):
        with tempfile.TemporaryDirectory() as directory:
            info = Path(directory) / "Info.plist"
            with info.open("wb") as stream:
                plistlib.dump({"CFBundleName": "TurnOva"}, stream)
            with self.assertRaisesRegex(ValueError, "CFBundleIdentifier"):
                verify_ios_info_plist(info, "com.turnova.rider")
            with info.open("wb") as stream:
                plistlib.dump({"CFBundleIdentifier": "com.turnova.driver"}, stream)
            with self.assertRaisesRegex(ValueError, "mismatch"):
                verify_ios_info_plist(info, "com.turnova.rider")

    def test_ios_verifier_rejects_malformed_bundle_id(self):
        with tempfile.TemporaryDirectory() as directory:
            info = Path(directory) / "Info.plist"
            with info.open("wb") as stream:
                plistlib.dump({"CFBundleIdentifier": "not-a-bundle-id"}, stream)
            with self.assertRaisesRegex(ValueError, "reverse-DNS"):
                verify_ios_info_plist(info, "com.turnova.rider")


if __name__ == "__main__":
    unittest.main()

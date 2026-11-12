#!/usr/bin/env python3
"""Verify application identities embedded in native Android/iOS build artifacts."""

from __future__ import annotations

import argparse
import plistlib
import re
import subprocess
import sys
from pathlib import Path

PACKAGE_ID_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$")


def validate_expected_id(value: str) -> str:
    if (not isinstance(value, str) or not PACKAGE_ID_PATTERN.fullmatch(value) or
            value.lower() == "com.example" or value.lower().startswith("com.example.")):
        raise ValueError("Expected application ID must be a non-placeholder reverse-DNS identifier.")
    return value


def parse_aapt_package_id(output: str) -> str:
    matches = re.findall(r"^package:\s+name='([^']+)'(?:\s|$)", output, flags=re.MULTILINE)
    if len(matches) != 1:
        raise ValueError("Android manifest must contain exactly one package identity.")
    return validate_expected_id(matches[0])


def verify_android_apk(apk_path: Path, expected_id: str, aapt_path: str) -> str:
    expected = validate_expected_id(expected_id)
    if not apk_path.is_file() or apk_path.stat().st_size == 0:
        raise ValueError("Android APK artifact is missing or empty.")
    result = subprocess.run(
        [aapt_path, "dump", "badging", str(apk_path)],
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(f"Android manifest inspection failed (aapt exit {result.returncode}).")
    actual = parse_aapt_package_id(result.stdout)
    if actual != expected:
        raise ValueError(f"Android APK application ID mismatch: expected {expected}, found {actual}.")
    return actual


def verify_ios_info_plist(info_plist_path: Path, expected_id: str) -> str:
    expected = validate_expected_id(expected_id)
    if not info_plist_path.is_file() or info_plist_path.stat().st_size == 0:
        raise ValueError("iOS Simulator Info.plist is missing or empty.")
    with info_plist_path.open("rb") as stream:
        metadata = plistlib.load(stream)
    actual = metadata.get("CFBundleIdentifier") if isinstance(metadata, dict) else None
    if not isinstance(actual, str):
        raise ValueError("iOS Info.plist does not contain a string CFBundleIdentifier.")
    actual = validate_expected_id(actual)
    if actual != expected:
        raise ValueError(f"iOS Simulator bundle identifier mismatch: expected {expected}, found {actual}.")
    return actual


def verify_ios_app_bundle(app_bundle: Path, expected_id: str) -> str:
    if not app_bundle.is_dir():
        raise ValueError("iOS Simulator app bundle is missing.")
    actual = verify_ios_info_plist(app_bundle / "Info.plist", expected_id)
    with (app_bundle / "Info.plist").open("rb") as stream:
        metadata = plistlib.load(stream)
    executable = metadata.get("CFBundleExecutable") if isinstance(metadata, dict) else None
    if not isinstance(executable, str) or not executable or Path(executable).name != executable:
        raise ValueError("iOS Info.plist does not contain a safe CFBundleExecutable name.")
    executable_path = app_bundle / executable
    if not executable_path.is_file() or executable_path.stat().st_size == 0:
        raise ValueError("iOS Simulator app executable is missing or empty.")

    required_framework_binaries = (
        Path("Frameworks/React.framework/React"),
        Path("Frameworks/ReactNativeDependencies.framework/ReactNativeDependencies"),
    )
    for relative_path in required_framework_binaries:
        framework_binary = app_bundle / relative_path
        if not framework_binary.is_file() or framework_binary.stat().st_size == 0:
            raise ValueError(
                f"iOS Simulator app is missing or empty required framework binary: {relative_path.as_posix()}."
            )
    return actual


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="platform", required=True)
    android = commands.add_parser("android", help="Verify the package ID embedded in an APK.")
    android.add_argument("--apk", required=True, type=Path)
    android.add_argument("--expected-id", required=True)
    android.add_argument("--aapt", required=True)
    ios = commands.add_parser("ios", help="Verify identity and required frameworks in a Simulator app.")
    ios_input = ios.add_mutually_exclusive_group(required=True)
    ios_input.add_argument("--app-bundle", type=Path)
    ios_input.add_argument("--info-plist", type=Path)
    ios.add_argument("--expected-id", required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.platform == "android":
            actual = verify_android_apk(args.apk, args.expected_id, args.aapt)
            print(f"Verified Android APK application ID: {actual}")
        else:
            if args.app_bundle is not None:
                actual = verify_ios_app_bundle(args.app_bundle, args.expected_id)
                print(
                    f"Verified iOS Simulator app bundle: {actual}; main executable, "
                    "React.framework and ReactNativeDependencies.framework are present "
                    "(CI artifact integrity only; signing and production approval remain external)."
                )
            else:
                actual = verify_ios_info_plist(args.info_plist, args.expected_id)
                print(
                    f"Verified iOS Simulator CFBundleIdentifier: {actual} "
                    "(identity-only check; framework completeness was not requested)."
                )
        return 0
    except (OSError, ValueError, RuntimeError, plistlib.InvalidFileException) as error:
        print(f"Native artifact identity verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

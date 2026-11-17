#!/usr/bin/env python3
"""Fail-closed contextual npm audit gate for the Expo 54 build toolchain."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# npm has reported either of these exact major-only SDK migration hints
# for the same transitive advisory across app runs. Neither is a picomatch patch.
EXPECTED_PICOMATCH_REMEDIATIONS = (
    {"name": "react-native", "version": "0.88.1", "isSemVerMajor": True},
    {"name": "expo", "version": "58.0.26", "isSemVerMajor": True},
)
KNOWN_APP_PACKAGE_NAMES = {"turnova-rider", "turnova-driver"}
ALLOWED_HIGH_ADVISORY = (
    "picomatch",
    "https://github.com/advisories/GHSA-fc5q-39xc-vjpf",
)
ALLOWED_PICOMATCH_PARENTS = {
    "@expo/cli",
    "@expo/metro-config",
    "tinyglobby",
    "anymatch",
    "jest-util",
    "micromatch",
}
SEVERITIES = ("info", "low", "moderate", "high", "critical")
IMPORT_PATTERN = re.compile(
    r"(?:from\s*|require(?:\.resolve)?\s*\(|import\s*(?:\(\s*)?)['\"]picomatch(?:/[^'\"]*)?['\"]"
)
SOURCE_PATTERN = re.compile(
    r"(?:^|[/\\])node_modules[/\\]picomatch(?:[/\\]|$)", re.IGNORECASE
)
SOURCE_EXTENSIONS = {".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs"}


def fail(message: str) -> int:
    print(f"Mobile npm audit gate failed: {message}", file=sys.stderr)
    return 1


def high_roots(name: str, vulnerabilities: dict, path: frozenset = frozenset()) -> set[tuple[str, str | None]]:
    if name in path:
        return set()
    item = vulnerabilities.get(name)
    if not isinstance(item, dict):
        return set()
    roots: set[tuple[str, str | None]] = set()
    next_path = path | {name}
    for entry in item.get("via", []):
        if isinstance(entry, dict):
            if entry.get("severity") in {"high", "critical"}:
                roots.add((name, entry.get("url")))
        elif isinstance(entry, str):
            roots.update(high_roots(entry, vulnerabilities, next_path))
    return roots


def package_name_from_lock_path(path: str) -> str:
    if "node_modules/" not in path:
        return "(root)"
    return path.rsplit("node_modules/", 1)[1]


def validate_audit_report(report: dict) -> tuple[dict, dict] | str:
    """Reject incomplete or internally inconsistent npm audit JSON before exceptions apply."""
    metadata = report.get("metadata")
    if not isinstance(metadata, dict):
        return "npm audit report is missing its metadata object."
    counts = metadata.get("vulnerabilities")
    if not isinstance(counts, dict):
        return "npm audit report is missing metadata.vulnerabilities."

    vulnerabilities = report.get("vulnerabilities")
    if not isinstance(vulnerabilities, dict):
        return "npm audit report is missing a valid vulnerabilities object."

    actual_counts = {severity: 0 for severity in SEVERITIES}
    for name, item in vulnerabilities.items():
        if not isinstance(name, str) or not name or not isinstance(item, dict):
            return "npm audit report contains a malformed vulnerability record."
        severity = item.get("severity")
        if not isinstance(severity, str) or severity not in actual_counts:
            return f"npm audit report contains an unknown severity for {name}."
        nodes = item.get("nodes")
        if not isinstance(nodes, list) or not nodes or any(not isinstance(node, str) or not node for node in nodes):
            return f"npm audit report contains invalid package nodes for {name}."
        actual_counts[severity] += 1

    for severity in SEVERITIES:
        reported = counts.get(severity)
        if type(reported) is not int or reported < 0:
            return f"npm audit metadata has an invalid {severity} count."
        if reported != actual_counts[severity]:
            return (
                f"npm audit metadata {severity} count ({reported}) does not match "
                f"the vulnerability records ({actual_counts[severity]})."
            )

    total = counts.get("total")
    if type(total) is not int or total < 0:
        return "npm audit metadata has an invalid total count."
    if total != len(vulnerabilities) or total != sum(actual_counts.values()):
        return "npm audit metadata total does not match the vulnerability records."

    return counts, vulnerabilities


def verify(report_path: Path, audit_status: int, app_dir: Path, bundle_dir: Path) -> int:
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
        package = json.loads((app_dir / "package.json").read_text(encoding="utf-8"))
        lock = json.loads((app_dir / "package-lock.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return fail(f"could not read audit report or app dependency manifests: {exc}")

    if audit_status not in (0, 1):
        return fail(f"npm audit execution failed with exit code {audit_status}")
    if not isinstance(report, dict):
        return fail("npm audit report root must be a JSON object.")
    if not isinstance(package, dict):
        return fail("app package manifest root must be a JSON object.")
    if not isinstance(lock, dict):
        return fail("generated package-lock root must be a JSON object.")

    validated = validate_audit_report(report)
    if isinstance(validated, str):
        return fail(validated)
    metadata, vulnerabilities = validated

    critical = metadata["critical"]
    high = metadata["high"]
    moderate = metadata["moderate"]
    low = metadata["low"]
    total = metadata["total"]

    if (audit_status == 0 and total != 0) or (audit_status == 1 and total == 0):
        return fail("npm audit exit status is inconsistent with its vulnerability inventory.")

    print(
        f"npm audit inventory: {total} total; {critical} critical; {high} high package nodes; "
        f"{moderate} moderate; {low} low."
    )

    if critical:
        return fail("critical advisories are never allowlisted")

    high_packages = {
        name for name, item in vulnerabilities.items()
        if item.get("severity") == "high"
    }
    if high:
        for name in sorted(high_packages):
            roots = high_roots(name, vulnerabilities)
            if roots != {ALLOWED_HIGH_ADVISORY}:
                return fail(
                    f"high finding {name} resolves to unexpected advisory roots: {sorted(roots)}"
                )

        picomatch = vulnerabilities.get("picomatch")
        if not isinstance(picomatch, dict) or picomatch.get("isDirect"):
            return fail("allowlisted picomatch advisory must remain transitive, not a direct app dependency")
        if picomatch.get("severity") != "high":
            return fail("expected the allowlisted picomatch record to remain high severity")
        app_name = package.get("name")
        if app_name not in KNOWN_APP_PACKAGE_NAMES:
            return fail(f"unsupported app package identity for picomatch exception: {app_name!r}")
        if picomatch.get("fixAvailable") not in EXPECTED_PICOMATCH_REMEDIATIONS:
            return fail(f"picomatch remediation metadata changed for {app_name}; re-review the dependency upgrade path")

        nodes = set(picomatch.get("nodes", []))
        lock_packages = lock.get("packages", {})
        if not nodes or any(node not in lock_packages for node in nodes):
            return fail("audit picomatch nodes do not match the generated package-lock")
        if any(not node.startswith("node_modules/") or not node.endswith("picomatch") for node in nodes):
            return fail("picomatch is resolved outside the expected node_modules package paths")

        parents = set()
        for parent_path, item in lock_packages.items():
            dependencies = {}
            for section in ("dependencies", "optionalDependencies", "devDependencies"):
                dependencies.update(item.get(section, {}))
            if "picomatch" in dependencies:
                parents.add(package_name_from_lock_path(parent_path))
        if not parents or not parents.issubset(ALLOWED_PICOMATCH_PARENTS):
            return fail(f"picomatch has an unreviewed dependency parent: {sorted(parents - ALLOWED_PICOMATCH_PARENTS)}")

        for section in ("dependencies", "devDependencies", "optionalDependencies", "peerDependencies"):
            if "picomatch" in package.get(section, {}):
                return fail(f"picomatch must not be a direct app {section} dependency")

        for path in app_dir.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in SOURCE_EXTENSIONS:
                continue
            if "node_modules" in path.parts or "android" in path.parts or "ios" in path.parts:
                continue
            try:
                source = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            if IMPORT_PATTERN.search(source):
                return fail(f"app source imports picomatch directly: {path.relative_to(app_dir)}")

        maps = list(bundle_dir.rglob("*.map")) if bundle_dir.exists() else []
        if not maps:
            return fail("Android export did not produce source maps; bundle-exclusion proof is required")
        for source_map in maps:
            try:
                data = json.loads(source_map.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as exc:
                return fail(f"invalid source map {source_map.name}: {exc}")
            sources = data.get("sources", [])
            if not isinstance(sources, list):
                return fail(f"source map {source_map.name} has no valid sources array")
            if any(SOURCE_PATTERN.search(str(source)) for source in sources):
                return fail(f"vulnerable picomatch source is included in Android bundle map {source_map.name}")

        print(
            "Contextual exception: the only high advisory is GHSA-fc5q-39xc-vjpf in transitive "
            "Expo CLI/Metro/Jest build tooling; it is absent from app imports and the exported Android bundle. "
            "This is not represented as patched; upstream has not published a fixed version."
        )
    else:
        print("No high-severity npm audit findings.")

    print(f"Moderate/low findings are recorded for follow-up: moderate={moderate}, low={low}.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("audit_status", type=int)
    parser.add_argument("app_dir", type=Path)
    parser.add_argument("bundle_dir", type=Path)
    args = parser.parse_args()
    return verify(args.report, args.audit_status, args.app_dir.resolve(), args.bundle_dir.resolve())


if __name__ == "__main__":
    raise SystemExit(main())

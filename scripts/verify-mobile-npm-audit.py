#!/usr/bin/env python3
"""Fail-closed contextual npm audit gate for the Expo 54 build toolchain."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

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
IMPORT_PATTERN = re.compile(
    r"(?:from\s*|require\s*\(|import\s*\()\s*['\"]picomatch(?:/[^'\"]*)?['\"]"
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


def verify(report_path: Path, audit_status: int, app_dir: Path, bundle_dir: Path) -> int:
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
        package = json.loads((app_dir / "package.json").read_text(encoding="utf-8"))
        lock = json.loads((app_dir / "package-lock.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return fail(f"could not read audit report or app dependency manifests: {exc}")

    if audit_status not in (0, 1):
        return fail(f"npm audit execution failed with exit code {audit_status}")

    metadata = report.get("metadata", {}).get("vulnerabilities", {})
    critical = int(metadata.get("critical", 0))
    high = int(metadata.get("high", 0))
    moderate = int(metadata.get("moderate", 0))
    low = int(metadata.get("low", 0))
    total = int(metadata.get("total", 0))
    print(
        f"npm audit inventory: {total} total; {critical} critical; {high} high package nodes; "
        f"{moderate} moderate; {low} low."
    )

    if critical:
        return fail("critical advisories are never allowlisted")

    vulnerabilities = report.get("vulnerabilities", {})
    if not isinstance(vulnerabilities, dict):
        return fail("npm audit vulnerabilities payload is malformed")

    high_packages = {
        name for name, item in vulnerabilities.items()
        if isinstance(item, dict) and item.get("severity") == "high"
    }
    if high:
        if not high_packages:
            return fail("metadata reports high findings but no high package records exist")
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

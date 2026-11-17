#!/usr/bin/env python3
"""Deterministic tests for the mobile npm audit context gate."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify-mobile-npm-audit.py")
ADVISORY = "https://github.com/advisories/GHSA-fc5q-39xc-vjpf"


class MobileNpmAuditGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.app = self.root / "app"
        self.bundle = self.root / "bundle"
        (self.app / "src").mkdir(parents=True)
        self.bundle.mkdir()
        (self.app / "package.json").write_text(
            json.dumps({"name": "turnova-rider", "dependencies": {"expo": "~54.0.0"}}),
            encoding="utf-8",
        )
        (self.app / "package-lock.json").write_text(json.dumps({
            "packages": {
                "": {"dependencies": {"expo": "~54.0.0"}},
                "node_modules/picomatch": {"version": "4.0.7"},
                "node_modules/@expo/cli": {
                    "version": "54.0.28", "dependencies": {"picomatch": "^4.0.3"}
                },
            }
        }), encoding="utf-8")
        (self.bundle / "index.android.js.map").write_text(json.dumps({
            "version": 3, "sources": ["../../src/index.tsx"], "names": [], "mappings": ""
        }), encoding="utf-8")
        self.report = {
            "metadata": {"vulnerabilities": {
                "info": 0, "low": 0, "moderate": 0, "high": 2, "critical": 0, "total": 2
            }},
            "vulnerabilities": {
                "picomatch": {
                    "severity": "high", "isDirect": False, "range": "<=4.0.7",
                    "nodes": ["node_modules/picomatch"],
                    "fixAvailable": {"name": "react-native", "version": "0.88.1", "isSemVerMajor": True},
                    "via": [{"name": "picomatch", "severity": "high", "url": ADVISORY}]
                },
                "@expo/cli": {
                    "severity": "high", "isDirect": False,
                    "nodes": ["node_modules/@expo/cli"], "via": ["picomatch"]
                },
            },
        }
        self.report_path = self.root / "audit.json"
        self.write_report()

    def tearDown(self):
        self.temp.cleanup()

    def write_report(self):
        self.report_path.write_text(json.dumps(self.report), encoding="utf-8")

    def run_gate(self, audit_status=1):
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(self.report_path), str(audit_status), str(self.app), str(self.bundle)],
            capture_output=True, text=True, check=False,
        )

    def test_allows_only_transitive_build_tooling_advisory_outside_bundle(self):
        result = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        self.assertIn("not represented as patched", result.stdout)

    def test_rejects_any_critical_finding(self):
        self.report["vulnerabilities"]["critical-package"] = {
            "severity": "critical", "isDirect": False,
            "nodes": ["node_modules/critical-package"],
            "via": [{"name": "critical-package", "severity": "critical", "url": "https://example.invalid/critical"}],
        }
        counts = self.report["metadata"]["vulnerabilities"]
        counts["critical"] = 1
        counts["total"] = 3
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_unknown_high_advisory(self):
        self.report["vulnerabilities"]["other"] = {
            "severity": "high", "isDirect": False,
            "nodes": ["node_modules/other"],
            "via": [{"name": "other", "severity": "high", "url": "https://example.invalid/GHSA-unknown"}]
        }
        counts = self.report["metadata"]["vulnerabilities"]
        counts["high"] = 3
        counts["total"] = 3
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_direct_picomatch_dependency(self):
        package = json.loads((self.app / "package.json").read_text())
        package["dependencies"]["picomatch"] = "4.0.7"
        (self.app / "package.json").write_text(json.dumps(package))
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_app_source_import(self):
        (self.app / "src" / "index.ts").write_text("import picomatch from 'picomatch';")
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_picomatch_in_exported_bundle_source_map(self):
        (self.bundle / "index.android.js.map").write_text(json.dumps({
            "version": 3, "sources": ["../../node_modules/picomatch/lib/index.js"], "names": [], "mappings": ""
        }))
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_unreviewed_picomatch_parent(self):
        lock = json.loads((self.app / "package-lock.json").read_text())
        lock["packages"]["node_modules/unknown-tool"] = {
            "version": "1.0.0", "dependencies": {"picomatch": "^4.0.7"}
        }
        (self.app / "package-lock.json").write_text(json.dumps(lock))
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_missing_source_maps(self):
        (self.bundle / "index.android.js.map").unlink()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_missing_audit_metadata(self):
        del self.report["metadata"]
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_mismatched_severity_count(self):
        self.report["metadata"]["vulnerabilities"]["high"] = 0
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_mismatched_total_count(self):
        self.report["metadata"]["vulnerabilities"]["total"] = 3
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_malformed_vulnerability_nodes(self):
        self.report["vulnerabilities"]["picomatch"]["nodes"] = []
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_audit_exit_status_inconsistent_with_findings(self):
        self.assertNotEqual(self.run_gate(audit_status=0).returncode, 0)

    def test_rejects_nonzero_exit_without_findings(self):
        self.report["metadata"]["vulnerabilities"] = {
            "info": 0, "low": 0, "moderate": 0, "high": 0, "critical": 0, "total": 0
        }
        self.report["vulnerabilities"] = {}
        self.write_report()
        self.assertNotEqual(self.run_gate(audit_status=1).returncode, 0)


    def test_allows_reviewed_expo_sdk_remediation_metadata(self):
        self.report["vulnerabilities"]["picomatch"]["fixAvailable"] = {
            "name": "expo", "version": "58.0.26", "isSemVerMajor": True
        }
        self.write_report()
        result = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        self.assertIn("not represented as patched", result.stdout)

    def test_rejects_unreviewed_picomatch_remediation_path(self):
        self.report["vulnerabilities"]["picomatch"]["fixAvailable"] = True
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_unreviewed_picomatch_remediation_version(self):
        self.report["vulnerabilities"]["picomatch"]["fixAvailable"] = {
            "name": "expo", "version": "58.0.27", "isSemVerMajor": True
        }
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_non_major_picomatch_remediation(self):
        self.report["vulnerabilities"]["picomatch"]["fixAvailable"] = {
            "name": "expo", "version": "58.0.26", "isSemVerMajor": False
        }
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)


    def test_rejects_unknown_audit_severity(self):
        self.report["vulnerabilities"]["picomatch"]["severity"] = "urgent"
        self.write_report()
        self.assertNotEqual(self.run_gate().returncode, 0)

    def test_rejects_non_object_audit_report_root(self):
        self.report_path.write_text(json.dumps([]), encoding="utf-8")
        self.assertNotEqual(self.run_gate().returncode, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

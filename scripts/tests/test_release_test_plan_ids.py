#!/usr/bin/env python3
"""Guard stable, unique RELEASE-SMOKE identifiers in the test plan."""

from __future__ import annotations

import re
import unittest
from collections import Counter
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
TEST_PLAN = REPO_ROOT / "docs" / "test-plan.md"


class ReleaseTestPlanIdTests(unittest.TestCase):
    def test_release_smoke_test_ids_are_unique(self) -> None:
        content = TEST_PLAN.read_text(encoding="utf-8")
        identifiers = re.findall(r"^\\|\\s*(RELEASE-SMOKE-\\d+)\\s*\\|", content, flags=re.MULTILINE)
        duplicates = sorted(identifier for identifier, count in Counter(identifiers).items() if count > 1)
        self.assertGreater(len(identifiers), 0, "No RELEASE-SMOKE test IDs were found")
        self.assertEqual(duplicates, [], f"Duplicate RELEASE-SMOKE IDs: {duplicates}")


if __name__ == "__main__":
    unittest.main()

import json
import os
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class CliTest(unittest.TestCase):
    def _run(self, *args):
        env = dict(os.environ)
        env["PYTHONPATH"] = str(ROOT / "src")
        return subprocess.run(
            [sys.executable, "-m", "spirepilot", *args],
            cwd=ROOT,
            env=env,
            capture_output=True,
            text=True,
            timeout=20,
        )

    def test_doctor(self):
        result = self._run("doctor")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("OK", result.stdout)

    def test_inspect(self):
        result = self._run("inspect", "--state", str(ROOT / "data" / "samples" / "combat_basic.json"))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("状态审计", result.stdout)

    def test_trace(self):
        result = self._run("trace", "--trace", str(ROOT / "data" / "traces" / "basic_thunderclap.json"))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("PASS", result.stdout)

    def test_live_state_json(self):
        result = self._run(
            "live",
            "--state",
            str(ROOT / "data" / "samples" / "sts2mcp_combat.json"),
            "--solver",
            "exact",
            "--json",
            "--samples",
            "1",
            "--budget-ms",
            "200",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertEqual(payload["solver"], "exact")
        self.assertIn("action", payload)
        self.assertTrue(payload["reasons"])

    def test_live_rollout_json(self):
        result = self._run(
            "live",
            "--state",
            str(ROOT / "data" / "samples" / "sts2mcp_combat.json"),
            "--solver",
            "rollout",
            "--json",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertTrue(payload)
        self.assertIn("hp", payload[0])

    def test_advise_json(self):
        result = self._run(
            "advise",
            "--state",
            str(ROOT / "data" / "samples" / "combat_basic.json"),
            "--json",
            "--samples",
            "1",
            "--budget-ms",
            "150",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertIn("action", payload)
        self.assertIn(payload["action"]["type"], {"play_card", "end_turn"})
        self.assertTrue(payload["reasons"])


if __name__ == "__main__":
    unittest.main()

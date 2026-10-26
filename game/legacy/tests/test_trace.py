import json
import tempfile
import unittest
from pathlib import Path

from spirepilot.cards import CardDatabase
from spirepilot.trace import load_trace, replay_trace

ROOT = Path(__file__).resolve().parents[1]
TRACE = ROOT / "data" / "traces" / "basic_thunderclap.json"


class TraceTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.trace = json.loads(TRACE.read_text(encoding="utf-8"))

    def test_synthetic_trace_passes(self):
        result = replay_trace(self.trace, self.db)
        self.assertTrue(result.passed, result.render())
        self.assertEqual(len(result.steps), 1)
        self.assertEqual(result.errors, 0)

    def test_detects_wrong_hp(self):
        trace = json.loads(json.dumps(self.trace))
        trace["steps"][0]["expected_state"]["Enemies"][0]["CurrentHp"] = 99
        result = replay_trace(trace, self.db)
        self.assertFalse(result.passed)
        self.assertGreaterEqual(result.errors, 1)
        self.assertTrue(any("Enemy[0].hp" == d.field for d in result.steps[0].diffs))

    def test_trace_without_expected_state_is_executable(self):
        trace = json.loads(json.dumps(self.trace))
        trace["steps"][0].pop("expected_state")
        result = replay_trace(trace, self.db)
        self.assertTrue(result.passed)
        self.assertTrue(result.steps[0].warnings)

    def test_load_trace(self):
        result = load_trace(TRACE, self.db)
        self.assertTrue(result.passed)


if __name__ == "__main__":
    unittest.main()

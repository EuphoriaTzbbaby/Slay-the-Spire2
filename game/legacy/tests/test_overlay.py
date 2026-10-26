import json
import unittest
from pathlib import Path

from spirepilot.cards import CardDatabase
from spirepilot.overlay import build_view, format_lines
from spirepilot.search import SearchConfig

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "data" / "samples" / "sts2mcp_combat.json"


class OverlayViewTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.raw = json.loads(FIXTURE.read_text(encoding="utf-8"))
        cls.config = SearchConfig(max_depth=5, time_budget_ms=250, samples=1)

    def test_combat_view_has_both_solvers(self):
        view = build_view(self.raw, self.db, self.config)
        self.assertIsNotNone(view.exact)
        self.assertIsNotNone(view.beam)
        self.assertIn("推荐", "\n".join(format_lines(view)))
        self.assertTrue(any("[0]" in line for line in format_lines(view)))

    def test_non_combat_view(self):
        view = build_view({"state_type": "rewards", "message": "Choose a card"}, self.db, self.config)
        lines = format_lines(view)
        self.assertTrue(any("rewards" in line for line in lines))

    def test_error_view_does_not_crash(self):
        view = build_view({"state_type": "monster"}, self.db, self.config)
        self.assertTrue(view.error)
        self.assertIn("错误", "\n".join(format_lines(view)))


if __name__ == "__main__":
    unittest.main()

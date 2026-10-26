import unittest
from pathlib import Path

from spirepilot.cards import CardDatabase
from spirepilot.exact import exact_search
from spirepilot.model import EndTurn
from spirepilot.search import SearchConfig, action_label
from spirepilot.sts2mcp import load_state_file

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "data" / "samples" / "sts2mcp_combat.json"


class ExactSearchTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.state = load_state_file(FIXTURE, cls.db)
        cls.config = SearchConfig(max_depth=6, time_budget_ms=500, samples=1)

    def test_exact_returns_complete_line(self):
        result = exact_search(self.state, self.config)
        self.assertTrue(result.complete)
        self.assertEqual(result.solver, "exact")
        self.assertEqual(result.stats["complete"], True)
        self.assertTrue(result.labels)
        self.assertEqual(result.sequence[0], result.action)
        self.assertEqual(result.labels[0], action_label(self.state, result.action))
        self.assertTrue(result.alternatives)
        self.assertIsNotNone(result.alternatives[0].player_hp)

    def test_exact_prefers_less_hp_loss(self):
        result = exact_search(self.state, self.config)
        # On the fixture, Defend reduces actual HP loss vs. an all-attack line.
        self.assertEqual(result.action.hand_index, 0)
        self.assertEqual(result.labels[0], "防御")
        self.assertEqual(result.alternatives[0].hp_lost, 7)

    def test_empty_hand_ends_turn(self):
        state = load_state_file(FIXTURE, self.db)
        state.hand = []
        result = exact_search(state, self.config)
        self.assertIsInstance(result.action, EndTurn)
        self.assertEqual(result.labels[0], "结束回合")


if __name__ == "__main__":
    unittest.main()

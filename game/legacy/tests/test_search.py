import unittest
from pathlib import Path

from spirepilot.adapter import parse_file
from spirepilot.advisor import advice_to_dict, advise
from spirepilot.cards import CardDatabase
from spirepilot.model import EndTurn, PlayCard
from spirepilot.rules import legal_actions
from spirepilot.search import SearchConfig, action_label

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class SearchTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.config = SearchConfig(max_depth=4, beam_width=10, time_budget_ms=250, samples=1)

    def test_advice_is_legal_and_has_reasons(self):
        state = parse_file(str(SAMPLE), self.db)
        advice = advise(state, self.config)
        self.assertIn(advice.action.key, {a.key for a in legal_actions(state)})
        self.assertTrue(advice.reasons)
        self.assertTrue(advice.warnings)  # approximate model warning
        self.assertIn("action", advice_to_dict(advice))
        # Regression: the displayed label must belong to the returned action.
        self.assertEqual(advice.sequence[0], advice.action)
        self.assertEqual(advice.sequence_labels[0], action_label(state, advice.action))

    def test_zero_energy_ends_turn(self):
        state = parse_file(str(SAMPLE), self.db)
        state.player.energy = 0
        advice = advise(state, SearchConfig(max_depth=3, beam_width=8, time_budget_ms=150, samples=1))
        self.assertIsInstance(advice.action, EndTurn)

    def test_combat_over(self):
        state = parse_file(str(SAMPLE), self.db)
        for enemy in state.enemies:
            enemy.hp = 0
        advice = advise(state, self.config)
        self.assertIn("结束", advice.label)


if __name__ == "__main__":
    unittest.main()

import unittest
from pathlib import Path

from spirepilot.cards import CardDatabase
from spirepilot.model import EndTurn
from spirepilot.rollout import rollout_rank
from spirepilot.sts2mcp import load_state_file

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "data" / "samples" / "sts2mcp_combat.json"


class RolloutTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.state = load_state_file(FIXTURE, cls.db)

    def test_rollout_rank_has_multiple_lines(self):
        lines = rollout_rank(self.state, turns=2, samples=1, policy_ms=20)
        self.assertGreaterEqual(len(lines), 3)
        self.assertFalse(lines[0].lethal)
        self.assertEqual(lines[0].hp, 70)
        self.assertTrue(all(line.hp <= 91 for line in lines))

    def test_end_turn_is_not_automatically_best(self):
        lines = rollout_rank(self.state, turns=2, samples=1, policy_ms=20)
        end = next((line for line in lines if isinstance(line.action, EndTurn)), None)
        self.assertIsNotNone(end)
        self.assertLess(end.hp, lines[0].hp)


if __name__ == "__main__":
    unittest.main()

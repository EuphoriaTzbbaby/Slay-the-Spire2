import random
import unittest
from pathlib import Path

from spirepilot.adapter import parse_file
from spirepilot.cards import CardDatabase
from spirepilot.model import EndTurn, PlayCard
from spirepilot.rules import enemy_turn, legal_actions, play_card

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class RulesTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()

    def setUp(self):
        self.state = parse_file(str(SAMPLE), self.db)

    def test_legal_actions_count(self):
        actions = legal_actions(self.state)
        # Strike/Bash/Pommel: 2 targets each; Thunderclap: 1; Defend: 1; EndTurn: 1.
        self.assertEqual(len(actions), 9)
        self.assertEqual(sum(isinstance(a, EndTurn) for a in actions), 1)

    def test_bash_damage_and_vulnerable(self):
        # Hand: Strike, Bash, Thunderclap, Pommel Strike, Defend.
        state, report = play_card(self.state, PlayCard(1, 0), random.Random(0))
        enemy = state.enemy(0)
        self.assertIsNotNone(enemy)
        self.assertEqual(enemy.hp, 33)  # 8 base + 1 Strength
        self.assertEqual(enemy.power("vulnerable"), 2)
        self.assertEqual(state.player.energy, 1)
        self.assertEqual(len(state.hand), 4)
        self.assertIn("Bash", report.card_name)

    def test_thunderclap_hits_all(self):
        state, report = play_card(self.state, PlayCard(2, None), random.Random(0))
        self.assertEqual(state.enemy(0).hp, 37)  # 4 + 1
        self.assertEqual(state.enemy(1).hp, 11)
        self.assertEqual(state.enemy(0).power("vulnerable"), 1)
        self.assertEqual(state.enemy(1).power("vulnerable"), 1)
        self.assertEqual(report.damage_dealt, 10)
        self.assertEqual(report.vulnerable_applied, 2)  # total across two enemies

    def test_defend_blocks_enemy_turn(self):
        state, _ = play_card(self.state, PlayCard(4, None), random.Random(0))
        after, report = enemy_turn(state, random.Random(0))
        # 11 + 6 = 17 incoming; 5 block -> 12 HP lost, 58 -> 46.
        self.assertEqual(report.damage_taken, 17)
        self.assertEqual(report.block_absorbed, 5)
        self.assertEqual(after.player.hp, 46)

    def test_enemy_turn_without_block(self):
        after, report = enemy_turn(self.state, random.Random(0))
        self.assertEqual(report.damage_taken, 17)
        self.assertEqual(after.player.hp, 41)
        self.assertFalse(report.player_died)


if __name__ == "__main__":
    unittest.main()

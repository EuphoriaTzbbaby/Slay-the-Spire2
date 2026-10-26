import random
import unittest
from pathlib import Path

from spirepilot.adapter import parse_file
from spirepilot.cards import CardDatabase
from spirepilot.model import EnemyState, Intent, PlayCard
from spirepilot.rules import play_card

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class SpecialCardTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()

    def _base(self):
        state = parse_file(str(SAMPLE), self.db)
        state.hand = []
        state.draw_pile = []
        state.discard_pile = []
        state.exhaust_pile = []
        state.player.block = 0
        state.player.powers = {}
        state.player.energy = 3
        state.enemies = [
            EnemyState(index=0, monster_id="DUMMY", hp=50, max_hp=50, intents=()),
        ]
        return state

    def test_body_slam_uses_block(self):
        state = self._base()
        state.hand = [self.db.make_instance("BODY_SLAM", "h0")]
        state.player.block = 12
        state.player.energy = 1
        after, report = play_card(state, PlayCard(0, 0), random.Random(0))
        self.assertEqual(after.enemy(0).hp, 38)
        self.assertEqual(report.damage_dealt, 12)

    def test_whirlwind_uses_all_energy(self):
        state = self._base()
        state.hand = [self.db.make_instance("WHIRLWIND", "h0")]
        state.player.energy = 3
        state.enemies.append(EnemyState(index=1, monster_id="DUMMY2", hp=50, max_hp=50, intents=()))
        after, report = play_card(state, PlayCard(0, None), random.Random(0))
        self.assertEqual(after.player.energy, 0)
        self.assertEqual(after.enemy(0).hp, 35)  # 5 damage x 3 hits
        self.assertEqual(after.enemy(1).hp, 35)
        self.assertEqual(report.damage_dealt, 30)

    def test_offering_costs_hp_and_draws(self):
        state = self._base()
        state.hand = [self.db.make_instance("OFFERING", "h0")]
        state.player.hp = 20
        state.draw_pile = [self.db.make_instance("STRIKE_IRONCLAD", f"d{i}") for i in range(5)]
        after, report = play_card(state, PlayCard(0, None), random.Random(0))
        self.assertEqual(after.player.hp, 14)
        self.assertEqual(after.player.energy, 5)  # cost 0, +2
        self.assertEqual(report.cards_drawn, 3)
        self.assertEqual(len(after.hand), 3)

    def test_fiend_fire_exhausts_hand(self):
        state = self._base()
        state.hand = [
            self.db.make_instance("FIEND_FIRE", "h0"),
            self.db.make_instance("STRIKE_IRONCLAD", "h1"),
            self.db.make_instance("DEFEND_IRONCLAD", "h2"),
        ]
        state.player.energy = 2
        after, report = play_card(state, PlayCard(0, 0), random.Random(0))
        self.assertEqual(after.enemy(0).hp, 36)  # 7 damage x 2 exhausted cards
        self.assertEqual(len(after.hand), 0)
        self.assertEqual(len(after.exhaust_pile), 3)
        self.assertEqual(report.damage_dealt, 14)

    def test_second_wind_exhausts_non_attacks(self):
        state = self._base()
        state.hand = [
            self.db.make_instance("SECOND_WIND", "h0"),
            self.db.make_instance("DEFEND_IRONCLAD", "h1"),
            self.db.make_instance("STRIKE_IRONCLAD", "h2"),
        ]
        state.player.energy = 1
        after, report = play_card(state, PlayCard(0, None), random.Random(0))
        self.assertEqual(after.player.block, 5)  # one non-attack card exhausted
        self.assertEqual(len(after.hand), 1)  # Strike remains
        self.assertEqual(len(after.exhaust_pile), 1)


if __name__ == "__main__":
    unittest.main()

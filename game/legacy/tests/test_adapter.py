import json
import unittest
from pathlib import Path

from spirepilot.adapter import parse_combat_state, is_combat_state
from spirepilot.cards import CardDatabase

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class AdapterTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.raw = json.loads(SAMPLE.read_text(encoding="utf-8"))

    def test_is_combat_state(self):
        self.assertTrue(is_combat_state(self.raw))
        self.assertFalse(is_combat_state({"Trigger": "Loot", "Rewards": []}))

    def test_parse_sample(self):
        state = parse_combat_state(self.raw, self.db)
        self.assertEqual(state.turn, 3)
        self.assertEqual(state.player.hp, 58)
        self.assertEqual(state.player.energy, 3)
        self.assertEqual(len(state.hand), 5)
        self.assertEqual(len(state.enemies), 2)
        self.assertEqual(state.hand[0].card_id, "STRIKE_IRONCLAD")
        self.assertEqual(state.player.power("strength"), 1)
        self.assertEqual(state.player.relics, ("BURNING_BLOOD", "VAJRA"))
        self.assertEqual(state.warnings, ())

    def test_missing_block_warns(self):
        raw = json.loads(json.dumps(self.raw))
        raw["Player"].pop("Block", None)
        for enemy in raw["Enemies"]:
            enemy.pop("Block", None)
        state = parse_combat_state(raw, self.db)
        self.assertTrue(any("玩家格挡" in w for w in state.warnings))
        self.assertTrue(any("敌人格挡" in w for w in state.warnings))

    def test_missing_catalog_uses_static_db(self):
        raw = json.loads(json.dumps(self.raw))
        raw.pop("CardCatalog", None)
        state = parse_combat_state(raw, self.db)
        self.assertTrue(any("CardCatalog" in w for w in state.warnings))
        self.assertEqual(state.hand[0].card_id, "STRIKE_IRONCLAD")
        self.assertEqual(state.hand[0].spec.damage, 6)

    def test_minimal_upstream_shape(self):
        raw = json.loads((ROOT / "data" / "samples" / "combat_minimal.json").read_text(encoding="utf-8"))
        state = parse_combat_state(raw, self.db)
        self.assertEqual(state.player.hp, 60)
        self.assertEqual(len(state.hand), 4)
        self.assertEqual(state.draw_pile, [])
        self.assertTrue(any("玩家格挡" in w for w in state.warnings))
        self.assertTrue(any("敌人格挡" in w for w in state.warnings))
        # CardCatalog Cost is a string ("1"), which must be normalized.
        self.assertEqual(state.hand[0].spec.cost, 1)

    def test_catalog_fallback_for_unknown_card(self):
        raw = json.loads(json.dumps(self.raw))
        raw["Piles"]["HandPile"][0] = {"Card_Name": "MYSTERY", "Card_ID": 999}
        raw["CardCatalog"].append(
            {
                "CardId": "MYSTERY",
                "IsUpgraded": False,
                "TotalCount": 1,
                "Cost": 0,
                "TargetType": "AnyEnemy",
                "Damage": 7,
                "Block": 0,
                "DrawsCards": 0,
                "GivesEnergy": 0,
                "AppliesVulnerable": 0,
                "AppliesWeak": 0,
            }
        )
        state = parse_combat_state(raw, self.db)
        card = state.hand[0]
        self.assertEqual(card.card_id, "MYSTERY")
        self.assertEqual(card.spec.damage, 7)
        self.assertEqual(card.spec.source, "live_catalog")
        self.assertEqual(card.spec.cost, 0)


if __name__ == "__main__":
    unittest.main()

import json
import unittest
from pathlib import Path

from spirepilot.cards import CardDatabase
from spirepilot.sts2mcp import UnsupportedStateError, parse_state

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "data" / "samples" / "sts2mcp_combat.json"


class Sts2McpAdapterTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.raw = json.loads(FIXTURE.read_text(encoding="utf-8"))

    def test_parse_live_combat(self):
        state = parse_state(self.raw, self.db)
        self.assertEqual(state.player.hp, 91)
        self.assertEqual(state.player.energy, 3)
        self.assertEqual(len(state.hand), 5)
        self.assertEqual(len(state.enemies), 1)
        self.assertEqual(state.enemies[0].intents[0].damage, 12)
        self.assertEqual(state.hand[2].card_id, "BASH")
        self.assertEqual(state.draw_pile[0].card_id, "DEFEND_IRONCLAD")
        self.assertTrue(any("draw_pile" in w for w in state.warnings))

    def test_menu_state_is_rejected(self):
        with self.assertRaises(UnsupportedStateError):
            parse_state({"state_type": "menu", "menu_screen": "main"}, self.db)

    def test_hand_card_cost_and_playability(self):
        state = parse_state(self.raw, self.db)
        bash = state.hand[2]
        self.assertEqual(bash.cost, 2)
        self.assertTrue(bash.playable_override)


if __name__ == "__main__":
    unittest.main()

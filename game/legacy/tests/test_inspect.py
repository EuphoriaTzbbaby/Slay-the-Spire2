import json
import unittest
from pathlib import Path

from spirepilot.inspect import audit_state

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class InspectTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.raw = json.loads(SAMPLE.read_text(encoding="utf-8"))

    def test_combat_state_degraded_for_missing_hand_fields(self):
        report = audit_state(self.raw)
        self.assertEqual(report.kind, "combat")
        self.assertEqual(report.verdict, "degraded")
        names = {c.name for c in report.checks}
        self.assertIn("CardCatalog", names)
        self.assertIn("Player.Block", names)
        self.assertTrue(any("手牌 Cost" == c.name for c in report.checks))

    def test_missing_block_is_warned(self):
        raw = json.loads(json.dumps(self.raw))
        raw["Player"].pop("Block", None)
        for enemy in raw["Enemies"]:
            enemy.pop("Block", None)
        report = audit_state(raw)
        self.assertTrue(any("Player.Block" == c.name and c.status == "warn" for c in report.checks))
        self.assertTrue(any("敌人 Block" == c.name and c.status == "warn" for c in report.checks))
        self.assertTrue(any("Block 缺失" in w for w in report.warnings))

    def test_reward_state(self):
        report = audit_state(
            {
                "Trigger": "Loot",
                "Rewards": [
                    {"Type": "Card", "Options": [{"CardId": "ANGER"}, {"CardId": "BASH"}]},
                    {"Type": "Gold", "Amount": 20},
                ],
            }
        )
        self.assertEqual(report.kind, "reward")
        self.assertEqual(report.stats["card_options"], 2)
        self.assertTrue(any("SelectReward" in c.detail for c in report.checks))

    def test_blocked_payload(self):
        report = audit_state({"Trigger": "Loot"})
        self.assertEqual(report.verdict, "non_combat")


if __name__ == "__main__":
    unittest.main()

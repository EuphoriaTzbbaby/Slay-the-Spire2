import unittest

from spirepilot.cards import CardDatabase, normalize_card_id, spec_from_catalog


class CardDatabaseTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()

    def test_database_has_ironclad_cards(self):
        self.assertGreaterEqual(len(self.db), 80)
        self.assertIsNotNone(self.db.path)

    def test_core_card_stats(self):
        strike = self.db.get("STRIKE_IRONCLAD")
        self.assertIsNotNone(strike)
        self.assertEqual(strike.cost, 1)
        self.assertEqual(strike.damage, 6)
        self.assertEqual(strike.target, "AnyEnemy")

        thunder = self.db.get("THUNDERCLAP")
        self.assertIsNotNone(thunder)
        self.assertEqual(thunder.target, "AllEnemies")
        self.assertEqual(thunder.power_amount("Vulnerable"), 1)

    def test_alias_resolution(self):
        self.assertEqual(self.db.get("STRIKE").card_id, "STRIKE_IRONCLAD")
        self.assertEqual(self.db.get("DEFEND").card_id, "DEFEND_IRONCLAD")

    def test_catalog_fallback(self):
        spec = spec_from_catalog(
            {
                "CardId": "MYSTERY",
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
        self.assertEqual(spec.card_id, "MYSTERY")
        self.assertEqual(spec.damage, 7)
        self.assertEqual(spec.cost, 0)
        self.assertEqual(spec.source, "live_catalog")

    def test_normalize_id(self):
        self.assertEqual(normalize_card_id(" strike "), "STRIKE")


if __name__ == "__main__":
    unittest.main()

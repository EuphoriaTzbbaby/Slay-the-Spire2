import http.client
import json
import tempfile
import threading
import unittest
from pathlib import Path

from spirepilot.capture import CaptureStore, load_capture_raw, make_capture_server

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class CaptureTest(unittest.TestCase):
    def test_capture_stores_raw_state(self):
        with tempfile.TemporaryDirectory() as tmp:
            store = CaptureStore(out_dir=Path(tmp), session="test", quiet=True)
            server = make_capture_server("127.0.0.1", 0, store)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                raw = json.loads(SAMPLE.read_text(encoding="utf-8"))
                conn = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=5)
                conn.request(
                    "POST",
                    "/update_state",
                    body=json.dumps(raw),
                    headers={"Content-Type": "application/json"},
                )
                response = conn.getresponse()
                body = response.read().decode("utf-8")
                conn.close()
                self.assertEqual(response.status, 200)
                self.assertEqual(body, "OK")

                state_path = Path(tmp) / "test" / "states.jsonl"
                self.assertTrue(state_path.exists())
                lines = state_path.read_text(encoding="utf-8").strip().splitlines()
                self.assertEqual(len(lines), 1)
                record = json.loads(lines[0])
                self.assertEqual(record["kind"], "combat")
                self.assertEqual(record["trigger"], "OnTurnStarted")
                self.assertEqual(record["raw"]["Player"]["CurrentHp"], 58)
                raw_again = load_capture_raw(state_path)
                self.assertEqual(raw_again["Player"]["CurrentHp"], 58)

                health = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=5)
                health.request("GET", "/health")
                payload = json.loads(health.getresponse().read().decode("utf-8"))
                health.close()
                self.assertTrue(payload["ok"])
                self.assertEqual(payload["counts"]["stored"], 1)
            finally:
                server.shutdown()
                server.server_close()
                thread.join(timeout=2)

    def test_capture_reward_kind(self):
        with tempfile.TemporaryDirectory() as tmp:
            store = CaptureStore(out_dir=Path(tmp), session="reward", quiet=True)
            store.prepare()
            kind = store.store({"Trigger": "Loot", "Rewards": [{"Type": "Card", "Options": []}]})
            self.assertEqual(kind, "reward")
            self.assertEqual(store.snapshot()["counts"]["reward"], 1)


if __name__ == "__main__":
    unittest.main()

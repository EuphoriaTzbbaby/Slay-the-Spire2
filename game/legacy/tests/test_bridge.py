import http.client
import json
import threading
import unittest
from pathlib import Path

from spirepilot.bridge import make_server
from spirepilot.cards import CardDatabase
from spirepilot.search import SearchConfig

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "data" / "samples" / "combat_basic.json"


class BridgeTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = CardDatabase.load()
        cls.config = SearchConfig(max_depth=3, beam_width=8, time_budget_ms=150, samples=1)
        cls.raw = json.loads(SAMPLE.read_text(encoding="utf-8"))

    def _serve(self, auto=False):
        server = make_server(
            "127.0.0.1",
            0,
            self.db,
            self.config,
            auto=auto,
            quiet=True,
            log_path=None,
        )
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        return server, thread

    def _post(self, server, payload):
        conn = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=5)
        body = json.dumps(payload)
        conn.request("POST", "/update_state", body=body, headers={"Content-Type": "application/json"})
        response = conn.getresponse()
        data = response.read().decode("utf-8")
        conn.close()
        return response.status, data

    def test_copilot_returns_ok(self):
        server, thread = self._serve(auto=False)
        try:
            status, data = self._post(server, self.raw)
            self.assertEqual(status, 200)
            self.assertEqual(data, "OK")
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_auto_returns_action(self):
        server, thread = self._serve(auto=True)
        try:
            status, data = self._post(server, self.raw)
            self.assertEqual(status, 200)
            payload = json.loads(data)
            self.assertIn(payload["Type"], {"PlayCard", "EndTurn"})
            if payload["Type"] == "PlayCard":
                self.assertIn("HandIndex", payload)
                self.assertIn("TargetIndex", payload)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_health(self):
        server, thread = self._serve(auto=False)
        try:
            conn = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=5)
            conn.request("GET", "/health")
            response = conn.getresponse()
            payload = json.loads(response.read().decode("utf-8"))
            conn.close()
            self.assertTrue(payload["ok"])
            self.assertGreater(payload["cards"], 0)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()

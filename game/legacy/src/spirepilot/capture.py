"""Capture raw Communication_Mod_STS2 payloads for D1-D3 integration.

This is intentionally separate from the advice bridge: it only records what the
game sends and returns ``OK``.  Captured files are the input for field audits,
golden traces, and later replay evaluation.
"""

from __future__ import annotations

import json
import threading
import time
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any


def _kind(raw: Any) -> str:
    if not isinstance(raw, dict):
        return "unknown"
    if isinstance(raw.get("Player"), dict) and isinstance(raw.get("Piles"), dict) and isinstance(raw.get("Enemies"), list):
        return "combat"
    if isinstance(raw.get("Rewards"), list):
        return "reward"
    return "non_combat"


@dataclass
class CaptureStore:
    out_dir: Path
    session: str
    host: str = "127.0.0.1"
    port: int = 5000
    counts: dict[str, int] = field(
        default_factory=lambda: {
            "requests": 0,
            "stored": 0,
            "errors": 0,
            "combat": 0,
            "reward": 0,
            "non_combat": 0,
        },
    )
    last_trigger: str = ""
    quiet: bool = False
    started_at: str = field(default_factory=lambda: time.strftime("%Y-%m-%dT%H:%M:%S%z"))
    lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    @property
    def session_dir(self) -> Path:
        return self.out_dir / self.session

    @property
    def states_path(self) -> Path:
        return self.session_dir / "states.jsonl"

    @property
    def meta_path(self) -> Path:
        return self.session_dir / "meta.json"

    def prepare(self) -> None:
        self.session_dir.mkdir(parents=True, exist_ok=True)
        self._write_meta()

    def _write_meta(self) -> None:
        payload = {
            "session": self.session,
            "started_at": self.started_at,
            "host": self.host,
            "port": self.port,
            "counts": self.counts,
            "last_trigger": self.last_trigger,
        }
        self.meta_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

    def store(self, raw: Any) -> str:
        kind = _kind(raw)
        trigger = raw.get("Trigger", "") if isinstance(raw, dict) else ""
        record = {
            "ts": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "trigger": trigger,
            "kind": kind,
            "raw": raw,
        }
        with self.lock:
            self.counts["requests"] += 1
            try:
                with self.states_path.open("a", encoding="utf-8") as handle:
                    handle.write(json.dumps(record, ensure_ascii=False) + "\n")
                self.counts["stored"] += 1
                self.counts[kind] = self.counts.get(kind, 0) + 1
                self.last_trigger = str(trigger)
            except OSError:
                self.counts["errors"] += 1
            self._write_meta()
        return kind

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            return {
                "session": self.session,
                "path": str(self.states_path),
                "counts": dict(self.counts),
                "last_trigger": self.last_trigger,
            }


class CaptureHandler(BaseHTTPRequestHandler):
    server_version = "SpirePilotCapture/0.1"
    store: CaptureStore  # injected by make_capture_server

    def _send(self, status: int, body: str, content_type: str = "text/plain; charset=utf-8") -> None:
        data = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self) -> None:  # noqa: N802
        path = self.path.split("?")[0]
        if path == "/health":
            payload = {"ok": True, "service": "spirepilot-capture", **self.store.snapshot()}
            self._send(200, json.dumps(payload, ensure_ascii=False), "application/json; charset=utf-8")
            return
        if path == "/stats":
            self._send(200, json.dumps(self.store.snapshot(), ensure_ascii=False), "application/json; charset=utf-8")
            return
        self._send(404, "not found")

    def do_POST(self) -> None:  # noqa: N802
        if self.path.split("?")[0] != "/update_state":
            self._send(404, "not found")
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if length <= 0:
            self._send(400, "empty body")
            return
        raw_bytes = self.rfile.read(length)
        try:
            raw = json.loads(raw_bytes.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            with self.store.lock:
                self.store.counts["errors"] += 1
            self._send(200, "OK")
            return

        kind = self.store.store(raw)
        if kind == "combat" and not self.store.quiet:
            print(f"[capture] {self.store.last_trigger or 'combat'} -> {self.store.states_path}")
        self._send(200, "OK")

    def log_message(self, fmt: str, *args: Any) -> None:  # noqa: A003
        return


def load_capture_raw(path: str | Path, index: int = -1) -> dict[str, Any]:
    """Load one raw state from a capture JSONL file.

    ``index=-1`` means the last record.  Raises ``ValueError`` with a readable
    message so the CLI can fail cleanly.
    """

    records: list[dict[str, Any]] = []
    try:
        with Path(path).open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                item = json.loads(line)
                if isinstance(item, dict):
                    records.append(item)
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"cannot read capture {path}: {exc}") from exc
    if not records:
        raise ValueError(f"capture is empty: {path}")
    try:
        record = records[index]
    except IndexError as exc:
        raise ValueError(f"capture index {index} out of range (0..{len(records) - 1})") from exc
    raw = record.get("raw", record)
    if not isinstance(raw, dict):
        raise ValueError("capture record does not contain a JSON object")
    return raw


def make_capture_server(host: str, port: int, store: CaptureStore) -> ThreadingHTTPServer:
    store.prepare()
    handler = type("BoundCaptureHandler", (CaptureHandler,), {"store": store})
    server = ThreadingHTTPServer((host, port), handler)
    server.daemon_threads = True
    return server


def serve_capture(host: str, port: int, out_dir: str | Path, session: str | None = None) -> None:
    session = session or time.strftime("%Y%m%d-%H%M%S")
    store = CaptureStore(out_dir=Path(out_dir), session=session, host=host, port=port)
    server = make_capture_server(host, port, store)
    print(f"SpirePilot capture 已启动：http://{host}:{port}")
    print(f"会话：{session}")
    print(f"状态文件：{store.states_path}")
    print("只记录状态并回 OK，不执行动作；Ctrl-C 停止。")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n收到 Ctrl-C，关闭 capture。")
    finally:
        server.server_close()
        snapshot = store.snapshot()
        print(f"已保存 {snapshot['counts']['stored']} 条状态到 {snapshot['path']}")

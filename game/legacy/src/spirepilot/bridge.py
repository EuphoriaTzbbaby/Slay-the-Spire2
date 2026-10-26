"""Local HTTP bridge for Communication_Mod_STS2.

The mod POSTs a JSON state to ``/update_state`` and treats the response body as
a command.  In the default copilot mode this bridge always answers ``OK`` and
prints an advice block in the terminal; in ``--auto`` mode it answers with the
recommended combat action.
"""

from __future__ import annotations

import json
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

from .advisor import Advice, advice_to_dict, advise
from .adapter import UnsupportedStateError, is_combat_state, parse_combat_state
from .cards import CardDatabase
from .model import PlayCard
from .search import SearchConfig


def _action_payload(advice: Advice) -> dict[str, Any]:
    action = advice.action
    if isinstance(action, PlayCard):
        return {
            "Type": "PlayCard",
            "HandIndex": action.hand_index,
            "TargetIndex": action.target_index if action.target_index is not None else -1,
        }
    return {"Type": "EndTurn"}


def format_advice_text(advice: Advice, state=None, *, title: str = "SpirePilot 战斗建议") -> str:
    lines = [title, "=" * 48]
    if state is not None and getattr(state, "hand", None):
        lines.append("当前手牌：")
        for i, card in enumerate(state.hand):
            cost = card.cost
            lines.append(f"  [{i}] {card.display_name}  ({cost}费)")
        lines.append("")
    lines.append(f"下一步：{advice.label}")
    if isinstance(advice.action, PlayCard):
        lines.append(f"手牌索引：{advice.action.hand_index}")
    if len(advice.sequence_labels) > 1:
        lines.append("模拟连招：" + " → ".join(advice.sequence_labels[:6]))
    lines.append(f"评分：{advice.score:.2f}")
    if advice.stats:
        nodes = advice.stats.get("nodes", "?")
        elapsed = advice.stats.get("elapsed_ms", "?")
        solver = advice.stats.get("solver", "beam")
        complete = advice.stats.get("complete", True)
        lines.append(f"求解器：{solver} / {nodes} 节点 / {elapsed} ms / {'完整' if complete else '超时'}")
    if advice.reasons:
        lines.append("")
        lines.append("理由：")
        for i, reason in enumerate(advice.reasons, 1):
            lines.append(f"  {i}. {reason}")
    if advice.alternatives:
        lines.append("")
        lines.append("备选：")
        for alt in advice.alternatives[:5]:
            metrics = ""
            if alt.player_hp is not None:
                metrics = f"｜HP {alt.player_hp}"
                if alt.hp_lost is not None:
                    metrics += f"｜掉血 {alt.hp_lost}"
                if alt.enemy_hp is not None:
                    metrics += f"｜敌 {alt.enemy_hp}"
                if alt.lethal:
                    metrics += "｜击杀"
            lines.append(f"  - {alt.label}  (评分 {alt.score:.2f}){metrics}")
    if advice.warnings:
        lines.append("")
        lines.append("注意：")
        for warning in advice.warnings[:5]:
            lines.append(f"  - {warning}")
    return "\n".join(lines)


class BridgeState:
    def __init__(
        self,
        db: CardDatabase,
        config: SearchConfig,
        *,
        auto: bool = False,
        quiet: bool = False,
        log_path: str | Path | None = None,
    ):
        self.db = db
        self.config = config
        self.auto = auto
        self.quiet = quiet
        self.log_path = Path(log_path) if log_path else None
        self.lock = threading.Lock()
        self.requests = 0
        self.errors = 0
        self.last_advice: dict[str, Any] | None = None
        if self.log_path:
            self.log_path.parent.mkdir(parents=True, exist_ok=True)

    def handle(self, raw: Any) -> tuple[dict[str, Any] | str, int]:
        """Return ``(response_body, http_status)``."""

        self.requests += 1
        if not is_combat_state(raw):
            trigger = raw.get("Trigger", "non_combat") if isinstance(raw, dict) else "unknown"
            if not self.quiet:
                print(f"[bridge] 收到非战斗状态：{trigger}（仅记录，不决策）")
            return "OK", 200

        try:
            state = parse_combat_state(raw, self.db)
            advice = advise(state, self.config)
        except (UnsupportedStateError, ValueError, KeyError) as exc:
            self.errors += 1
            print(f"[bridge] 状态解析/建议失败：{exc}")
            return "OK", 200

        self.last_advice = advice_to_dict(advice)
        if not self.quiet:
            print()
            print(format_advice_text(advice, state))
        self._log(raw, advice)

        if self.auto:
            return _action_payload(advice), 200
        return "OK", 200

    def _log(self, raw: Any, advice: Advice) -> None:
        if not self.log_path:
            return
        record = {
            "ts": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "trigger": raw.get("Trigger") if isinstance(raw, dict) else None,
            "turn": raw.get("TurnNumber") if isinstance(raw, dict) else None,
            "advice": advice_to_dict(advice),
            "raw": raw,
        }
        with self.log_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(record, ensure_ascii=False) + "\n")


class BridgeHandler(BaseHTTPRequestHandler):
    server_version = "SpirePilotBridge/0.1"
    state: BridgeState  # injected by make_server

    def _send(self, status: int, body: str, content_type: str = "text/plain; charset=utf-8") -> None:
        payload = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self) -> None:  # noqa: N802 - http.server API
        if self.path.split("?")[0] == "/health":
            body = json.dumps(
                {
                    "ok": True,
                    "service": "spirepilot",
                    "auto": self.state.auto,
                    "requests": self.state.requests,
                    "errors": self.state.errors,
                    "cards": len(self.state.db),
                },
                ensure_ascii=False,
            )
            self._send(200, body, "application/json; charset=utf-8")
            return
        self._send(404, "not found")

    def do_POST(self) -> None:  # noqa: N802 - http.server API
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
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            self.state.errors += 1
            print(f"[bridge] JSON 解析失败：{exc}")
            self._send(200, "OK")
            return

        with self.state.lock:
            body, status = self.state.handle(raw)
        if isinstance(body, dict):
            self._send(status, json.dumps(body, ensure_ascii=False), "application/json; charset=utf-8")
        else:
            self._send(status, body)

    def log_message(self, fmt: str, *args: Any) -> None:  # noqa: A003
        # Keep the game's stdout clean; bridge messages are printed explicitly.
        return


def make_server(
    host: str,
    port: int,
    db: CardDatabase,
    config: SearchConfig,
    *,
    auto: bool = False,
    quiet: bool = False,
    log_path: str | Path | None = "logs/bridge.jsonl",
) -> ThreadingHTTPServer:
    bridge_state = BridgeState(db, config, auto=auto, quiet=quiet, log_path=log_path)
    handler = type("BoundBridgeHandler", (BridgeHandler,), {"state": bridge_state})
    server = ThreadingHTTPServer((host, port), handler)
    server.daemon_threads = True
    return server


def serve(
    host: str,
    port: int,
    db: CardDatabase,
    config: SearchConfig,
    *,
    auto: bool = False,
    quiet: bool = False,
    log_path: str | Path | None = "logs/bridge.jsonl",
) -> None:
    server = make_server(host, port, db, config, auto=auto, quiet=quiet, log_path=log_path)
    mode = "自动出牌" if auto else "copilot（只建议，不执行）"
    print(f"SpirePilot bridge 已启动：http://{host}:{port}")
    print(f"模式：{mode}")
    print(f"卡牌库：{len(db)} 张（{db.path or '未加载'}）")
    print("等待 Communication_Mod_STS2 POST /update_state ...")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n收到 Ctrl-C，关闭 bridge。")
    finally:
        server.server_close()

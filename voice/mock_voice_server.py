"""Minimal mock voice service for stage 2 (voice -> text chat).

Implements just enough of protocol v1: session.hello, input.final events and
input.ack logging. Type a sentence in the terminal and it is delivered to the
mod as if ASR had produced it.

Usage:
    python3 voice/mock_voice_server.py            # interactive
    python3 voice/mock_voice_server.py --demo     # send three demo inputs
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path

import websockets

CLIENTS: set = set()
TOKEN_FILE = Path.home() / "Library/Application Support/SlayTheSpire2/spire-companion/voice-session.json"
ACTIVE_TOKEN_FILE = TOKEN_FILE
INTENTS = {
    "暂停": "pause", "等一下": "pause", "停": "pause",
    "恢复": "resume", "继续": "resume", "继续操作": "resume",
    "到你了": "handoff", "你来打": "handoff", "你来": "handoff",
}
SEQ = 0


def expected_token() -> str | None:
    try:
        data = json.loads(ACTIVE_TOKEN_FILE.read_text(encoding="utf-8"))
        return data.get("token")
    except Exception:
        return None


async def send_event(text: str) -> None:
    global SEQ
    if not CLIENTS:
        print("[mock] 还没有游戏连接，先在游戏里打开「语音输入：开」")
        return
    SEQ += 1
    payload = {
        "version": 1, "kind": "event", "event_id": f"evt-{SEQ}", "session_id": "mock-voice",
        "seq": SEQ, "name": "input.final",
        "data": {"input_id": f"in-{SEQ}", "text": text, "language": "zh-CN", "intent": INTENTS.get(text)},
    }
    raw = json.dumps(payload, ensure_ascii=False)
    for ws in list(CLIENTS):
        try:
            await ws.send(raw)
        except Exception:
            CLIENTS.discard(ws)
    print(f"[mock] -> input.final {text} (intent={INTENTS.get(text)})")


async def handler(ws) -> None:
    CLIENTS.add(ws)
    print("[mock] 游戏已连接")
    try:
        async for raw in ws:
            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                continue
            if msg.get("kind") == "request":
                method = msg.get("method")
                rid = msg.get("id")
                if method == "session.hello":
                    token = (msg.get("params") or {}).get("token")
                    want = expected_token()
                    ok = want is None or token == want
                    if ok:
                        reply = {"version": 1, "kind": "response", "id": rid, "ok": True,
                                 "result": {"session_id": "mock-voice", "features": ["asr"]}}
                    else:
                        reply = {"version": 1, "kind": "response", "id": rid, "ok": False,
                                 "error": {"code": "auth", "message": "token mismatch", "retryable": False}}
                    await ws.send(json.dumps(reply))
                    print(f"[mock] hello ok={ok}")
                elif method == "input.ack":
                    print(f"[mock] ack {msg.get('params')}")
                elif method == "service.ping":
                    await ws.send(json.dumps({"version": 1, "kind": "response", "id": rid, "ok": True, "result": {}}))
    finally:
        CLIENTS.discard(ws)
        print("[mock] 连接断开")


async def console(demo: bool) -> None:
    if demo:
        await asyncio.sleep(1.0)
        for text in ("你好", "暂停", "恢复"):
            await send_event(text)
            await asyncio.sleep(0.5)
        return
    while True:
        try:
            line = await asyncio.to_thread(input, "说话(输入文字回车)> ")
        except EOFError:
            break
        text = line.strip()
        if not text:
            continue
        if text in ("exit", "quit"):
            break
        await send_event(text)


async def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=17860)
    parser.add_argument("--demo", action="store_true")
    parser.add_argument("--token-file", default=str(TOKEN_FILE))
    args = parser.parse_args()
    global ACTIVE_TOKEN_FILE
    ACTIVE_TOKEN_FILE = Path(args.token_file)
    want = expected_token()
    print(f"[mock] token file: {ACTIVE_TOKEN_FILE} ({'已读取' if want else '不存在，接受任意 token'})")
    async with websockets.serve(handler, "127.0.0.1", args.port):
        print(f"[mock] 监听 ws://127.0.0.1:{args.port}")
        await console(args.demo)
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))

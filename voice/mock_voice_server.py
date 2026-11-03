"""Protocol-only voice simulator: terminal input and printed TTS, never microphone access."""
from __future__ import annotations

import argparse
import asyncio
from pathlib import Path
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))
from spire_voice.common import default_token_file, intent_for
from spire_voice.service import VoiceServer
from websockets.asyncio.server import serve


class MockMicrophone:
    def start(self): pass
    def stop(self): pass
    def reset(self): pass
    def mute(self, value): pass

    def next_result(self):
        time.sleep(0.1)
        return None


class MockSpeech:
    async def speak(self, text):
        print(f"[mock] 模拟播报（无声音）：{text}", flush=True)
        await asyncio.sleep(0.05)


async def send_input(server, text):
    session = server.owner
    if session is None or not session.authenticated or session.recognition is None:
        print("[mock] 先在游戏按 QQ 或点「开始语音」，等待显示持续监听。", flush=True)
        return
    await session.event("input.final", input_id="mock-" + uuid.uuid4().hex,
                        text=text, language="zh-CN", intent=intent_for(text))
    print("[mock] 已发送 input.final", flush=True)


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=17860)
    parser.add_argument("--token-file", type=Path, default=default_token_file())
    parser.add_argument("--demo", action="store_true")
    args = parser.parse_args()
    server = VoiceServer(args.token_file, MockMicrophone(), MockSpeech())
    async with serve(server.handler, "127.0.0.1", args.port, max_size=65536):
        print(f"[mock] 监听 ws://127.0.0.1:{args.port}；需要游戏会话鉴权，不采集或播放音频。", flush=True)
        if args.demo:
            async with asyncio.timeout(30):
                while server.owner is None or server.owner.recognition is None:
                    await asyncio.sleep(0.1)
            for text in ("你好", "这手牌怎么打", "请简单回答"):
                await send_input(server, text)
                await asyncio.sleep(1)
            return
        try:
            while True:
                try:
                    text = (await asyncio.to_thread(input, "模拟说话（exit 退出）> ")).strip()
                except EOFError:
                    break
                if text in ("exit", "quit"):
                    break
                if text:
                    await send_input(server, text)
        finally:
            if server.owner:
                await server.owner.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
    except TimeoutError:
        sys.exit("[mock] 等待游戏连接超时。")

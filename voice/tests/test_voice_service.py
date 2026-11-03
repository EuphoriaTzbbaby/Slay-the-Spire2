import asyncio
import json
import os
from pathlib import Path
import queue
import secrets
import sys
import tempfile
import unittest
from unittest.mock import patch, AsyncMock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from spire_voice.common import intent_for, speech_text
from spire_voice.service import VoiceServer
from spire_voice.audio import SystemSpeech
from websockets.asyncio.client import connect
from websockets.asyncio.server import serve

ROOT = Path(__file__).resolve().parents[2]


class FakeMicrophone:
    def __init__(self):
        self.started = 0
        self.stopped = 0
        self.muted = False
        self.chunks = queue.Queue()
        self.resets = 0

    def start(self):
        self.started += 1
        self.drain()
    def stop(self):
        self.stopped += 1
        self.drain()
    def reset(self): self.resets += 1
    def mute(self, value): self.muted = value

    def drain(self):
        while not self.chunks.empty():
            try: self.chunks.get_nowait()
            except queue.Empty: break

    def next_result(self):
        try: return self.chunks.get(timeout=0.02)
        except queue.Empty: return None


class FakeSpeech:
    def __init__(self):
        self.calls = asyncio.Queue()
        self.cancelled = 0
        self.gates = []

    async def speak(self, text):
        gate = asyncio.Event()
        self.gates.append(gate)
        await self.calls.put(text)
        try: await gate.wait()
        except asyncio.CancelledError:
            self.cancelled += 1
            raise


class TextTests(unittest.TestCase):
    def test_commands_only_match_complete_phrases(self):
        self.assertEqual(intent_for("暂 停。"), "pause")
        self.assertEqual(intent_for("到 你 了"), "handoff")
        self.assertIsNone(intent_for("先别暂停"))
        self.assertIsNone(intent_for("暂停之后怎么办"))

    def test_speech_keeps_names_and_numbers_without_markup(self):
        text = speech_text("## 建议\n**打击**造成 6 点伤害。[卡牌](https://example.com) `c1@enemy` [b]防御[/b]。")
        self.assertEqual(text, "建议 打击造成 6 点伤害。卡牌 防御。")


class AudioStatusTests(unittest.IsolatedAsyncioTestCase):
    async def test_muted_system_reports_error_instead_of_false_success(self):
        with patch("spire_voice.audio.system_audio_status", return_value={"output_muted": True}), \
                patch("asyncio.create_subprocess_exec", new_callable=AsyncMock) as process:
            with self.assertRaisesRegex(RuntimeError, "静音"):
                await SystemSpeech().speak("不会静默报成功")
            process.assert_not_called()


class ServiceTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.token_file = Path(self.temp.name) / "session.json"
        self.token_file.write_text(json.dumps({"token": "test-session-token"}))
        self.mic, self.speaker = FakeMicrophone(), FakeSpeech()
        self.bridge = VoiceServer(self.token_file, self.mic, self.speaker)
        self.server = await serve(self.bridge.handler, "127.0.0.1", 0)
        self.url = f"ws://127.0.0.1:{self.server.sockets[0].getsockname()[1]}"
        self.ws = await connect(self.url)
        self.events = []

    async def asyncTearDown(self):
        await self.ws.close()
        self.server.close()
        await self.server.wait_closed()
        self.temp.cleanup()

    async def request(self, method, params=None, rid=None, version=1):
        rid = rid or secrets.token_hex(8)
        await self.ws.send(json.dumps({"version": version, "kind": "request", "id": rid,
                                       "method": method, "params": {} if params is None else params}))
        async with asyncio.timeout(3):
            while True:
                message = json.loads(await self.ws.recv())
                if message["kind"] == "response" and message["id"] == rid:
                    return message
                self.events.append(message)

    async def hello(self):
        result = await self.request("session.hello", {"token": "test-session-token", "protocol_version": 1})
        self.assertTrue(result["ok"])
        return result["result"]

    async def event(self, name, uid=None):
        async with asyncio.timeout(3):
            while True:
                for index, message in enumerate(self.events):
                    if message.get("name") == name and (uid is None or message["data"].get("utterance_id") == uid):
                        return self.events.pop(index)
                self.events.append(json.loads(await self.ws.recv()))

    async def enqueue(self, uid, text=None, **options):
        return await self.request("speech.enqueue", {"utterance_id": uid, "text": text or uid,
                                  "priority": "normal", "ttl_ms": 5000, **options})

    async def test_authentication_precedes_audio(self):
        result = await self.request("listening.start")
        self.assertEqual(result["error"]["code"], "auth_failed")
        self.assertEqual(self.mic.started, 0)
        self.assertIsNone(self.bridge.owner)

    async def test_missing_token_file_never_accepts_any_token(self):
        self.token_file.unlink()
        result = await self.request("session.hello", {"token": "anything", "protocol_version": 1})
        self.assertFalse(result["ok"])
        self.assertEqual(self.mic.started, 0)

    async def test_second_game_cannot_take_over_microphone(self):
        await self.hello()
        async with connect(self.url) as other:
            await other.send(json.dumps({"version": 1, "kind": "request", "id": "other", "method": "session.hello",
                                         "params": {"token": "test-session-token", "protocol_version": 1}}))
            result = json.loads(await other.recv())
            self.assertEqual(result["error"]["code"], "session_busy")
        self.assertEqual(self.mic.stopped, 0)

    async def test_real_input_contract_and_fixed_intents(self):
        await self.hello()
        self.assertTrue((await self.request("listening.start"))["ok"])
        await self.request("speech.interrupt")
        self.mic.chunks.put("先 别 暂 停")
        result = await self.event("input.final")
        self.assertEqual(result["data"]["text"], "先别暂停")
        self.assertIsNone(result["data"]["intent"])
        self.assertTrue((await self.request("input.ack", {"input_id": result["data"]["input_id"], "status": "accepted"}))["ok"])
        self.mic.chunks.put("搭 子 暂 停")
        second = await self.event("input.final")
        self.assertEqual(second["data"]["intent"], "pause")
        self.assertNotEqual(result["data"]["input_id"], second["data"]["input_id"])

    async def test_continuous_accepts_multiple_sentences_without_wake_or_rearming(self):
        await self.hello()
        for _ in range(2):
            result = await self.request("listening.start", {"mode": "continuous"})
            self.assertTrue(result["ok"])
            self.assertEqual(result["result"]["listening_mode"], "continuous")
        self.assertEqual(self.mic.started, 1)
        self.mic.chunks.put("这 是 第 一 句")
        first = await self.event("input.final")
        self.mic.chunks.put("这 是 第 二 句")
        second = await self.event("input.final")
        self.assertEqual(first["data"]["text"], "这是第一句")
        self.assertEqual(second["data"]["text"], "这是第二句")
        self.assertNotEqual(first["data"]["input_id"], second["data"]["input_id"])
        self.assertEqual((await self.request("service.status"))["result"]["listening"], "continuous")
        self.assertEqual(self.mic.resets, 0, "Finalizing one sentence must not discard subsequent PCM")

    async def test_continuous_silence_keeps_listening_without_empty_input(self):
        await self.hello()
        await self.request("listening.start", {"mode": "continuous"})
        self.assertFalse(any(e.get("name") in ("input.final", "recognition.timeout") for e in self.events))
        with self.assertRaises(asyncio.TimeoutError):
            await asyncio.wait_for(self.ws.recv(), 0.1)
        self.assertEqual((await self.request("service.status"))["result"]["listening"], "continuous")

    async def test_stop_cancels_unfinished_input_and_fresh_start_drops_old_audio(self):
        await self.hello()
        await self.request("listening.start", {"mode": "continuous"})
        await self.request("speech.interrupt")
        started = await self.event("recognition.started")
        stopped = await self.request("listening.stop")
        self.assertEqual(stopped["result"]["listening"], "disabled")
        self.assertIsNone(stopped["result"]["listening_mode"])
        cancelled = await self.event("recognition.cancelled")
        self.assertEqual(cancelled["data"]["input_id"], started["data"]["input_id"])
        self.mic.chunks.put("关闭期间的旧音频")
        await self.request("listening.start", {"mode": "continuous"})
        self.mic.chunks.put("重新开启后的新句子")
        result = await self.event("input.final")
        self.assertEqual(result["data"]["text"], "重新开启后的新句子")
        self.assertGreater(self.mic.stopped, 0)

    async def test_invalid_listening_mode_does_not_change_live_session(self):
        await self.hello()
        await self.request("listening.start", {"mode": "continuous"})
        for mode in ("", "always", None, 12):
            result = await self.request("listening.start", {"mode": mode})
            self.assertEqual(result["error"]["code"], "invalid_params")
        self.assertEqual((await self.request("service.status"))["result"]["listening_mode"], "continuous")
        self.assertEqual(self.mic.started, 1)

    async def test_queue_urgent_interrupt_and_terminal_events(self):
        await self.hello()
        await self.enqueue("first")
        self.assertEqual(await asyncio.wait_for(self.speaker.calls.get(), 2), "first")
        await self.enqueue("queued")
        await self.enqueue("urgent", priority="urgent")
        event = await self.event("speech.cancelled", "first")
        self.assertEqual(event["data"]["reason"], "urgent")
        self.assertEqual(await asyncio.wait_for(self.speaker.calls.get(), 2), "urgent")
        self.speaker.gates[-1].set()
        self.assertEqual(await asyncio.wait_for(self.speaker.calls.get(), 2), "queued")
        await self.request("speech.clear", {"include_current": True})
        await self.event("speech.cancelled", "queued")
        self.assertGreaterEqual(self.speaker.cancelled, 2)
        self.assertFalse(self.mic.muted)

    async def test_expired_speech_is_not_played(self):
        await self.hello()
        await self.enqueue("blocking")
        await asyncio.wait_for(self.speaker.calls.get(), 2)
        await self.enqueue("stale", ttl_ms=1)
        self.speaker.gates[0].set()
        result = await self.event("speech.cancelled", "stale")
        self.assertEqual(result["data"]["reason"], "expired")
        self.assertTrue(self.speaker.calls.empty())

    async def test_replayed_requests_and_utterances_are_not_repeated(self):
        await self.hello()
        params = {"utterance_id": "same", "text": "一次", "priority": "normal", "ttl_ms": 5000}
        first = await self.request("speech.enqueue", params, rid="same-request")
        again = await self.request("speech.enqueue", params, rid="same-request")
        self.assertEqual(first, again)
        self.assertTrue((await self.request("speech.enqueue", params))["result"]["duplicate"])
        changed = await self.request("speech.enqueue", {**params, "text": "不应执行"}, rid="same-request")
        self.assertEqual(changed["error"]["code"], "request_id_reused")
        self.assertEqual(await asyncio.wait_for(self.speaker.calls.get(), 2), "一次")
        self.assertTrue(self.speaker.calls.empty())

    async def test_microphone_disabled_rejects_interrupt_and_invalid_params(self):
        await self.hello()
        self.assertEqual((await self.request("speech.interrupt"))["error"]["code"], "microphone_disabled")
        self.assertFalse((await self.enqueue("bad", ttl_ms=True))["ok"])
        self.assertFalse((await self.request("settings.update", {"wake_word": "anything"}))["ok"])

    async def test_disconnect_stops_audio_and_does_not_replay_on_reconnect(self):
        await self.hello()
        await self.request("listening.start", {"mode": "continuous"})
        await self.enqueue("old")
        await asyncio.wait_for(self.speaker.calls.get(), 2)
        await self.ws.close()
        async with asyncio.timeout(3):
            while self.bridge.owner:
                await asyncio.sleep(0.02)
        self.assertGreater(self.mic.stopped, 0)
        self.assertEqual(self.speaker.cancelled, 1)
        self.ws = await connect(self.url)
        result = await self.hello()
        self.assertEqual(result["queue_length"], 0)
        self.assertEqual(result["listening"], "disabled")


@unittest.skipUnless(os.environ.get("SPIRE_VOICE_WIRE_CHECK") == "1", "set SPIRE_VOICE_WIRE_CHECK=1 after building C# checks")
class CSharpWireTests(unittest.IsolatedAsyncioTestCase):
    async def test_cross_language_client(self):
        sessions = 0
        requests = []

        async def handler(ws):
            nonlocal sessions
            sessions += 1
            session = f"wire-{sessions}"
            continuous_attempts = 0
            try:
                async for raw in ws:
                    request = json.loads(raw)
                    method = request["method"]
                    requests.append(method)
                    if method == "session.hello" and sessions == 1:
                        await ws.send(json.dumps({"version": 1, "kind": "response", "id": request["id"], "ok": False,
                                                  "error": {"code": "auth_failed", "message": "test rejection", "retryable": False}}))
                        continue
                    result = {"session_id": session} if method == "session.hello" else {}
                    if method == "listening.start" and request["params"].get("mode") == "continuous":
                        continuous_attempts += 1
                        # First mimic an old server silently ignoring the new mode.
                        if continuous_attempts > 1:
                            result = {"listening_mode": "continuous", "listening": "continuous"}
                    if method == "listening.stop":
                        result = {"listening": "disabled", "listening_mode": None}
                    await ws.send(json.dumps({"version": 1, "kind": "response", "id": request["id"], "ok": True, "result": result}))
                    if method == "speech.enqueue":
                        for name, data in [("service.error", {"code": "synthesis_failed", "message": "测试播报失败", "retryable": True}),
                                           ("state.changed", {"playback": "idle", "listening": "waiting_wake"})]:
                            await ws.send(json.dumps({"version": 1, "kind": "event", "event_id": name, "session_id": session,
                                                      "seq": 10, "name": name, "data": data}, ensure_ascii=False))
                    if method == "listening.start" and request["params"].get("mode") != "continuous":
                        def event(i, text, intent=None, sid=session):
                            return json.dumps({"version": 1, "kind": "event", "event_id": str(i), "session_id": sid,
                                               "seq": i, "name": "input.final", "data": {"input_id": f"same-{i}", "text": text, "intent": intent}}, ensure_ascii=False)
                        raw = event(1, "这手牌怎么打？").encode()
                        split = raw.index("这".encode()) + 1
                        await ws.send([raw[:split], raw[split:]], text=True)
                        await ws.send(event(1, "这手牌怎么打？"))
                        await ws.send(event(3, "错误会话", sid="wrong-session"))
                        await ws.send(event(2, "暂停", "pause"))
            except Exception:
                pass

        async with serve(handler, "127.0.0.1", 0) as server:
            port = server.sockets[0].getsockname()[1]
            assembly = ROOT / "game/legacy/SpireCompanion.Checks/bin/Release/net9.0/SpireCompanion.Checks.dll"
            process = await asyncio.create_subprocess_exec("dotnet", str(assembly), "--voice-client-check", str(port), "wire-test-token",
                                                           stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
            try:
                output, _ = await asyncio.wait_for(process.communicate(), 30)
            finally:
                if process.returncode is None:
                    process.kill()
                    await process.wait()
            self.assertEqual(process.returncode, 0, output.decode(errors="replace"))
            print(output.decode().strip())
            self.assertGreaterEqual(requests.count("input.ack"), 4)
            self.assertEqual(requests.count("speech.enqueue"), 2)

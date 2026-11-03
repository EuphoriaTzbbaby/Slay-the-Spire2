from __future__ import annotations

import asyncio
import hmac
import hashlib
import json
import time
import uuid
from collections import OrderedDict, deque
from dataclasses import dataclass
from pathlib import Path

from .common import compact, intent_for, speech_text


class ProtocolError(Exception):
    def __init__(self, code: str, message: str):
        self.code, self.message = code, message


@dataclass
class Utterance:
    id: str
    text: str
    group: str
    deadline: float
    reason: str = "cancelled"
    terminal: bool = False


class VoiceServer:
    def __init__(self, token_file: Path, microphone, speaker):
        self.token_file = token_file
        self.microphone = microphone
        self.speaker = speaker
        self.owner: VoiceSession | None = None

    def authenticate(self, token) -> bool:
        try:
            expected = json.loads(self.token_file.read_text(encoding="utf-8"))["token"]
            return (isinstance(expected, str) and bool(expected) and isinstance(token, str)
                    and hmac.compare_digest(expected.encode(), token.encode()))
        except (OSError, ValueError, KeyError, TypeError):
            return False

    async def handler(self, socket):
        session = VoiceSession(self, socket)
        try:
            async for raw in socket:
                await session.receive(raw)
        except Exception as error:
            # No raw request/response bodies or tokens in logs.
            print(f"[voice] 连接结束：{type(error).__name__}", flush=True)
        finally:
            await session.close()


class VoiceSession:
    def __init__(self, server: VoiceServer, socket):
        self.server, self.socket = server, socket
        self.id = "voice-" + uuid.uuid4().hex
        self.seq = 0
        self.authenticated = False
        self.closed = False
        self.send_lock = asyncio.Lock()
        self.responses: OrderedDict[str, tuple[str, dict]] = OrderedDict()
        self.request_ids: set[str] = set()
        self.microphone = server.microphone
        self.speaker = server.speaker
        self.recognition: asyncio.Task | None = None
        self.player: asyncio.Task | None = None
        self.playback: asyncio.Task | None = None
        self.current: Utterance | None = None
        self.queue: deque[Utterance] = deque()
        self.utterance_ids: set[str] = set()
        self.available = asyncio.Event()
        self.input_id: str | None = None
        self.input_deadline = 0.0
        self.listening_mode = "wake"

    async def send(self, payload):
        async with self.send_lock:
            await self.socket.send(json.dumps(payload, ensure_ascii=False))

    async def event(self, name: str, **data):
        async with self.send_lock:
            self.seq += 1
            await self.socket.send(json.dumps({
                "version": 1, "kind": "event", "event_id": f"{self.id}-{self.seq}",
                "session_id": self.id, "seq": self.seq, "name": name, "data": data,
            }, ensure_ascii=False))

    def status(self):
        return {"service": "ready", "listening": "disabled" if self.recognition is None else
                "continuous" if self.listening_mode == "continuous" else "listening" if self.input_id else "waiting_wake",
                "listening_mode": self.listening_mode if self.recognition is not None else None,
                "playback": "playing" if self.current else "idle", "queue_length": len(self.queue),
                "current_utterance_id": self.current.id if self.current else None,
                "echo_guard": "mute_during_playback"}

    async def state(self):
        await self.event("state.changed", **self.status())

    async def receive(self, raw):
        try:
            msg = json.loads(raw)
        except (ValueError, TypeError):
            return
        if not isinstance(msg, dict) or msg.get("kind") != "request":
            return
        rid = msg.get("id")
        if not isinstance(rid, str) or not 0 < len(rid) <= 128:
            return
        fingerprint = hashlib.sha256(json.dumps(msg, sort_keys=True).encode()).hexdigest()
        if rid in self.responses:
            previous, response = self.responses[rid]
            if previous == fingerprint:
                await self.send(response)
                return
            await self.send({"version": 1, "kind": "response", "id": rid, "ok": False,
                             "error": {"code": "request_id_reused", "message": "请求 ID 已用于其他内容。", "retryable": False}})
            return
        response = {"version": 1, "kind": "response", "id": rid}
        auth_failure = False
        try:
            if rid in self.request_ids or len(self.request_ids) >= 8192:
                raise ProtocolError("request_expired", "请求已过期，请使用新会话；不会重放旧操作。")
            if msg.get("version") != 1:
                raise ProtocolError("protocol_incompatible", "只支持协议版本 1。")
            params = msg.get("params")
            if not isinstance(params, dict):
                raise ProtocolError("invalid_params", "params 必须为对象。")
            result = await self.request(msg.get("method"), params)
            response.update(ok=True, result=result)
        except ProtocolError as error:
            response.update(ok=False, error={"code": error.code, "message": error.message, "retryable": False})
            auth_failure = error.code == "auth_failed"
        except Exception as error:
            response.update(ok=False, error={"code": "audio_unavailable", "message": str(error)[:240], "retryable": True})
        self.request_ids.add(rid)
        self.responses[rid] = (fingerprint, response)
        if len(self.responses) > 256:
            self.responses.popitem(last=False)
        await self.send(response)
        if auth_failure:
            await self.socket.close(code=1008, reason="Authentication required")

    async def request(self, method, params):
        if method == "session.hello":
            if params.get("protocol_version") != 1:
                raise ProtocolError("protocol_incompatible", "只支持协议版本 1。")
            if not self.server.authenticate(params.get("token")):
                raise ProtocolError("auth_failed", "会话鉴权失败；请在游戏中开启语音，或检查 --token-file。")
            if self.server.owner not in (None, self):
                raise ProtocolError("session_busy", "语音服务已连接另一个游戏实例。")
            self.server.owner = self
            self.authenticated = True
            if self.player is None:
                self.player = asyncio.create_task(self.play_loop())
            return {"session_id": self.id, "features": ["asr", "tts", "push_to_talk", "continuous_listening"], **self.status()}
        if not self.authenticated:
            raise ProtocolError("auth_failed", "请先完成 session.hello。")
        if method in ("service.status", "service.ping"):
            return self.status()
        if method == "listening.start":
            mode = params.get("mode", "wake")
            if mode not in ("wake", "continuous"):
                raise ProtocolError("invalid_params", "mode 只支持 wake 或 continuous。")
            if self.recognition is not None and self.listening_mode != mode:
                await self.stop_listening()
            self.listening_mode = mode
            if self.recognition is None:
                await asyncio.to_thread(self.microphone.start)
                self.recognition = asyncio.create_task(self.recognize_loop())
            await self.state()
            return self.status()
        if method == "listening.stop":
            await self.stop_listening()
            return self.status()
        if method == "speech.interrupt":
            if self.recognition is None:
                raise ProtocolError("microphone_disabled", "请先开启语音输入。")
            await self.clear(True, "interrupted")
            await self.begin_input()
            return self.status()
        if method == "recognition.cancel":
            if self.input_id and params.get("input_id") == self.input_id:
                await self.event("recognition.cancelled", input_id=self.input_id, reason="cancelled")
                self.input_id = None
                self.microphone.reset()
                await self.state()
            return self.status()
        if method == "input.ack":
            if params.get("status") not in ("accepted", "rejected", "ignored") or not isinstance(params.get("input_id"), str):
                raise ProtocolError("invalid_params", "需要 input_id 和有效 status。")
            print(f"[voice] input.ack {params['status']}", flush=True)
            return {}
        if method == "speech.enqueue":
            return await self.enqueue(params)
        if method == "speech.clear":
            if type(params.get("include_current", False)) is not bool:
                raise ProtocolError("invalid_params", "include_current 必须为布尔值。")
            await self.clear(params.get("include_current", False), "cleared")
            return self.status()
        if method == "speech.cancel":
            uid, group = params.get("utterance_id"), params.get("group_id")
            if not any(isinstance(value, str) and value for value in (uid, group)):
                raise ProtocolError("invalid_params", "需要 utterance_id 或 group_id。")
            matches = lambda job: job.id == uid or bool(group) and job.group == group
            cancelled = [job for job in self.queue if matches(job)]
            self.queue = deque(job for job in self.queue if not matches(job))
            for job in cancelled:
                await self.terminal(job, "speech.cancelled", reason="cancelled")
            if self.current and matches(self.current):
                await self.cancel_current("cancelled")
            return self.status()
        if method == "settings.get":
            return {"language": "zh-CN", "wake_word": "搭子", "echo_guard": "mute_during_playback",
                    "adjustable": [], "note": "基本调试版使用系统中文音色；当前游戏侧默认文字回复。"}
        raise ProtocolError("method_not_supported", f"基本调试版暂不支持 {method}。")

    async def begin_input(self, wake=False, reset_audio=True):
        if self.input_id:
            await self.event("recognition.cancelled", input_id=self.input_id, reason="replaced")
        self.input_id = "input-" + uuid.uuid4().hex
        self.input_deadline = time.monotonic() + 15
        if reset_audio:
            self.microphone.reset()
        if wake:
            await self.event("wake.detected", input_id=self.input_id, wake_word="搭子")
        await self.event("recognition.started", input_id=self.input_id)
        await self.state()

    async def recognize_loop(self):
        try:
            while True:
                text = await asyncio.to_thread(self.microphone.next_result)
                if self.input_id and time.monotonic() > self.input_deadline:
                    await self.event("recognition.timeout", input_id=self.input_id, reason="no_speech_or_too_long")
                    self.input_id = None
                    self.microphone.reset()
                    await self.state()
                if not text or self.current is not None:
                    continue
                text = compact(text)
                if not text:
                    continue
                if self.input_id is None:
                    if self.listening_mode == "continuous":
                        # Vosk has ended this utterance. Keep subsequent PCM for the next one.
                        await self.begin_input(reset_audio=False)
                    else:
                        if not text.startswith("搭子"):
                            continue
                        await self.clear(True, "wake")
                        await self.begin_input(wake=True)
                        text = text[2:]
                if text:
                    await self.event("input.final", input_id=self.input_id, text=text,
                                     language="zh-CN", intent=intent_for(text))
                    self.input_id = None
                    await self.state()
        except asyncio.CancelledError:
            raise
        except Exception as error:
            await self.event("service.error", code="recognition_failed", message=str(error)[:240],
                             retryable=True, input_id=self.input_id)
            self.input_id = None
            self.recognition = None
            await asyncio.to_thread(self.microphone.stop)
            await self.state()

    async def stop_listening(self):
        task, self.recognition = self.recognition, None
        if task:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
        await asyncio.to_thread(self.microphone.stop)
        if self.input_id:
            await self.event("recognition.cancelled", input_id=self.input_id, reason="microphone_disabled")
            self.input_id = None
        await self.state()

    async def enqueue(self, params):
        uid, text = params.get("utterance_id"), params.get("text")
        ttl, priority = params.get("ttl_ms"), params.get("priority")
        group = params.get("group_id", "")
        if (not isinstance(uid, str) or not 0 < len(uid) <= 128 or not isinstance(text, str)
                or not text.strip() or len(text) > 8000 or type(ttl) is not int or not 0 < ttl <= 120000
                or priority not in ("normal", "urgent") or not isinstance(group, str)):
            raise ProtocolError("invalid_params", "播报需要有效 utterance_id、text、priority 和 ttl_ms（1–120000）。")
        if uid in self.utterance_ids:
            return {"accepted": True, "utterance_id": uid, "duplicate": True}
        if len(self.queue) >= 16 or len(self.utterance_ids) >= 4096:
            raise ProtocolError("queue_full", "播报队列已满，请清空或重连。")
        self.utterance_ids.add(uid)
        job = Utterance(uid, speech_text(text), group, time.monotonic() + ttl / 1000)
        if priority == "urgent":
            self.queue.appendleft(job)
            await self.cancel_current("urgent")
        else:
            self.queue.append(job)
        self.available.set()
        return {"accepted": True, "utterance_id": uid}

    async def cancel_current(self, reason):
        if self.current and self.playback:
            job, task = self.current, self.playback
            job.reason = reason
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await self.terminal(job, "speech.cancelled", reason=reason)

    async def terminal(self, job, name, **data):
        if job is not None and not job.terminal:
            job.terminal = True
            await self.event(name, utterance_id=job.id, **data)

    async def clear(self, include_current, reason):
        cancelled = list(self.queue)
        self.queue.clear()
        for job in cancelled:
            await self.terminal(job, "speech.cancelled", reason=reason)
        if include_current:
            await self.cancel_current(reason)

    async def play_loop(self):
        while not self.closed:
            await self.available.wait()
            while self.queue and not self.closed:
                job = self.queue.popleft()
                self.current = job
                self.playback = asyncio.create_task(self.play(job))
                await asyncio.gather(self.playback, return_exceptions=True)
                self.current, self.playback = None, None
                if not self.closed:
                    await self.state()
            self.available.clear()

    async def play(self, job):
        try:
            remaining = job.deadline - time.monotonic()
            if remaining <= 0 or not job.text:
                await self.terminal(job, "speech.cancelled", reason="expired" if remaining <= 0 else "empty")
                return
            self.microphone.mute(True)
            await self.event("speech.started", utterance_id=job.id)
            await self.state()
            await asyncio.wait_for(self.speaker.speak(job.text), remaining)
            await self.terminal(job, "speech.completed")
        except asyncio.TimeoutError:
            await self.terminal(job, "speech.cancelled", reason="expired")
        except asyncio.CancelledError:
            await self.terminal(job, "speech.cancelled", reason=job.reason)
        except Exception as error:
            await self.terminal(job, "service.error", code="synthesis_failed", message=str(error)[:240], retryable=True)
        finally:
            # Discard speaker echo and buffered audio; the basic version uses half duplex.
            try:
                await asyncio.sleep(0.2)
            finally:
                self.microphone.mute(False)

    async def close(self):
        self.closed = True
        if self.server.owner is not self:
            return
        # Disconnect cleanup must still finish when sending terminal events is impossible.
        try:
            await self.stop_listening()
        except Exception:
            pass
        self.queue.clear()
        if self.playback:
            self.playback.cancel()
            await asyncio.gather(self.playback, return_exceptions=True)
        if self.player:
            self.player.cancel()
            await asyncio.gather(self.player, return_exceptions=True)
        self.server.owner = None

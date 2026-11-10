from __future__ import annotations

import asyncio
import queue
import sys
import threading
import time
from array import array
from collections import deque
from pathlib import Path
from .utterance import AudioEvent, UtteranceDecoder


def system_audio_status() -> dict:
    """Read CoreAudio output state; never change global volume or permissions."""
    if sys.platform != "darwin":
        return {}
    import ctypes as c

    class Address(c.Structure):
        _fields_ = [("selector", c.c_uint32), ("scope", c.c_uint32), ("element", c.c_uint32)]

    try:
        api = c.CDLL("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")
        get = api.AudioObjectGetPropertyData
        get.argtypes = [c.c_uint32, c.POINTER(Address), c.c_uint32, c.c_void_p, c.POINTER(c.c_uint32), c.c_void_p]
        get.restype = c.c_int32

        def read(device, selector, scope="glob", element=0, kind=c.c_uint32):
            address = Address(int.from_bytes(selector.encode("ascii"), "big"),
                              int.from_bytes(scope.encode("ascii"), "big"), element)
            value, size = kind(), c.c_uint32(c.sizeof(kind))
            result = get(device, c.byref(address), 0, None, c.byref(size), c.byref(value))
            return value.value if result == 0 else None

        output = read(1, "dOut")
        if output is None:
            return {}
        muted = read(output, "mute", "outp")
        volume = read(output, "volm", "outp", kind=c.c_float)
        return {"output_muted": bool(muted) if muted is not None else None, "output_volume": volume}
    except (OSError, AttributeError):
        return {}


class Microphone:
    """Bounded PCM queue; model decoding runs off the WebSocket event loop."""

    def __init__(self, model_path: Path, device: int | str | None = None, diagnostics=False):
        self.model_path = model_path
        self.device = device
        self.model = None
        self.stream = None
        self.recognizer = None
        self.decoder = None
        self.events = deque()
        self.chunks: queue.Queue[bytes] = queue.Queue(maxsize=32)
        self.lock = threading.Lock()
        self.reset_requested = threading.Event()
        self.muted = threading.Event()
        self.diagnostics = diagnostics
        self.sample_rate = 0
        self.samples_seen = 0
        self.peak = 0

    def load(self):
        from vosk import Model, SetLogLevel
        if not (self.model_path / "am/final.mdl").is_file():
            raise RuntimeError(f"缺少中文模型：{self.model_path}；先运行 --download-model。")
        SetLogLevel(-1)
        self.model = Model(str(self.model_path))

    def start(self):
        import sounddevice as sd
        from vosk import KaldiRecognizer
        import webrtcvad
        with self.lock:
            if self.stream is not None:
                return

            def callback(data, frames, timing, status):
                if self.muted.is_set():
                    return
                try:
                    self.chunks.put_nowait(bytes(data))
                except queue.Full:
                    self.reset_requested.set()  # Drop old speech rather than replay a late command.

            for attempt in range(3):
                stream = None
                try:
                    info = sd.query_devices(self.device, "input")
                    rate = int(info["default_samplerate"])
                    if rate not in (8000, 16000, 32000, 48000):
                        rate = 16000
                        sd.check_input_settings(device=self.device, channels=1, dtype='int16', samplerate=rate)
                    self.sample_rate = rate
                    self.samples_seen = self.peak = 0
                    self.recognizer = KaldiRecognizer(self.model, rate)
                    self.decoder = UtteranceDecoder(self.recognizer, webrtcvad.Vad(2), rate)
                    self.events.clear()
                    self.reset_requested.clear()
                    self._drain()

                    stream = sd.RawInputStream(samplerate=rate, blocksize=max(800, rate // 10),
                                               device=self.device, channels=1, dtype="int16", callback=callback)
                    stream.start()
                    self.stream = stream
                    return
                except Exception as error:
                    if stream is not None:
                        try:
                            stream.close()
                        except Exception:
                            pass  # Preserve the original startup error.
                    internal = isinstance(error, sd.PortAudioError) and len(error.args) > 1 and error.args[1] == -9986
                    if not internal:
                        raise
                    if attempt == 2:
                        raise RuntimeError("无法打开麦克风（PortAudio -9986，已重试）。请确认麦克风授权与系统输入设备，关闭占用设备的程序后重新按 QQ；仍失败时重启游戏。") from error
                    # Device routes may be settling after a previous stream closes. Re-query only
                    # the requested/default device; never switch to another microphone silently.
                    time.sleep((0.15, 0.35)[attempt])

    def _drain(self):
        while True:
            try:
                self.chunks.get_nowait()
            except queue.Empty:
                return

    def reset(self):
        self._drain()
        self.reset_requested.set()

    def mute(self, value: bool):
        if value:
            self.muted.set()
        else:
            self.muted.clear()
        self.reset()

    def next_event(self) -> AudioEvent | None:
        with self.lock:
            if self.stream is None:
                return None
            if self.reset_requested.is_set():
                self.decoder.reset()
                self.events.clear()
                self.reset_requested.clear()
                self._drain()
                return AudioEvent('cancelled', reason='audio_reset')
            if self.events:
                return self.events.popleft()
        try:
            chunk = self.chunks.get(timeout=0.2)
        except queue.Empty:
            stream = self.stream
            if stream is not None and not stream.active:
                raise RuntimeError("麦克风已断开，请检查设备并重新开启语音。")
            return None
        if self.diagnostics and chunk:
            self.samples_seen += len(chunk) // 2
            self.peak = max(self.peak, max(map(abs, array("h", chunk))))
        with self.lock:
            if self.stream is None:
                return None
            if self.reset_requested.is_set():
                self.decoder.reset()
                self.events.clear()
                self.reset_requested.clear()
                self._drain()
                return AudioEvent('cancelled', reason='audio_reset')
            self.events.extend(self.decoder.feed(chunk))
            if self.events:
                return self.events.popleft()
        return None

    def next_result(self) -> str | None:
        event = self.next_event()
        return event.text if event and event.kind == 'final' else None

    def stop(self):
        with self.lock:
            stream, self.stream = self.stream, None
            if stream is not None:
                try:
                    stream.stop()
                finally:
                    stream.close()
            self.reset()


class SystemSpeech:
    """One cancellable OS process per utterance. Text goes through stdin, never a shell."""

    async def speak(self, text: str):
        status = await asyncio.to_thread(system_audio_status)
        if status.get("output_muted"):
            raise RuntimeError("系统扬声器已静音，请解除系统静音后重试；语音服务不会修改系统音量。")
        if status.get("output_volume") == 0:
            raise RuntimeError("系统输出音量为 0，请调高音量后重试。")
        if sys.platform == "darwin":
            command = ["/usr/bin/say", "-v", "Tingting", "-r", "190"]
            payload = text.encode("utf-8")
        elif sys.platform == "win32":
            script = (
                "[Console]::InputEncoding = [Text.Encoding]::UTF8; "
                "Add-Type -AssemblyName System.Speech; "
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
                "try { $s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, "
                "[System.Speech.Synthesis.VoiceAge]::NotSet, 0, "
                "[Globalization.CultureInfo]::GetCultureInfo('zh-CN')); "
                "$s.Speak([Console]::In.ReadToEnd()) } finally { $s.Dispose() }"
            )
            command = ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script]
            payload = text.encode("utf-8")
        else:
            raise RuntimeError("当前系统 TTS 仅支持 macOS 和 Windows。")
        process = await asyncio.create_subprocess_exec(*command, stdin=asyncio.subprocess.PIPE,
                                                       stdout=asyncio.subprocess.DEVNULL,
                                                       stderr=asyncio.subprocess.PIPE)
        try:
            _, error = await process.communicate(payload)
            if process.returncode:
                raise RuntimeError("系统语音合成失败，请检查已安装的中文音色。" + error.decode(errors="replace")[:160])
        finally:
            if process.returncode is None:
                try:
                    process.terminate()
                except ProcessLookupError:
                    pass
                await process.wait()

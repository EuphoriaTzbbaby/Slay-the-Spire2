from __future__ import annotations

import argparse
import asyncio
import sys
import urllib.request
import zipfile
from pathlib import Path

from .audio import Microphone, SystemSpeech, system_audio_status
from .common import default_token_file

ROOT = Path(__file__).resolve().parents[2]
MODEL_NAME = "vosk-model-small-cn-0.22"
DEFAULT_MODEL = ROOT / "models" / MODEL_NAME


def download_model():
    destination = ROOT / "models"
    destination.mkdir(parents=True, exist_ok=True)
    archive = destination / (MODEL_NAME + ".zip")
    if archive.exists() and not zipfile.is_zipfile(archive):
        archive.unlink()
    if not archive.exists():
        print("下载官方中文模型（约 42 MB）…", flush=True)
        temporary = archive.with_suffix(".download")
        try:
            with urllib.request.urlopen(f"https://alphacephei.com/vosk/models/{MODEL_NAME}.zip", timeout=60) as source:
                with temporary.open("wb") as target:
                    while chunk := source.read(1024 * 1024):
                        target.write(chunk)
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
    with zipfile.ZipFile(archive) as bundle:
        for member in bundle.infolist():
            path = (destination / member.filename).resolve()
            if not path.is_relative_to(destination.resolve()) or not member.filename.startswith(MODEL_NAME + "/"):
                raise RuntimeError("模型压缩包路径不符合预期。")
        bundle.extractall(destination)
    print(f"模型已准备：{DEFAULT_MODEL}")


async def run(args):
    if args.test_tts:
        await SystemSpeech().speak("你好，我是你的尖塔搭子。语音播报测试成功。")
        print("系统播报进程完成；请确认实际听到声音。")
        return
    if args.list_devices:
        import sounddevice as sd
        print(sd.query_devices())
        status = system_audio_status()
        if status:
            print("系统输出状态：" + str(status))
        return
    from .lifecycle import watch_parent_pipe
    parent_closed = watch_parent_pipe(sys.stdin.buffer) if args.managed else asyncio.Event()
    device = args.input_device
    if device is not None and device.isdecimal():
        device = int(device)
    microphone = Microphone(args.model, device, diagnostics=args.test_mic)
    print(f"[voice] 加载本地中文模型：{args.model}", flush=True)
    await asyncio.to_thread(microphone.load)
    if parent_closed.is_set():
        return
    if args.test_mic:
        await asyncio.to_thread(microphone.start)
        print("麦克风已打开；请说一句中文，静默结束。最多等待 15 秒，不保存录音。", flush=True)
        try:
            async with asyncio.timeout(15):
                while True:
                    text = await asyncio.to_thread(microphone.next_result)
                    if text:
                        print("识别结果：" + text)
                        return
        except TimeoutError:
            print(f"输入诊断：{microphone.samples_seen} 个采样，峰值 {microphone.peak}/32768；没有得到有效最终文本。", flush=True)
            raise
        finally:
            await asyncio.to_thread(microphone.stop)
    from websockets.asyncio.server import serve
    from .service import VoiceServer
    server = VoiceServer(args.token_file, microphone, SystemSpeech())
    print(f"[voice] 会话文件：{args.token_file}（令牌不打印）", flush=True)
    async with serve(server.handler, "127.0.0.1", args.port, max_size=65536):
        print(f"[voice] 已就绪 ws://127.0.0.1:{args.port}；游戏开启语音后才采集。", flush=True)
        try:
            await parent_closed.wait()
        finally:
            if server.owner:
                await server.owner.close()


def main():
    parser = argparse.ArgumentParser(description="尖塔本地语音基本调试：Vosk 中文识别 + 系统 TTS")
    parser.add_argument("--port", type=int, default=17860)
    parser.add_argument("--token-file", type=Path, default=default_token_file())
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--input-device", help="设备编号或名称；默认使用系统输入")
    parser.add_argument("--managed", action="store_true", help="由游戏管理：父进程标准输入关闭后退出")
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument("--download-model", action="store_true")
    actions.add_argument("--list-devices", action="store_true")
    actions.add_argument("--test-tts", action="store_true")
    actions.add_argument("--test-mic", action="store_true")
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("端口必须在 1–65535。")
    try:
        if args.download_model:
            download_model()
        else:
            asyncio.run(run(args))
    except KeyboardInterrupt:
        pass
    except TimeoutError:
        sys.exit("语音测试超时；请检查麦克风权限、设备和输入音量。")
    except Exception as error:
        sys.exit(f"语音服务未就绪：{error}")


if __name__ == "__main__":
    main()

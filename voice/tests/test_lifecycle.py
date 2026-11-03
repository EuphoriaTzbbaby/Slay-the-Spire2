import asyncio
from contextlib import asynccontextmanager
import os
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from spire_voice.__main__ import run
from spire_voice.lifecycle import watch_parent_pipe


class LifecycleTests(unittest.IsolatedAsyncioTestCase):
    async def test_private_pipe_eof_signals_parent_exit(self):
        read, write = os.pipe()
        with os.fdopen(read, 'rb', buffering=0) as reader, os.fdopen(write, 'wb', buffering=0) as writer:
            closed = watch_parent_pipe(reader)
            writer.write(b'x')
            await asyncio.sleep(0.03)
            self.assertFalse(closed.is_set())
            writer.close()
            await asyncio.wait_for(closed.wait(), 1)

    async def test_managed_service_exits_and_cleans_audio_on_parent_eof(self):
        await self.service_lifetime(managed=True)

    async def test_manual_service_ignores_stdin_eof(self):
        await self.service_lifetime(managed=False)

    async def service_lifetime(self, managed):
        read, write = os.pipe()
        with os.fdopen(read, 'rb', buffering=0) as reader, os.fdopen(write, 'wb', buffering=0) as writer:
            entered = asyncio.Event()

            @asynccontextmanager
            async def fake_serve(*args, **kwargs):
                entered.set()
                yield

            owner = SimpleNamespace(close=AsyncMock())
            server = SimpleNamespace(owner=owner, handler=None)
            args = SimpleNamespace(test_tts=False, list_devices=False, test_mic=False, managed=managed,
                                   model=Path('unused'), input_device=None, port=17860, token_file=Path('unused'))
            with patch('spire_voice.__main__.Microphone', return_value=MagicMock()) as microphone, \
                    patch('spire_voice.__main__.sys.stdin', SimpleNamespace(buffer=reader)), \
                    patch('spire_voice.service.VoiceServer', return_value=server), \
                    patch('websockets.asyncio.server.serve', fake_serve):
                task = asyncio.create_task(run(args))
                try:
                    await asyncio.wait_for(entered.wait(), 1)
                    writer.close()
                    if managed:
                        await asyncio.wait_for(task, 1)
                    else:
                        await asyncio.sleep(0.03)
                        self.assertFalse(task.done())
                    microphone.return_value.start.assert_not_called()
                finally:
                    if not task.done():
                        task.cancel()
                    await asyncio.gather(task, return_exceptions=True)
                owner.close.assert_awaited_once()


if __name__ == '__main__':
    unittest.main()

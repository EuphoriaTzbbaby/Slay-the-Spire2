"""A private stdin pipe ties a managed service to the game that launched it."""
import asyncio
import threading


def watch_parent_pipe(stream) -> asyncio.Event:
    closed = asyncio.Event()
    loop = asyncio.get_running_loop()

    def watch():
        try:
            while stream.read(1):
                pass
        finally:
            try:
                loop.call_soon_threadsafe(closed.set)
            except RuntimeError:
                pass  # The service has already exited through another path.

    threading.Thread(target=watch, name="voice-parent-pipe", daemon=True).start()
    return closed

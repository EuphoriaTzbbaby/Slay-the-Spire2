"""Bridge the game's stdin lifetime to a LaunchServices app using a private FIFO.

macOS cannot pass an anonymous pipe via `open --stdin /dev/stdin`. This process
never accesses audio; microphone consent belongs to the separately launched app.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time

ERRORS = {"SPIRE_VOICE_ERROR:microphone_permission_denied", "SPIRE_VOICE_ERROR:invalid_installation"}


def launch(app, arguments, parent_input=None):
    parent_input = parent_input or sys.stdin.buffer
    with tempfile.TemporaryDirectory(prefix='spire-voice-') as temporary:
        directory = Path(temporary)
        fifo = directory / 'parent.pipe'
        os.mkfifo(fifo, 0o600)
        # Keep the write end open until the game exits, including while permission is pending.
        anchor = os.open(fifo, os.O_RDWR | os.O_NONBLOCK)
        closed = threading.Event()

        def watch():
            try:
                while parent_input.read(1):
                    pass
            finally:
                closed.set()

        threading.Thread(target=watch, name='voice-launch-parent', daemon=True).start()
        errors = directory / 'startup.log'
        process = None
        reported = set()
        try:
            if closed.is_set():
                return 0
            process = subprocess.Popen(['/usr/bin/open', '-n', '-g', '-W', '--stdin', str(fifo),
                                        '--stdout', '/dev/null', '--stderr', str(errors), str(app), '--args', *arguments],
                                       stdin=subprocess.DEVNULL)
            deadline = None
            while True:
                result = process.poll()
                if errors.exists():
                    for marker in errors.read_text(errors='replace')[-4096:].splitlines():
                        if marker in ERRORS and marker not in reported:
                            print(marker, file=sys.stderr, flush=True)
                            reported.add(marker)
                if result is not None:
                    return result
                if closed.is_set() and anchor is not None:
                    os.close(anchor)
                    anchor = None
                    deadline = time.monotonic() + 8
                if deadline is not None and time.monotonic() > deadline:
                    process.terminate()
                    return process.wait(timeout=3)
                time.sleep(.05)
        finally:
            if anchor is not None:
                os.close(anchor)
            if process is not None and process.poll() is None:
                # The app's native EOF watcher owns and reaps its Python child.
                try:
                    process.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    process.terminate()
                    process.wait(timeout=3)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', required=True, type=Path)
    args, remaining = parser.parse_known_args()
    raise SystemExit(launch(args.app, remaining))

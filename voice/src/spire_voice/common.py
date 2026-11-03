from __future__ import annotations

import os
import re
import sys
from pathlib import Path

INTENTS = {
    "暂停": "pause", "等一下": "pause", "停": "pause",
    "恢复": "resume", "继续": "resume", "继续操作": "resume",
    "到你了": "handoff", "你来打": "handoff", "你来": "handoff",
}


def compact(text: str) -> str:
    return re.sub(r"[\s，。！？、,.!?：:；;]+", "", text)


def intent_for(text: str) -> str | None:
    # Chinese ASR inserts spaces between words. Match the whole normalized phrase.
    return INTENTS.get(compact(text))


def speech_text(text: str) -> str:
    text = re.sub(r"```[\s\S]*?```", "", text)
    text = re.sub(r"!\[[^\]]*\]\([^)]*\)", "", text)
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"https?://\S+", "", text)
    text = re.sub(r"\[/?(?:b|i|u|s|color|url|font_size)(?:=[^\]]*)?\]", "", text)
    text = re.sub(r"\b(?:c\d+@\w+|(?:card|option|enemy|select|reward):[\w:-]+)\b", "", text)
    text = re.sub(r"(?m)^\s*(?:#{1,6}\s*|[-*>]\s+|\d+[.)]\s+)", "", text)
    text = re.sub(r"[*_`|]", "", text)
    return re.sub(r"\s+", " ", text).strip()[:2000]


def default_token_file() -> Path:
    if sys.platform == "darwin":
        root = Path.home() / "Library/Application Support/SlayTheSpire2"
    elif sys.platform == "win32":
        root = Path(os.environ.get("APPDATA", Path.home() / "AppData/Roaming")) / "SlayTheSpire2"
    else:
        root = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share")) / "SlayTheSpire2"
    return root / "spire-companion/voice-session.json"

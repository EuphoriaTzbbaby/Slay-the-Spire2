"""Speech turns: tolerate short pauses, preview text, and finish bounded utterances."""
from collections import deque
from dataclasses import dataclass
import json


@dataclass(frozen=True)
class AudioEvent:
    kind: str
    text: str = ""
    reason: str = ""


class UtteranceDecoder:
    FRAME_SECONDS = .02

    def __init__(self, recognizer, vad, sample_rate, silence_seconds=.9, max_seconds=20):
        self.recognizer, self.vad, self.sample_rate = recognizer, vad, sample_rate
        self.frame_bytes = sample_rate // 50 * 2
        self.silence_seconds, self.max_seconds = silence_seconds, max_seconds
        self.pending = bytearray()
        self._clear_turn()

    def _clear_turn(self):
        self.active = False
        self.preroll = deque(maxlen=15)  # Keep 300 ms so speech detection does not clip the first syllable.
        self.votes = deque(maxlen=8)
        self.parts = []
        self.quiet = self.duration = self.last_preview_at = 0.0
        self.last_preview = ""

    def reset(self):
        self.pending.clear()
        self.recognizer.Reset()
        self._clear_turn()

    def _decode(self, frame):
        if self.recognizer.AcceptWaveform(frame):
            text = json.loads(self.recognizer.Result()).get('text', '').strip()
            if text:
                self.parts.append(text)

    def feed(self, chunk):
        self.pending.extend(chunk)
        events = []
        while len(self.pending) >= self.frame_bytes:
            frame = bytes(self.pending[:self.frame_bytes])
            del self.pending[:self.frame_bytes]
            voiced = self.vad.is_speech(frame, self.sample_rate)
            if not self.active:
                self.preroll.append(frame)
                self.votes.append(voiced)
                if not voiced or sum(self.votes) < 6:
                    continue
                self.active = True
                events.append(AudioEvent('started'))
                for buffered in self.preroll:
                    self._decode(buffered)
                self.duration = len(self.preroll) * self.FRAME_SECONDS
                self.preroll.clear()
            else:
                self._decode(frame)
                self.duration += self.FRAME_SECONDS
                self.quiet = 0.0 if voiced else self.quiet + self.FRAME_SECONDS
            if self.duration - self.last_preview_at >= .2:
                self.last_preview_at = self.duration
                partial = json.loads(self.recognizer.PartialResult()).get('partial', '').strip()
                preview = ' '.join([*self.parts, partial]).strip()
                if preview and preview != self.last_preview:
                    self.last_preview = preview
                    events.append(AudioEvent('partial', preview))
            if self.quiet + 1e-6 >= self.silence_seconds or self.duration + 1e-6 >= self.max_seconds:
                final = json.loads(self.recognizer.FinalResult()).get('text', '').strip()
                text = ' '.join([*self.parts, final]).strip()
                reason = 'max_duration' if self.duration + 1e-6 >= self.max_seconds else 'silence'
                events.append(AudioEvent('final', text, reason) if text else AudioEvent('cancelled', reason='no_text'))
                self.recognizer.Reset()
                self._clear_turn()
        return events

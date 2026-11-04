import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from spire_voice.utterance import UtteranceDecoder


class FakeVad:
    def is_speech(self, frame, rate): return frame[:2] != b'\0\0'


class FakeRecognizer:
    def __init__(self, text='你好', endpoint_at=None):
        self.text, self.endpoint_at, self.frames = text, endpoint_at, 0
    def AcceptWaveform(self, frame):
        self.frames += 1
        return self.frames == self.endpoint_at
    def Result(self): return json.dumps({'text': '前半句'})
    def PartialResult(self): return json.dumps({'partial': self.text})
    def FinalResult(self): return json.dumps({'text': self.text})
    def Reset(self): self.frames = 0


class UtteranceTests(unittest.TestCase):
    def decoder(self, **options): return UtteranceDecoder(FakeRecognizer(), FakeVad(), 16000, **options)

    def audio(self, decoder, voiced, frames):
        # Deterministic frames exercise turn boundaries, not real-world VAD accuracy.
        return decoder.feed((b'\1\0' if voiced else b'\0\0') * 320 * frames)

    def test_silence_and_single_click_do_not_start_a_turn(self):
        decoder = self.decoder()
        self.assertEqual(self.audio(decoder, False, 500), [])
        self.assertEqual(self.audio(decoder, True, 1), [])
        self.assertEqual(self.audio(decoder, False, 100), [])

    def test_short_pause_is_merged_and_final_waits_for_end_of_speech(self):
        decoder = self.decoder()
        events = self.audio(decoder, True, 30) + self.audio(decoder, False, 25) + self.audio(decoder, True, 30)
        self.assertEqual(sum(e.kind == 'started' for e in events), 1)
        self.assertTrue(any(e.kind == 'partial' for e in events))
        self.assertFalse(any(e.kind == 'final' for e in events))
        self.assertFalse(any(e.kind == 'final' for e in self.audio(decoder, False, 44)))
        final = self.audio(decoder, False, 1)
        self.assertEqual([(e.kind, e.text) for e in final], [('final', '你好')])

    def test_asr_endpoint_does_not_split_a_short_conversational_pause(self):
        decoder = UtteranceDecoder(FakeRecognizer('后半句', endpoint_at=30), FakeVad(), 16000)
        events = self.audio(decoder, True, 20) + self.audio(decoder, False, 20)
        self.assertFalse(any(e.kind == 'final' for e in events))
        events += self.audio(decoder, True, 20) + self.audio(decoder, False, 45)
        self.assertEqual([e.text for e in events if e.kind == 'final'], ['前半句 后半句'])

    def test_long_speech_is_bounded_without_silence(self):
        finals = [e for e in self.audio(self.decoder(max_seconds=1), True, 50) if e.kind == 'final']
        self.assertEqual(len(finals), 1)
        self.assertEqual(finals[0].reason, 'max_duration')

    def test_reset_discards_unfinished_speech_and_next_turn_is_fresh(self):
        decoder = self.decoder()
        self.audio(decoder, True, 30)
        decoder.reset()
        self.assertEqual(self.audio(decoder, False, 60), [])
        events = self.audio(decoder, True, 30) + self.audio(decoder, False, 45)
        self.assertEqual(sum(e.kind == 'started' for e in events), 1)
        self.assertEqual(sum(e.kind == 'final' for e in events), 1)

    def test_empty_asr_closes_activity_without_a_chat_input(self):
        decoder = UtteranceDecoder(FakeRecognizer(''), FakeVad(), 16000)
        events = self.audio(decoder, True, 30) + self.audio(decoder, False, 45)
        self.assertEqual([e.kind for e in events], ['started', 'cancelled'])

    def test_chunk_boundaries_do_not_change_turns(self):
        audio = b'\1\0' * 320 * 30 + b'\0\0' * 320 * 45
        expected = self.decoder().feed(audio)
        decoder, actual = self.decoder(), []
        for start in range(0, len(audio), 713): actual += decoder.feed(audio[start:start + 713])
        self.assertEqual(actual, expected)

    def test_real_vad_accepts_silence_at_supported_rates(self):
        import webrtcvad
        for rate in (8000, 16000, 32000, 48000):
            self.assertFalse(webrtcvad.Vad(2).is_speech(b'\0\0' * (rate // 50), rate))


if __name__ == '__main__': unittest.main()

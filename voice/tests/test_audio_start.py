"""Startup recovery without real audio hardware or an ASR model."""
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from spire_voice.audio import Microphone


class AudioError(Exception):
    pass


class MicrophoneStartTests(unittest.TestCase):
    def backend(self, outcomes):
        return SimpleNamespace(PortAudioError=AudioError,
            query_devices=Mock(return_value={'default_samplerate': 48000}),
            RawInputStream=Mock(side_effect=outcomes))

    def start(self, backend):
        mic = Microphone(Path('.'))
        modules = {'sounddevice': backend, 'vosk': SimpleNamespace(KaldiRecognizer=Mock()),
                   'webrtcvad': SimpleNamespace(Vad=Mock())}
        with patch.dict(sys.modules, modules), patch('spire_voice.audio.time.sleep'):
            mic.start()
        return mic

    def test_internal_open_error_requeries_device_and_recovers(self):
        stream = Mock()
        backend = self.backend([AudioError('internal', -9986), stream])
        mic = self.start(backend)
        self.assertIs(mic.stream, stream)
        self.assertEqual(backend.query_devices.call_count, 2)
        mic.stop()
        stream.close.assert_called_once()

    def test_failed_start_closes_partial_stream_before_retry(self):
        failed, healthy = Mock(), Mock()
        failed.start.side_effect = AudioError('internal', -9986)
        mic = self.start(self.backend([failed, healthy]))
        failed.close.assert_called_once()
        self.assertIs(mic.stream, healthy)
        mic.stop()

    def test_persistent_internal_failure_is_bounded_and_explained(self):
        backend = self.backend([AudioError('internal', -9986)] * 3)
        with self.assertRaisesRegex(RuntimeError, 'PortAudio -9986.*已重试'):
            self.start(backend)
        self.assertEqual(backend.RawInputStream.call_count, 3)

    def test_other_errors_do_not_retry_or_select_another_microphone(self):
        backend = self.backend([AudioError('unavailable', -9985)])
        with self.assertRaises(AudioError):
            self.start(backend)
        backend.RawInputStream.assert_called_once()
        backend.query_devices.assert_called_once_with(None, 'input')

import json
import os
import sys
import tempfile
import unittest
import wave
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'engine'))
import windows_speech
import vidistudio_engine as engine


class WindowsSpeechRouting(unittest.TestCase):
    def test_sapi_host_override_and_unicode_text_use_stdin(self):
        with tempfile.TemporaryDirectory() as folder:
            host = Path(folder) / 'speech.exe'
            host.touch()
            with patch.object(windows_speech.subprocess, 'run', return_value=SimpleNamespace(returncode=0, stdout='{}')) as run:
                windows_speech.request({'ttsMode': 'sapi5', 'sapiHostPath': str(host), 'systemVoice': 'token'},
                                       'synthesize', text='नमस्ते <hello>', output='out.wav', rate=1)
            self.assertEqual(run.call_args.args[0], [str(host)])
            payload = json.loads(run.call_args.kwargs['input'])
            self.assertEqual(payload['text'], 'नमस्ते <hello>')
            self.assertEqual(payload['engine'], 'sapi5')
            self.assertEqual(payload['voice'], 'token')

    def test_natural_voices_use_the_sapi_host_override(self):
        with tempfile.TemporaryDirectory() as folder:
            host = Path(folder) / 'speech-x64.exe'
            host.touch()
            with patch.object(windows_speech.subprocess, 'run', return_value=SimpleNamespace(returncode=0, stdout='[]')) as run:
                windows_speech.request({'ttsMode': 'windows-natural', 'sapiHostPath': str(host),
                                        'systemVoice': r'HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator\MicrosoftWindows.Voice.en-US.Ava.1'},
                                       'voices')
            self.assertEqual(run.call_args.args[0], [str(host)])
            payload = json.loads(run.call_args.kwargs['input'])
            self.assertEqual(payload['engine'], 'windows-natural')
            self.assertTrue(payload['voice'].endswith('MicrosoftWindows.Voice.en-US.Ava.1'))

    def test_unknown_engine_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Choose Windows OneCore'):
            windows_speech.request({'ttsMode': 'azure'}, 'voices')

    def test_natural_and_online_voices_have_their_own_narration_signature(self):
        signatures = {engine.narration_signature({'ttsMode': mode, 'systemVoice': 'token'}) for mode in ('windows-natural', 'sapi5', 'azure')}
        self.assertEqual(len(signatures), 3)
        self.assertEqual(engine.narration_engine({'ttsMode': 'windows-natural'}), 'windows-natural')

    def test_missing_host_reports_setup_error(self):
        with patch.dict(os.environ, {'VIDISTUDIO_SPEECH_HOST': ''}):
            with self.assertRaisesRegex(ValueError, 'host is missing'):
                windows_speech.request({'ttsMode': 'windows-core'}, 'voices')

    def test_different_providers_invalidate_narration_signature(self):
        self.assertNotEqual(engine.narration_signature({'ttsMode': 'windows-core'}),
                            engine.narration_signature({'ttsMode': 'sapi5'}))

    def test_provider_failure_is_not_treated_as_audio(self):
        with tempfile.TemporaryDirectory() as folder:
            host = Path(folder) / 'speech.exe'; host.touch()
            with patch.dict(os.environ, {'VIDISTUDIO_SPEECH_HOST': str(host)}), \
                 patch.object(windows_speech.subprocess, 'run', return_value=SimpleNamespace(returncode=1, stderr='Unavailable')):
                with self.assertRaisesRegex(ValueError, 'Windows speech failed'):
                    windows_speech.request({'ttsMode': 'windows-core'}, 'voices')


@unittest.skipUnless(os.name == 'nt' and os.environ.get('VIDISTUDIO_SPEECH_HOST'), 'Requires built Windows speech host')
class WindowsSpeechIntegration(unittest.TestCase):
    def test_installed_core_and_sapi_voices_produce_wave_audio(self):
        for mode in ('windows-core', 'sapi5'):
            with self.subTest(provider=mode), tempfile.TemporaryDirectory() as folder:
                voices = windows_speech.request({'ttsMode': mode}, 'voices')
                self.assertTrue(voices, f'Install a {mode} voice to validate synthesis')
                output = Path(folder) / 'voice.wav'
                windows_speech.request({'ttsMode': mode, 'systemVoice': voices[0]['Id']},
                                       'synthesize', text='A person walks into the room.', output=str(output), rate=1)
                with wave.open(str(output), 'rb') as audio:
                    self.assertGreater(audio.getnframes(), 100)
                    self.assertGreater(audio.getframerate(), 8000)

import io
import json
import tempfile
import unittest
import wave
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch, Mock
from test_engine import engine

class VoicePreview(unittest.TestCase):
    def test_shared_models_ignore_legacy_youtube_overrides(self):
        stages = ('summary', 'audio_descriptions', 'screen_text', 'text_rewrite', 'tts_audio')
        models = {stage: 'shared-' + stage for stage in stages}
        models.update({'youtube_' + stage: 'legacy-' + stage for stage in stages})
        models.update(transcript='gemini-local-transcribe', youtube_transcript='gemini-flash')
        with patch.object(engine, 'config', return_value={'models': models}), patch.object(engine, 'gemini_client', return_value=Mock()):
            for stage in stages:
                for source in ('local', 'youtube'):
                    with self.subTest(stage=stage, source=source):
                        self.assertEqual(engine.client_and_model(stage, {'sourceType': source})[1], models[stage])
                self.assertEqual(engine.client_and_model('youtube_' + stage)[1], models[stage])
            self.assertEqual(engine.client_and_model('transcribe', {'sourceType': 'local'})[1], 'gemini-local-transcribe')
            self.assertEqual(engine.client_and_model('transcribe', {'sourceType': 'youtube'})[1], 'gemini-flash')
            self.assertEqual(engine.client_and_model('youtube_transcript')[1], 'gemini-flash')
            self.assertEqual(engine.client_and_model('tts', {'sourceType': 'youtube'})[1], models['tts_audio'])
        settings = {'ttsMode': 'gemini', 'geminiVoice': 'Kore', 'models': models}
        self.assertEqual(engine.narration_signature(settings, {'sourceType': 'local'}),
                         engine.narration_signature(settings, {'sourceType': 'youtube'}))
        previous = engine.narration_signature(settings)
        models['tts_audio'] = 'new-shared-tts'
        self.assertNotEqual(previous, engine.narration_signature(settings, {'sourceType': 'youtube'}))

    def test_youtube_transcription_rejects_speech_only_models(self):
        for model in ('gemini-3.5-transcribe', 'gemini-flash-tts'):
            with patch.object(engine, 'config', return_value={'models': {'youtube_transcript': model}}), patch.object(engine, 'gemini_client') as client:
                with self.assertRaisesRegex(ValueError, 'video model for YouTube'):
                    engine.client_and_model('youtube_transcript')
                client.assert_not_called()

    def test_gemini_voice_catalog_emits_human_names_and_ids(self):
        client = Mock()
        client.voices.list.return_value = SimpleNamespace(voices=[SimpleNamespace(id='Kore', display_name='Kore', language_code='en-US', gender='female')])
        output = io.StringIO()
        with patch.object(engine, 'gemini_client', return_value=client), redirect_stdout(output):
            engine.list_voices()
        self.assertEqual(json.loads(output.getvalue())['voices'][0]['id'], 'Kore')
        client.voices.list.assert_called_once_with(page_size=1000)
        client.close.assert_called_once()

    def test_model_catalog_comes_from_available_generate_content_models(self):
        client = Mock()
        client.models.list.return_value = [
            SimpleNamespace(name='models/gemini-3.8-flash', display_name='Gemini 3.8 Flash', supported_actions=['generateContent', 'countTokens']),
            SimpleNamespace(name='models/gemini-3.8-flash-lite-tts', display_name='Gemini 3.8 Flash-Lite TTS', supported_actions=['generateContent']),
            SimpleNamespace(name='models/gemini-2.5-flash-preview-tts', display_name='Gemini 2.5 Flash TTS', supported_actions=['generateContent']),
            SimpleNamespace(name='models/gemini-embedding-001', display_name='Gemini Embedding', supported_actions=['embedContent']),
            SimpleNamespace(name='models/imagen-4.0-generate-001', display_name='Imagen 4', supported_actions=['predict']),
        ]
        output = io.StringIO()
        with patch.object(engine, 'gemini_client', return_value=client), redirect_stdout(output):
            engine.list_models()
        event = json.loads(output.getvalue())
        self.assertEqual([model['name'] for model in event['models']], [
            'gemini-2.5-flash-preview-tts',
            'gemini-3.8-flash',
            'gemini-3.8-flash-lite-tts',
        ])
        client.close.assert_called_once()

    def test_preview_uses_tts_model_and_voice_without_project(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'sample.wav'
            client = Mock()
            client.models.generate_content.return_value = SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(parts=[SimpleNamespace(inline_data=SimpleNamespace(data=b'\0\0' * 2400))]))])
            cfg = {'ttsMode':'gemini', 'geminiVoice':'Kore', 'prompts':{'tts_audio':{'user_prompt':'Speak: {text}'}}}
            with patch.object(engine, 'config', return_value=cfg), patch.object(engine, 'client_and_model', return_value=(client, 'configured-tts-model')):
                engine.preview_tts(output, 'Sample narration')
            call = client.models.generate_content.call_args.kwargs
            self.assertEqual(call['model'], 'configured-tts-model')
            self.assertEqual(call['contents'], 'Speak: Sample narration')
            self.assertEqual(call['config'].speech_config.voice_config.prebuilt_voice_config.voice_name, 'Kore')
            client.close.assert_called_once()
            with wave.open(str(output)) as audio:
                self.assertEqual(audio.getframerate(), 24000)
                self.assertEqual(audio.getnframes(), 2400)
            self.assertFalse((Path(directory)/'project.json').exists())

    def test_gemini_38_reads_verbatim_text_and_preserves_complete_wav(self):
        source = io.BytesIO()
        with wave.open(source, 'wb') as audio:
            audio.setnchannels(1)
            audio.setsampwidth(2)
            audio.setframerate(24000)
            audio.writeframes(b'\0\0' * 2400)
        cfg = {'geminiVoice':'Kore', 'prompts':{'tts_audio':{'user_prompt':'Read clearly and naturally: {text}'}}}
        for model in ('gemini-3.8-flash-tts', 'gemini-3.8-flash-lite-tts'):
            with self.subTest(model=model), tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / 'sample.wav'
                client = Mock()
                client.models.generate_content.return_value = SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(parts=[SimpleNamespace(inline_data=SimpleNamespace(data=source.getvalue(), mime_type='audio/wav'))]))])
                with patch.object(engine, 'config', return_value=cfg), patch.object(engine, 'client_and_model', return_value=(client, model)):
                    engine.gemini_speech(output, 'A woman types.', {})
                self.assertEqual(client.models.generate_content.call_args.kwargs['contents'], 'A woman types.')
                self.assertEqual(output.read_bytes(), source.getvalue())
                with wave.open(str(output)) as audio:
                    self.assertEqual(audio.getnframes(), 2400)

    def test_malformed_wav_is_not_wrapped_as_raw_audio(self):
        client = Mock()
        client.models.generate_content.return_value = SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(parts=[SimpleNamespace(inline_data=SimpleNamespace(data=b'RIFFbad', mime_type='audio/wav'))]))])
        cfg = {'prompts':{'tts_audio':{'user_prompt':'{text}'}}}
        with tempfile.TemporaryDirectory() as directory, patch.object(engine, 'config', return_value=cfg), patch.object(engine, 'client_and_model', return_value=(client, 'gemini-3.8-flash-tts')):
            output = Path(directory) / 'sample.wav'
            with self.assertRaisesRegex(ValueError, 'invalid WAV'):
                engine.gemini_speech(output, 'Test', {})
            self.assertFalse(output.exists())

    def test_missing_audio_reports_actionable_failure_and_closes_client(self):
        client = Mock()
        client.models.generate_content.return_value = SimpleNamespace(candidates=[])
        cfg = {'prompts':{'tts_audio':{'user_prompt':'{text}'}}}
        with patch.object(engine, 'config', return_value=cfg), patch.object(engine, 'client_and_model', return_value=(client, 'tts')):
            with self.assertRaisesRegex(ValueError, 'no audio'):
                engine.gemini_speech(Path('/tmp/not-created-preview.wav'), 'Test', {})
        client.close.assert_called_once()

    def test_preview_requires_bounded_text(self):
        for value in ['', 'x'*501]:
            with self.assertRaises(ValueError): engine.preview_tts(Path('/tmp/not-created-preview.wav'), value)

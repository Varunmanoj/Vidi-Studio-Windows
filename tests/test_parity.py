import json, sys, unittest, tempfile
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'engine'))
import parity
import vidistudio_engine as engine
class Parity(unittest.TestCase):
    def test_shared_multilingual_guidance_applies_only_to_media_understanding(self):
        prompts = {
            'multilingual_media': {'system_instruction': 'Understand every language.', 'user_prompt': 'Preserve original scripts.'},
            'transcript': {'system_instruction': 'Transcript system.', 'user_prompt': 'Transcript user.'},
            'text_rewrite': {'system_instruction': 'Rewrite system.', 'user_prompt': 'Rewrite user.'},
        }
        transcript = parity.media_prompt_pair({'prompts': prompts}, 'transcript')
        self.assertEqual(transcript['system_instruction'], 'Transcript system.\n\nUnderstand every language.')
        self.assertEqual(transcript['user_prompt'], 'Transcript user.\n\nPreserve original scripts.')
        rewrite = parity.media_prompt_pair({'prompts': prompts}, 'text_rewrite')
        self.assertEqual(rewrite, prompts['text_rewrite'])
        self.assertEqual(parity.MULTILINGUAL_MEDIA_PROMPT_KEYS, {
            'transcript', 'youtube_transcript', 'summary', 'audio_descriptions', 'screen_text', 'youtube_screen_text'
        })

    def test_url_validation(self):
        expected='https://www.youtube.com/watch?v=4fYlQtcm6qY'
        self.assertEqual(parity.youtube_url('https://youtu.be/4fYlQtcm6qY'),expected)
        self.assertEqual(parity.youtube_url('https://www.youtube.com/watch?v=4fYlQtcm6qY'),expected)
        for url in ['https://youtube.com.evil.test/watch?v=hYaQffYszuM','file:///tmp/test','https://youtube.com/watch?v=bad']:
            with self.assertRaises(ValueError):parity.youtube_url(url)
    def test_subtitles_and_undo(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);(root/'exports').mkdir();(root/'audio-description').mkdir()
            old={'id':'a','start':1.,'end':2.5,'text':'Hello'}
            project={'name':'Dilemma / Episode: 3','duration':5,'transcript':[old.copy()],'cues':[old | {'start':3.,'end':4.}],'exports':[]}
            parity.remember(project);project['cues'][0]['text']='Edited'
            e=SimpleNamespace(
                atomic_json=engine.atomic_json,
                validate_cues=engine.validate_cues,
                local=engine.local,
                write_text_export=engine.write_text_export,
            )
            args=SimpleNamespace(command='undo-cues')
            parity.execute(args,root,project,e)
            self.assertEqual(project['cues'][0]['text'],'Hello')
            args.command='redo-cues';parity.execute(args,root,project,e)
            self.assertEqual(project['cues'][0]['text'],'Edited')
            args=SimpleNamespace(command='export-subtitles',content='transcript',format='vtt',replace_existing=False)
            parity.execute(args,root,project,e)
            text=(root/project['exports'][0]).read_text()
            self.assertIn('00:00:01.000 --> 00:00:02.500',text)
            self.assertTrue(text.startswith('WEBVTT'))
            self.assertEqual(project['exports'][0],'exports/Dilemma - Episode - 3 - Transcript.vtt')
            with self.assertRaisesRegex(ValueError,'Confirm replacement'):
                parity.execute(args,root,project,e)
            args.replace_existing=True
            parity.execute(args,root,project,e)
            self.assertEqual(project['exports'],['exports/Dilemma - Episode - 3 - Transcript.vtt'])
            project['summary']='A concise project summary.'
            args=SimpleNamespace(command='export-subtitles',content='summary',format='md',replace_existing=False)
            parity.execute(args,root,project,e)
            summary=(root/project['exports'][1]).read_text()
            self.assertEqual(summary,'# Summary\n\nA concise project summary.\n')
            self.assertEqual(project['exports'][1],'exports/Dilemma - Episode - 3 - Summary.md')
            args=SimpleNamespace(command='export-subtitles',content='combined',format='srt',replace_existing=False)
            parity.execute(args,root,project,e)
            self.assertEqual(project['exports'][2],'exports/Dilemma - Episode - 3 - AD Transcript.srt')
            deleted=project['exports'][0]
            args=SimpleNamespace(command='delete-export',name=deleted)
            parity.execute(args,root,project,e)
            self.assertNotIn(deleted,project['exports'])
            self.assertFalse((root/deleted).exists())
            self.assertFalse((root/'metadata/trash').exists())

    def test_invalid_api_key_check(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = Path(tmp) / 'config.json'
            cfg.write_text(json.dumps({'apiKey': 'invalid_test_key_12345'}), encoding='utf-8')
            old_cfg = engine.os.environ.get('VIDISTUDIO_CONFIG')
            engine.os.environ['VIDISTUDIO_CONFIG'] = str(cfg)
            events = []
            old_emit = engine.emit
            engine.emit = lambda msg, **kwargs: events.append((msg, kwargs))
            try:
                engine.check_api_key()
                self.assertTrue(len(events) > 0)
                msg, data = events[-1]
                self.assertFalse(data.get('keyValid'))
                self.assertEqual(data.get('plan'), 'invalid')
                saved = json.loads(cfg.read_text(encoding='utf-8'))
                self.assertFalse(saved.get('apiKeyValid'))
                self.assertEqual(saved.get('apiPlan'), 'invalid')
            finally:
                engine.emit = old_emit
                if old_cfg is not None:
                    engine.os.environ['VIDISTUDIO_CONFIG'] = old_cfg
                else:
                    engine.os.environ.pop('VIDISTUDIO_CONFIG', None)

    def test_settings_store_safe_types(self):
        root = Path(__file__).resolve().parents[1]
        settings_store_path = root / 'Vidi Studio Windows' / 'Services' / 'SettingsStore.cs'
        if not settings_store_path.is_file():
            settings_store_path = root / 'Vidi-Studio-Windows' / 'Vidi Studio Windows' / 'Services' / 'SettingsStore.cs'
        if settings_store_path.is_file():
            source = settings_store_path.read_text(encoding='utf-8')
            self.assertIn('public static string GetString(JsonNode? node, string fallback = "")', source)
            self.assertIn('public static double GetDouble(JsonNode? node, double fallback = 1)', source)
            self.assertIn('public static bool GetBool(JsonNode? node, bool fallback = false)', source)
            self.assertNotIn('Data[key]?.GetValue<string>()', source)
            self.assertIn('GetString(Data[key], fallback)', source)
            self.assertIn('val.TryGetValue<bool>(out var b)', source)

        dialog_path = settings_store_path.parent.parent / 'Views' / 'SettingsDialog.cs'
        if dialog_path.is_file():
            dialog_source = dialog_path.read_text(encoding='utf-8')
            self.assertNotIn('draft["apiKeyValid"]?.GetValue<string>()', dialog_source)
            self.assertIn('SettingsStore.GetString(draft["apiKeyValid"]', dialog_source)

    def test_api_key_reads_from_environment_not_disk(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = Path(tmp) / 'config.json'
            cfg.write_text(json.dumps({'apiKey': 'plaintext-in-file'}), encoding='utf-8')
            old_cfg = engine.os.environ.get('VIDISTUDIO_CONFIG')
            old_key = engine.os.environ.get('VIDISTUDIO_API_KEY')
            engine.os.environ['VIDISTUDIO_CONFIG'] = str(cfg)
            engine.os.environ['VIDISTUDIO_API_KEY'] = 'from-credential-store'
            try:
                self.assertEqual(engine.config().get('apiKey'), 'from-credential-store')
            finally:
                if old_cfg is None: engine.os.environ.pop('VIDISTUDIO_CONFIG', None)
                else: engine.os.environ['VIDISTUDIO_CONFIG'] = old_cfg
                if old_key is None: engine.os.environ.pop('VIDISTUDIO_API_KEY', None)
                else: engine.os.environ['VIDISTUDIO_API_KEY'] = old_key

    def test_api_key_is_stored_in_credential_manager_not_config(self):
        root = Path(__file__).resolve().parents[1]
        credential = root / 'Vidi Studio Windows' / 'Services' / 'CredentialStore.cs'
        if not credential.is_file():
            credential = root / 'Vidi-Studio-Windows' / 'Vidi Studio Windows' / 'Services' / 'CredentialStore.cs'
        self.assertTrue(credential.is_file(), 'Windows must store the Gemini key in Credential Manager')
        source = credential.read_text(encoding='utf-8')
        for token in ('CredWriteW', 'CredReadW', 'CredDeleteW', 'CredTypeGeneric'):
            self.assertIn(token, source)
        services = credential.parent
        settings = services / 'SettingsStore.cs'
        if settings.is_file():
            settings_source = settings.read_text(encoding='utf-8')
            self.assertIn('CredentialStore.Read()', settings_source)
            self.assertIn('Data.Remove("apiKey")', settings_source)
        engine_client = services / 'EngineClient.cs'
        if engine_client.is_file():
            self.assertIn('VIDISTUDIO_API_KEY', engine_client.read_text(encoding='utf-8'))
        self.assertIn('VIDISTUDIO_API_KEY', (root / 'engine' / 'vidistudio_engine.py').read_text(encoding='utf-8'))

    def test_engine_sources_are_copied_unconditionally(self):
        root = Path(__file__).resolve().parents[1]
        csproj = root / 'Vidi Studio Windows' / 'Vidi Studio Windows.csproj'
        if not csproj.is_file():
            csproj = root / 'Vidi-Studio-Windows' / 'Vidi Studio Windows' / 'Vidi Studio Windows.csproj'
        if not csproj.is_file():
            return
        text = csproj.read_text(encoding='utf-8')
        engine_lines = [line for line in text.splitlines() if '..\\engine\\*.py' in line]
        self.assertTrue(engine_lines, 'engine sources must be copied into the app output')
        for line in engine_lines:
            self.assertIn('CopyToOutputDirectory="Always"', line)

    def test_app_version_parity(self):
        script = Path(__file__).resolve().parents[1] / 'script' / 'sync_version.py'
        if script.exists():
            import subprocess
            res = subprocess.run([sys.executable, str(script), '--check'], capture_output=True, text=True)
            self.assertEqual(res.returncode, 0, f"App versions are out of sync across Windows and macOS:\n{res.stdout}\n{res.stderr}")

if __name__=='__main__':unittest.main()

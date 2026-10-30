import array
import contextlib
import json
import math
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from test_engine import engine
import youtube_audio


class YouTubeAudio(unittest.TestCase):
    def test_worker_error_protocol_is_specific_and_never_contains_stream_urls(self):
        with tempfile.TemporaryDirectory() as directory:
            for detail, code in (
                ('HTTP Error 403: https://stream.invalid/?token=secret', 'blocked'),
                ('Connection timed out for https://stream.invalid/?token=secret', 'network'),
                ('Sign in to confirm your age: https://stream.invalid/?token=secret', 'restricted'),
                ('FFmpeg is unavailable. Set its path in Settings.', 'ffmpeg'),
                ('Deno challenge failed: https://stream.invalid/?token=secret', 'runtime'),
            ):
                with self.subTest(code=code):
                    failure = youtube_audio.write_worker_error(directory, RuntimeError(detail))
                    self.assertEqual(failure.code, code)
                    saved = (Path(directory)/youtube_audio.ERROR_FILE).read_text(encoding='utf-8')
                    self.assertEqual(json.loads(saved), {'code': code})
                    self.assertNotIn('secret', str(failure))
                    self.assertEqual(youtube_audio.read_worker_error(directory).code, code)
            (Path(directory)/youtube_audio.ERROR_FILE).write_text('{"code":"https://stream.invalid/?token=secret"}', encoding='utf-8')
            self.assertEqual(youtube_audio.read_worker_error(directory).code, 'worker')
            (Path(directory)/youtube_audio.ERROR_FILE).write_text('invalid json', encoding='utf-8')
            self.assertEqual(youtube_audio.read_worker_error(directory).code, 'worker')

    def run_download_workers(self, failures):
        real_popen = subprocess.Popen
        directories = []
        def start(command, **kwargs):
            if command[0] != sys.executable:
                return real_popen(command, **kwargs)
            directory = Path(command[-1])
            if directories:
                self.assertFalse(directories[-1].exists(), 'A retry retained the failed download')
            directories.append(directory)
            index = len(directories) - 1
            code = failures[index] if index < len(failures) else None
            script = ('import json,sys; from pathlib import Path; folder=Path(sys.argv[1]); '
                      + ('(folder/"download-error.json").write_text(json.dumps({"code":sys.argv[2]})); '
                         '(folder/"soundtrack.part").write_bytes(b"partial"); sys.exit(1)' if code else
                         '(folder/"soundtrack.webm").write_bytes(b"complete")'))
            return real_popen([sys.executable, '-c', script, str(directory), code or ''], **kwargs)
        return directories, patch.object(youtube_audio.subprocess, 'Popen', side_effect=start)

    def test_temporary_failure_retries_with_new_worker_and_removes_partial_files(self):
        directories, workers = self.run_download_workers(['blocked'])
        emit = Mock()
        with workers, youtube_audio.soundtrack('https://youtu.be/abcdefghijk', emit) as soundtrack:
            self.assertEqual(soundtrack.read_bytes(), b'complete')
            self.assertEqual(len(directories), 2)
        self.assertTrue(all(not folder.exists() for folder in directories))
        self.assertTrue(any('Retrying' in call.args[0] for call in emit.call_args_list))

    def test_retry_backoff_matches_mac_and_remains_cancellable(self):
        self.assertEqual(youtube_audio.DOWNLOAD_ATTEMPTS, 3)
        self.assertEqual(youtube_audio.RETRY_DELAYS, (2, 4))
        @contextlib.contextmanager
        def failed(*args):
            yield youtube_audio.DownloadError('network')
        with tempfile.TemporaryDirectory() as folder:
            marker = Path(folder) / 'cancel'
            def cancel(_): marker.touch()
            with patch.dict(os.environ, VIDISTUDIO_CANCEL_FILE=str(marker)), \
                 patch.object(youtube_audio, 'soundtrack_attempt', failed), \
                 patch.object(youtube_audio.time, 'sleep', side_effect=cancel), \
                 self.assertRaisesRegex(RuntimeError, 'cancelled'):
                with youtube_audio.soundtrack('https://youtu.be/abcdefghijk', Mock()):
                    self.fail('Cancelled download yielded audio')

    def test_repeated_or_permanent_download_failure_stays_bounded_and_specific(self):
        for failures, expected_count in ((['network', 'network', 'network'], 3), (['restricted'], 1), (['runtime'], 1)):
            with self.subTest(failures=failures):
                directories, workers = self.run_download_workers(failures)
                with workers, self.assertRaises(youtube_audio.DownloadError) as error:
                    with youtube_audio.soundtrack('https://youtu.be/abcdefghijk', Mock()):
                        pass
                self.assertEqual(error.exception.code, failures[-1])
                self.assertEqual(len(directories), expected_count)
                self.assertTrue(all(not folder.exists() for folder in directories))

    def test_missing_media_tools_are_reported_before_download(self):
        for missing in ('ffmpeg', 'ffprobe'):
            with tempfile.TemporaryDirectory() as temporary:
                project = dict(sourceType='youtube', duration=0, cues=[])
                def tool(name):
                    if name == missing:
                        raise ValueError(f'{name} is unavailable. Set its path in Settings.')
                    return 'test.exe'
                with patch.object(engine, 'tool', side_effect=tool), patch.object(youtube_audio, 'soundtrack') as download:
                    with self.assertRaisesRegex(ValueError, missing + ' is unavailable'):
                        engine.export_youtube_audio(Path(temporary), project)
                    download.assert_not_called()
                    self.assertFalse((Path(temporary)/'exports').exists())

    def test_youtube_audio_only_needs_no_narration_or_generated_duration(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root/'original.wav'
            engine.run([engine.tool('ffmpeg'), '-y', '-v', 'error', '-f', 'lavfi',
                        '-i', 'sine=frequency=440:sample_rate=48000', '-t', '1.5', source])
            project = dict(name='YouTube Original', sourceType='youtube', source='',
                           youtubeURL='https://youtu.be/abcdefghijk', duration=0,
                           transcript=[], cues=[], exports=[])

            @contextlib.contextmanager
            def soundtrack(*args):
                yield source

            with patch.object(youtube_audio, 'soundtrack', soundtrack), \
                 patch.object(engine, 'exportable_narration_cues', side_effect=AssertionError('Narration not needed')):
                engine.export_youtube_audio(root, project)
                self.assertEqual(project['exports'], ['exports/YouTube Original - YouTube Audio Only.mp3'])
                output = root/project['exports'][0]
                engine.validate_audio_export(output, 1.5)
                pcm = subprocess.check_output([engine.tool('ffmpeg'), '-v', 'error', '-i', str(output),
                                               '-f', 'f32le', '-ac', '1', '-ar', '48000', '-'])
                samples = array.array('f', pcm)[4800:19200]
                rms = math.sqrt(sum(value*value for value in samples)/len(samples))
                self.assertGreater(rms, .08)
                before = output.read_bytes()
                with self.assertRaisesRegex(ValueError, 'Confirm replacement'):
                    engine.export_youtube_audio(root, project)
                self.assertEqual(output.read_bytes(), before)
                self.assertFalse(list((root/'exports').glob('.*generating*')))
                project['duration'] = 100
                engine.export_youtube_audio(root, project, replace_existing=True)
                engine.validate_audio_export(output, 1.5)

    def test_prefers_original_audio_over_a_higher_quality_dub(self):
        original = dict(format_id='original', vcodec='none', acodec='opus', format_note='English original')
        dub = dict(format_id='dub', vcodec='none', acodec='opus', format_note='Hindi dubbed')
        video = dict(format_id='video', vcodec='h264', acodec='aac')
        self.assertEqual(list(youtube_audio.original_audio_format({'formats': [original, dub, video]})), [original])

    def test_download_does_not_expose_signed_urls_or_use_browser_credentials(self):
        downloader = Mock()
        downloader.extract_info.side_effect = RuntimeError('403 https://stream.invalid/?token=private')
        with patch('yt_dlp.YoutubeDL') as provider:
            provider.return_value.__enter__.return_value = downloader
            with self.assertRaises(ValueError) as error:
                youtube_audio.download('https://youtu.be/abcdefghijk', '/tmp/test', Mock())
            self.assertNotIn('token=', str(error.exception))
            options = provider.call_args.args[0]
            self.assertFalse(options['cachedir'])
            self.assertNotIn('cookiesfrombrowser', options)
            self.assertNotIn('cookiefile', options)
            self.assertFalse(options['remote_components'])

    def test_rejects_live_or_restricted_videos_before_downloading(self):
        for info in ({'is_live': True}, {'availability': 'private'}, {'_type': 'playlist'}):
            with self.subTest(info=info), patch('yt_dlp.YoutubeDL') as provider:
                downloader = provider.return_value.__enter__.return_value
                downloader.extract_info.return_value = info
                with self.assertRaises(ValueError):
                    youtube_audio.download('https://youtu.be/abcdefghijk', '/tmp/test', Mock())
                downloader.process_info.assert_not_called()

    def test_invalid_url_starts_no_worker(self):
        with patch.object(youtube_audio.subprocess, 'Popen') as popen:
            with self.assertRaises(ValueError):
                with youtube_audio.soundtrack('https://example.com/file', Mock()):
                    pass
            popen.assert_not_called()

    def test_timeout_and_cancellation_stop_worker_and_remove_download(self):
        real_popen = subprocess.Popen
        for cancelled in (False, True):
            captured = {}

            def start(command, **kwargs):
                if command[0] != sys.executable:
                    return real_popen(command, **kwargs)
                directory = Path(command[-1])
                (directory/'soundtrack.part').write_bytes(b'incomplete')
                captured['directory'] = directory
                process = real_popen([sys.executable, '-c', 'import time; time.sleep(60)'], **kwargs)
                captured['process'] = process
                return process

            with patch.object(youtube_audio.subprocess, 'Popen', side_effect=start), \
                 patch.object(youtube_audio, 'DOWNLOAD_TIMEOUT', 0):
                expected = engine.ProcessingCancelled if cancelled else ValueError
                with patch.object(youtube_audio.time, 'monotonic', side_effect=
                                  [0, engine.ProcessingCancelled('Cancelled')] if cancelled else [0, 1]):
                    with self.assertRaises(expected):
                        with youtube_audio.soundtrack('https://youtu.be/abcdefghijk', Mock()):
                            pass
            process = captured['process']
            self.assertIsNotNone(process.poll())
            self.assertFalse(captured['directory'].exists())

    def test_complete_mix_retains_original_audio_and_times_narration(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root/'audio-description').mkdir()
            source = root/'original.wav'
            narration = root/'audio-description/cue.wav'
            engine.run([engine.tool('ffmpeg'), '-y', '-v', 'error', '-f', 'lavfi',
                        '-i', 'sine=frequency=440:sample_rate=48000', '-t', '3', source])
            engine.run([engine.tool('ffmpeg'), '-y', '-v', 'error', '-f', 'lavfi',
                        '-i', 'sine=frequency=880:sample_rate=48000', '-t', '0.5', narration])
            project = dict(name='YouTube Demo', sourceType='youtube', source='',
                           youtubeURL='https://youtu.be/abcdefghijk', duration=3, hasAudio=True,
                           transcript=[], exports=[], cues=[dict(id='ad', start=1, end=1.6,
                           text='A shape appears.', audio='audio-description/cue.wav', audioText='A shape appears.')])

            @contextlib.contextmanager
            def soundtrack(*args):
                yield source

            with patch.object(youtube_audio, 'soundtrack', soundtrack):
                engine.export_youtube_described_audio(root, project)
                output = root/project['exports'][0]
                engine.validate_audio_export(output, 3)
                data = subprocess.check_output([engine.tool('ffmpeg'), '-v', 'error', '-i', str(output),
                                                '-f', 'f32le', '-ac', '1', '-ar', '48000', '-'])
                samples = array.array('f', data)

                def amplitude(frequency, start, end):
                    segment = samples[round(start*48000):round(end*48000)]
                    real = sum(value*math.cos(2*math.pi*frequency*i/48000) for i, value in enumerate(segment))
                    imag = sum(value*math.sin(2*math.pi*frequency*i/48000) for i, value in enumerate(segment))
                    return 2*math.hypot(real, imag)/len(segment)

                original_before = amplitude(440, .2, .7)
                original_during = amplitude(440, 1.1, 1.4)
                self.assertGreater(original_before, .08)
                self.assertAlmostEqual(original_during/original_before, .2, delta=.05)
                self.assertGreater(amplitude(880, 1.1, 1.4), .08)
                self.assertLess(amplitude(880, .2, .7), .002)
                before = output.read_bytes()
                with self.assertRaisesRegex(ValueError, 'Confirm replacement'):
                    engine.export_youtube_described_audio(root, project)
                project['duration'] = 6
                with self.assertRaisesRegex(ValueError, 'duration does not match'):
                    engine.export_youtube_described_audio(root, project, replace_existing=True)
                self.assertEqual(output.read_bytes(), before)
                self.assertFalse(list((root/'exports').glob('.*generating*')))
                project['duration'] = 3
                project['cues'][0]['audioText'] = 'Old narration'
                with patch.object(youtube_audio, 'soundtrack') as download:
                    with self.assertRaisesRegex(ValueError, 'missing or outdated'):
                        engine.export_youtube_described_audio(root, project, replace_existing=True)
                    download.assert_not_called()


if __name__ == '__main__':
    unittest.main()

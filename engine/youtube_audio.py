"""Temporary public YouTube soundtrack retrieval for described-audio exports."""
import contextlib
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import parity

DOWNLOAD_TIMEOUT = 900
DOWNLOAD_ATTEMPTS = 3
RETRY_DELAYS = (2, 4)
MAX_AUDIO_BYTES = 2 * 1024 * 1024 * 1024
DOWNLOAD_ERROR = ('YouTube audio could not be downloaded. Check your connection and that the video '
                  'is public, then try again. If YouTube blocks the download, import your original '
                  'video as a local project and export its described audio instead.')
ERROR_FILE = 'download-error.json'
ERROR_MESSAGES = {
    'download': DOWNLOAD_ERROR,
    'network': 'The YouTube audio download lost its connection or timed out. Check your connection and try again.',
    'blocked': 'YouTube refused the audio download (HTTP 403 or 429). Try again later, or import your original video.',
    'restricted': 'YouTube requires sign-in or restricts this video. Choose a public unrestricted video, or import your original video.',
    'live': 'Wait until the YouTube live video has finished processing before exporting.',
    'video': 'Choose one public YouTube video.',
    'audio': 'This YouTube video has no downloadable audio-only track.',
    'size': 'The YouTube audio is too large to download. Import your original video instead.',
    'runtime': 'The YouTube JavaScript runtime could not run. Rebuild the bundled engine and try again.',
    'ffmpeg': 'FFmpeg is unavailable. Set its executable path in Settings → Storage.',
    'worker': 'The YouTube download worker could not finish. Rebuild the bundled engine and try again.',
}
RETRYABLE_ERRORS = {'download', 'network', 'blocked'}


class DownloadError(ValueError):
    def __init__(self, code='download'):
        self.code = code if code in ERROR_MESSAGES else 'download'
        super().__init__(ERROR_MESSAGES[self.code])


def download_error(error):
    """Map diagnostics to fixed messages; never return signed URLs or tokens."""
    if isinstance(error, DownloadError):
        return error
    message = str(error).lower()
    if 'ffmpeg is unavailable' in message:
        code = 'ffmpeg'
    elif any(word in message for word in ('sign in', 'sign-in', 'private video', 'age-restricted', 'not available in your country')):
        code = 'restricted'
    elif any(word in message for word in ('http error 403', 'http error 429')):
        code = 'blocked'
    elif any(word in message for word in ('timed out', 'timeout', 'connection', 'network', 'certificate', 'ssl', 'http error 503')):
        code = 'network'
    elif any(word in message for word in ('deno', 'javascript runtime', 'signature', 'challenge', 'yt_dlp_ejs')):
        code = 'runtime'
    elif 'no downloadable audio-only track' in message:
        code = 'audio'
    else:
        code = 'download'
    return DownloadError(code)


def write_worker_error(directory, error):
    failure = download_error(error)
    # Only a known code crosses the worker boundary. The file stays temporary.
    (Path(directory) / ERROR_FILE).write_text(json.dumps({'code': failure.code}), encoding='utf-8')
    return failure


def read_worker_error(directory):
    try:
        code = json.loads((Path(directory) / ERROR_FILE).read_text(encoding='utf-8'))['code']
        return DownloadError(code) if code in ERROR_MESSAGES else DownloadError('worker')
    except (OSError, ValueError, KeyError, TypeError):
        return DownloadError('worker')


def check_cancelled():
    marker = os.environ.get('VIDISTUDIO_CANCEL_FILE')
    if marker and Path(marker).exists():
        raise RuntimeError('Processing cancelled. Completed results are saved.')


def javascript_runtime():
    suffix = '.exe' if os.name == 'nt' else ''
    bundled = Path(sys.executable).parent / 'media' / ('deno' + suffix)
    if bundled.is_file():
        return bundled
    # Source builds use the same locked runtime that gets copied into the app.
    from deno._find_deno import find_deno_bin
    return Path(find_deno_bin())


def original_audio_format(context):
    formats = [item for item in context['formats']
               if item.get('vcodec') == 'none' and item.get('acodec') not in (None, 'none')]
    original = [item for item in formats if 'original' in str(item.get('format_note', '')).lower()
                or str(item.get('language', '')).endswith('-orig')]
    if not formats:
        raise DownloadError('audio')
    yield (original or formats)[-1]


def download(url, directory, emit, ffmpeg_path=None):
    """Worker: no browser cookies, login, external config, or saved stream URLs."""
    import yt_dlp
    url = parity.youtube_url(url)

    class QuietLogger:
        def debug(self, message): pass
        def warning(self, message): pass
        def error(self, message): pass

    last_update = 0

    def progress(status):
        nonlocal last_update
        downloaded = status.get('downloaded_bytes', 0)
        if downloaded > MAX_AUDIO_BYTES:
            raise DownloadError('size')
        now = time.monotonic()
        if status['status'] == 'downloading' and now - last_update >= 1:
            total = status.get('total_bytes') or status.get('total_bytes_estimate')
            percentage = min(100, round(downloaded * 100 / total)) if total else None
            emit('Downloading original YouTube audio' + (f' — {percentage}%' if percentage is not None else ''),
                 exportProgress=min(0.25, downloaded / total * 0.25) if total else None)
            last_update = now

    options = {
        'format': original_audio_format, 'noplaylist': True, 'quiet': True,
        'logger': QuietLogger(), 'progress_hooks': [progress],
        'outtmpl': str(Path(directory) / 'soundtrack.%(ext)s'),
        'cachedir': False, 'socket_timeout': 20, 'retries': 2, 'fragment_retries': 2,
        'extractor_retries': 2, 'max_filesize': MAX_AUDIO_BYTES,
        'remote_components': set(),
    }
    if ffmpeg_path:
        options['ffmpeg_location'] = str(Path(ffmpeg_path).parent)
    try:
        options['js_runtimes'] = {'deno': {'path': str(javascript_runtime())}}
        with yt_dlp.YoutubeDL(options) as downloader:
            info = downloader.extract_info(url, download=False)
            if not info or info.get('_type', 'video') != 'video':
                raise DownloadError('video')
            if info.get('is_live') or info.get('live_status') in ('is_live', 'is_upcoming', 'post_live'):
                raise DownloadError('live')
            if info.get('availability') not in (None, 'public'):
                raise DownloadError('restricted')
            downloader.process_info(info)
    except Exception as error:
        # Downloader errors may contain temporary signed stream URLs. Keep them
        # out of stdout, application logs, and saved project errors.
        raise download_error(error) from None


def stop_worker(process):
    if process.poll() is None:
        try:
            if os.name == 'nt':
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
                if process.poll() is None:
                    process.kill()
            else:
                os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()


@contextlib.contextmanager
def soundtrack(url, emit):
    """Bound the entire download and kill its runtime on timeout/cancellation."""
    url = parity.youtube_url(url)
    deadline = time.monotonic() + DOWNLOAD_TIMEOUT
    for attempt in range(DOWNLOAD_ATTEMPTS):
        with soundtrack_attempt(url, emit, deadline) as result:
            if not isinstance(result, DownloadError):
                yield result
                return
            failure = result
        if attempt + 1 == DOWNLOAD_ATTEMPTS or failure.code not in RETRYABLE_ERRORS:
            raise failure
        check_cancelled()
        emit(f'Retrying YouTube audio download — attempt {attempt + 2} of {DOWNLOAD_ATTEMPTS}', exportProgress=None)
        # Match the Mac retry/backoff policy while keeping Windows cancellation responsive.
        retry_at = min(deadline, time.monotonic() + RETRY_DELAYS[attempt])
        while (remaining := retry_at - time.monotonic()) > 0:
            check_cancelled()
            time.sleep(min(.1, remaining))
        if time.monotonic() >= deadline:
            raise ValueError('YouTube audio download timed out. Try again, or import your original video.')


@contextlib.contextmanager
def soundtrack_attempt(url, emit, deadline):
    with tempfile.TemporaryDirectory(prefix='vidistudio-youtube-audio-') as temporary:
        command = [sys.executable]
        if not getattr(sys, 'frozen', False):
            command.append(str(Path(__file__).with_name('vidistudio_engine.py')))
        command += ['download-youtube-audio', '--url', url, '--output', temporary]
        emit('Downloading original YouTube audio', exportProgress=None)
        process = subprocess.Popen(command, stderr=subprocess.DEVNULL, start_new_session=os.name != 'nt')
        try:
            while process.poll() is None:
                check_cancelled()
                if time.monotonic() >= deadline:
                    raise ValueError('YouTube audio download timed out. Try again, or import your original video.')
                try:
                    process.wait(timeout=.25)
                except subprocess.TimeoutExpired:
                    pass
            if process.returncode:
                yield read_worker_error(temporary)
                return
            files = [item for item in Path(temporary).glob('soundtrack.*')
                     if item.is_file() and item.suffix not in ('.part', '.ytdl')]
            if len(files) != 1 or not files[0].stat().st_size or files[0].stat().st_size > MAX_AUDIO_BYTES:
                yield DownloadError()
                return
            yield files[0]
        finally:
            stop_worker(process)

"""Out-of-process Windows OneCore/SAPI/Piper speech, shared by preview and narration."""
import json
import os
import subprocess
from pathlib import Path

# "windows-natural" was an earlier engine name for SAPI 5 voices, so it is accepted
# and handled as SAPI 5 while saved settings are migrated.
SAPI_ENGINES = ('sapi5',)
LOCAL_ENGINES = ('windows-core', *SAPI_ENGINES, 'piper')
CRASHED = ('The speech helper stopped unexpectedly (exit code {code}). The selected voice may have stopped it; '
           'try again or choose another voice.')


def request(config, command, **kwargs):
    mode = config.get('ttsMode', 'windows-core')
    if mode == 'system':
        mode = 'windows-core'
    if mode == 'windows-natural':
        mode = 'sapi5'
    if mode not in LOCAL_ENGINES:
        raise ValueError('Choose Windows OneCore, Windows SAPI 5, or Piper speech.')
    payload = dict(command=command, engine=mode, voice=config.get('systemVoice', ''), **kwargs)
    if mode == 'piper':
        payload.update(piper_payload(config))
    host = config.get('sapiHostPath') if mode in SAPI_ENGINES else None
    host = host or os.environ.get('VIDISTUDIO_SPEECH_HOST')
    if not host or not Path(host).is_file():
        raise ValueError('Windows speech host is missing. Build the speech host or select its executable in Settings.')
    result = subprocess.run([host], input=json.dumps(payload), encoding='utf-8',
                            capture_output=True, timeout=300,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        # The helper's first stderr line is the explanation; keep the start, not the end.
        detail = (result.stderr or '').strip() or CRASHED.format(code=result.returncode)
        raise ValueError('Windows speech failed: ' + detail[:1000])
    return json.loads(result.stdout)


def piper_payload(config):
    """Resolves the bundled Piper runtime and the downloaded voice selected in Settings."""
    runtime = (os.environ.get('VIDISTUDIO_PIPER_PATH') or '').strip()
    if not runtime or not Path(runtime).is_file():
        raise ValueError('The bundled Piper speech engine is missing. Reinstall Vidi Studio, then try again.')
    voice = str(config.get('piperVoice') or config.get('systemVoice') or '').strip()
    if not voice:
        raise ValueError('Choose a Piper voice in Settings. Download one first if the list is empty.')
    folder = (os.environ.get('VIDISTUDIO_PIPER_VOICES') or '').strip()
    if not folder:
        raise ValueError('The Piper voice folder is unavailable. Restart Vidi Studio, then try again.')
    model = Path(folder) / (voice + '.onnx')
    settings = Path(folder) / (voice + '.onnx.json')
    if not model.is_file() or not settings.is_file():
        raise ValueError('The selected Piper voice is not downloaded. Download it in Settings, then try again.')
    return dict(piperPath=runtime, piperModel=str(model), piperConfig=str(settings), piperSpeaker=0)

"""Out-of-process Windows OneCore/SAPI/Natural speech, shared by preview and narration."""
import json
import os
import subprocess
from pathlib import Path

# Narrator natural voices are synthesized through a SAPI 5 bridge, so the SAPI host
# override (for example an x64 host for an x64-only bridge) applies to both engines.
SAPI_ENGINES = ('sapi5', 'windows-natural')


def request(config, command, **kwargs):
    mode = config.get('ttsMode', 'windows-core')
    if mode == 'system':
        mode = 'windows-core'
    if mode not in ('windows-core', *SAPI_ENGINES):
        raise ValueError('Choose Windows OneCore, SAPI 5 or Windows Natural speech.')
    host = config.get('sapiHostPath') if mode in SAPI_ENGINES else None
    host = host or os.environ.get('VIDISTUDIO_SPEECH_HOST')
    if not host or not Path(host).is_file():
        raise ValueError('Windows speech host is missing. Build the speech host or select its executable in Settings.')
    payload = dict(command=command, engine=mode, voice=config.get('systemVoice', ''), **kwargs)
    result = subprocess.run([host], input=json.dumps(payload), encoding='utf-8',
                            capture_output=True, timeout=300,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        raise ValueError('Windows speech failed: ' + result.stderr[-1000:])
    return json.loads(result.stdout)

#!/usr/bin/env python3
"""Run the combined-workspace desktop menu checker."""
from pathlib import Path
import os
import runpy
import sys

workspace = Path(os.environ.get('VIDISTUDIO_DESKTOP_WORKSPACE', Path(__file__).resolve().parents[2]))
checker = workspace / 'script/check_menu_parity.py'
if not checker.is_file():
    if '--allow-missing' in sys.argv:
        print('SKIP: desktop menu parity requires the combined Mac/Windows workspace.')
        raise SystemExit(0)
    raise SystemExit('Desktop menu parity requires the combined Mac/Windows workspace.')
sys.argv[1:1] = ['--windows-root', str(Path(__file__).resolve().parents[1])]
runpy.run_path(str(checker), run_name='__main__')

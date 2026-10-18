#!/usr/bin/env python3
"""Reject MainWindow inspector field references missing from current XAML.

Only production partial classes are checked. WinUI-generated bin/obj output is
not source; use build-windows.ps1 -Rebuild to regenerate stale connector files.
Stale compiler state can reference retired controls even after Rebuild. Use
`--deep` (run inside the failing Windows checkout) to locate every file —
including ignored build output — that still contains a retired name.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RETIRED_INSPECTOR_NAMES = (
    'InspectorRegenerateAllNarrationButton',
    'InspectorNarrateSelectedButton',
)


def missing_inspector_references(xaml, sources):
    name_key = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
    names = {element.get(name_key) for element in ET.fromstring(xaml).iter()}
    missing = []
    for path, source in sources.items():
        for number, line in enumerate(source.splitlines(), 1):
            for name in set(re.findall(r'\bInspector[A-Z]\w*\b', line)) - names:
                missing.append(f'{path}:{number}: {name} is referenced in C# but not declared in MainWindow.xaml')
    return sorted(missing)


def deep_retired_identifier_hits(root, names=RETIRED_INSPECTOR_NAMES):
    """Return file/line hits in `root` (any file, including ignored output) that contain `names`."""
    self_referential = {'README.md', 'PROGRESS.md', 'check_xaml_references.py'}
    hits = []
    for path in sorted(root.rglob('*')):
        if not path.is_file() or path.suffix == '.pyc':
            continue
        relative = path.relative_to(root)
        if any(part == '__pycache__' or part == 'tests' for part in relative.parts):
            continue
        if path.name in self_referential:
            continue
        try:
            blob = path.read_bytes()
        except OSError:
            continue
        for name in names:
            if name.encode('utf-8') not in blob:
                continue
            text = blob.decode('utf-8', 'ignore')
            for number, line in enumerate(text.splitlines(), 1):
                if name in line:
                    hits.append(f'{path}: {number}: {name}')
    return hits


def main(argv=None):
    if '--deep' in (argv if argv is not None else sys.argv[1:]):
        report = deep_retired_identifier_hits(Path.cwd())
        if report:
            print('Retired inspector identifiers are still present in this checkout or its local build output:')
            print('\n'.join(report))
            print('Delete the containing folders (for example any obj/ or bin/ directories that MSBuild will not rebuild), or update the checkout so every listed file matches the current commit.')
            raise SystemExit(1)
        print('PASS: this checkout and all of its local build output contain no retired inspector identifiers. If Visual Studio still reports these names, the failing project lives in a different folder — run this scan there, or close Visual Studio, delete %LOCALAPPDATA%\\Temp\\XamlCompiler, and rebuild.')
        return
    project = ROOT/'Vidi Studio Windows'
    errors = missing_inspector_references(
        (project/'MainWindow.xaml').read_text(encoding='utf-8'),
        {path.name: path.read_text(encoding='utf-8') for path in project.glob('MainWindow*.cs')},
    )
    if errors:
        raise SystemExit('\n'.join(errors))
    print('PASS: production MainWindow inspector references match current XAML.')


if __name__ == '__main__':
    main(sys.argv[1:])

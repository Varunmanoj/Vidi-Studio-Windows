#!/usr/bin/env python3
"""Reject MainWindow inspector field references missing from current XAML.

Only production partial classes are checked. WinUI-generated bin/obj output is
not source; use build-windows.ps1 -Rebuild to regenerate stale connector files.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def missing_inspector_references(xaml, sources):
    name_key = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
    names = {element.get(name_key) for element in ET.fromstring(xaml).iter()}
    missing = []
    for path, source in sources.items():
        for number, line in enumerate(source.splitlines(), 1):
            for name in set(re.findall(r'\bInspector[A-Z]\w*\b', line)) - names:
                missing.append(f'{path}:{number}: {name} is referenced in C# but not declared in MainWindow.xaml')
    return sorted(missing)


def main():
    project = ROOT/'Vidi Studio Windows'
    errors = missing_inspector_references(
        (project/'MainWindow.xaml').read_text(encoding='utf-8'),
        {path.name: path.read_text(encoding='utf-8') for path in project.glob('MainWindow*.cs')},
    )
    if errors:
        raise SystemExit('\n'.join(errors))
    print('PASS: production MainWindow inspector references match current XAML.')


if __name__ == '__main__':
    main()

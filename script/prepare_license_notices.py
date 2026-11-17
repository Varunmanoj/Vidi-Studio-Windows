"""Include readable component notices with the Windows engine, as on Mac."""
from pathlib import Path
import shutil
import sys


def prepare(root):
    root = Path(root)
    licenses = root / 'runtime/engine/licenses'
    licenses.mkdir(parents=True, exist_ok=True)
    assets = root / 'Vidi Studio Windows/Assets'
    for source, name in [('FFmpeg-GPLv3.txt', 'FFmpeg-GPLv3.txt'), ('piper-notice.txt', 'Piper-MIT.txt')]:
        shutil.copy2(assets / source, licenses / name)
    # Include original distribution notices, retaining package names and paths.
    packages = root / '.venv-windows/Lib/site-packages'
    for distribution in packages.glob('*.dist-info'):
        for item in distribution.rglob('*'):
            if item.is_file() and item.name.upper().startswith(('LICENSE', 'COPYING', 'NOTICE')):
                target = licenses / 'python' / distribution.name / item.relative_to(distribution)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(item, target)
    lines = [
        'VidiStudio third-party license index', '',
        'FFmpeg and FFprobe: GPL builds; see FFmpeg-GPLv3.txt.',
        'Bundled release and license: Settings > About > FFmpeg.',
        'FFmpeg licensing and source information: https://ffmpeg.org/legal.html', '',
        'Piper for Windows (rhasspy/piper 2023.11.14-2): MIT License.',
        'Full notice: Piper-MIT.txt. Source: https://github.com/rhasspy/piper', '',
        'Python package notices copied from the build environment:',
    ]
    lines.extend(str(item.relative_to(licenses)).replace('\\', '/') for item in sorted((licenses / 'python').rglob('*')) if item.is_file())
    lines.extend(['', '.NET: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT',
                  'Windows App SDK: https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE', ''])
    (licenses / 'THIRD-PARTY-LICENSES.txt').write_text('\n'.join(lines), encoding='utf-8')


if __name__ == '__main__':
    prepare(Path(__file__).resolve().parents[1] if len(sys.argv) == 1 else sys.argv[1])

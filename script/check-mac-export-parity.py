"""Report export-engine drift without copying source or altering either checkout."""
import argparse
import ast
import hashlib
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mac-root', type=Path, required=True)
args = parser.parse_args()
windows_root = Path(__file__).resolve().parents[1]


class PlatformHooks(ast.NodeTransformer):
    def visit_FunctionDef(self, node):
        previous = getattr(self, 'function', None)
        self.function = node.name
        result = self.generic_visit(node)
        self.function = previous
        return result

    def visit_Expr(self, node):
        # Windows uses a cancellation marker because hidden processes cannot
        # receive the Mac SIGTERM. Both paths unwind the same export cleanup.
        if ast.unparse(node).startswith(('youtube_audio.check_cancelled(', 'check_cancelled(')):
            return None
        # Windows checks installed media tools before starting a download worker.
        if getattr(self, 'function', None) == 'export_youtube_audio' and ast.unparse(node) in ("tool('ffmpeg')", "tool('ffprobe')"):
            return None
        return self.generic_visit(node)



def parsed(relative, root):
    tree = PlatformHooks().visit(ast.parse((root/relative).read_text(encoding='utf-8')))
    return tree


windows = parsed('engine/vidistudio_engine.py', windows_root)
mac = parsed('engine/vidistudio_engine.py', args.mac_root)
def functions(tree):
    return {node.name: ast.dump(node, include_attributes=False) for node in tree.body if isinstance(node, ast.FunctionDef)}

wf, mf = functions(windows), functions(mac)
start = next(node.lineno for node in mac.body if isinstance(node, ast.FunctionDef) and node.name == 'commit_export')
end = next(node.lineno for node in mac.body if isinstance(node, ast.FunctionDef) and node.name == 'main')
names = {'run', 'run_export'} | {node.name for node in mac.body if isinstance(node, ast.FunctionDef) and start <= node.lineno < end}
drift = [name for name in sorted(names) if wf.get(name) != mf[name]]
for name in ('VIDEO_EXPORT_FORMATS', 'AUDIO_EXPORT_FORMATS', 'EXPORT_DUCKING_LEVEL'):
    def assignment(tree):
        return next((ast.dump(node, include_attributes=False) for node in tree.body if isinstance(node, ast.Assign)
                     and any(isinstance(target, ast.Name) and target.id == name for target in node.targets)), None)
    if assignment(windows) != assignment(mac): drift.append(name)
if ast.dump(parsed('engine/language_results.py', windows_root), include_attributes=False) != ast.dump(parsed('engine/language_results.py', args.mac_root), include_attributes=False):
    drift.append('language_results.py')
if ast.dump(parsed('engine/multitrack_exports.py', windows_root), include_attributes=False) != ast.dump(parsed('engine/multitrack_exports.py', args.mac_root), include_attributes=False):
    drift.append('multitrack_exports.py')
wt = parsed('engine/youtube_audio.py', windows_root)
mt = parsed('engine/youtube_audio.py', args.mac_root)
# Compare the new metadata-only lookup directly, including its bounded cleanup.
# The pre-existing downloader snapshots below still protect platform-specific recovery.
metadata_helpers = {'read_metadata', 'video_duration'}
for name in metadata_helpers:
    if functions(wt).get(name) != functions(mt).get(name): drift.append('youtube_audio.' + name)
if ast.dump(parsed('engine/audio_sources.py', windows_root), include_attributes=False) != ast.dump(parsed('engine/audio_sources.py', args.mac_root), include_attributes=False):
    drift.append('audio_sources.py')
def legacy_downloader_nodes(tree, mac=False):
    return [node for node in tree.body
            if not (isinstance(node, ast.FunctionDef) and node.name in metadata_helpers)
            and not (isinstance(node, ast.Import) and all(alias.name in ({'math', 'json'} if mac else {'math'}) for alias in node.names))
            and not (isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == 'METADATA_TIMEOUT' for target in node.targets))]
wt.body = legacy_downloader_nodes(wt)
mt.body = legacy_downloader_nodes(mt, mac=True)
wt.body = [node for node in wt.body if not isinstance(node, ast.FunctionDef) or node.name != 'check_cancelled']
wd, md = ast.dump(wt, include_attributes=False), ast.dump(mt, include_attributes=False)
# Reviewed 1.0.212: both downloaders now report network_activity transitions
# (Windows 091b15d / Mac c9ea1ec). Windows retains its classified worker errors,
# retry deadline, and cancellation markers. Keep exact snapshots of both paths.
# Python 3.13+ omits empty AST fields by default; include them explicitly so the
# fingerprints agree with Python 3.12, which always includes those fields.
def downloader_fingerprint(tree):
    try:
        dump = ast.dump(tree, include_attributes=False, show_empty=True)
    except TypeError:  # Python 3.12
        dump = ast.dump(tree, include_attributes=False)
    return hashlib.sha256(dump.encode()).hexdigest()

reviewed_downloaders = (
    '969d75aa4986a0766d13e6483d7a91b1f4458403b4a2f09c1a2c760910034ae9',
    'f2424ce3939d44d41cd1cffe80ab1e1b8b9ff68144473666143d1e7061aa6930',
)
if wd != md and (downloader_fingerprint(wt), downloader_fingerprint(mt)) != reviewed_downloaders:
    drift.append('youtube_audio.py')
if drift:
    raise SystemExit('Export parity needs review: ' + ', '.join(drift))
print(f'PASS: {len(names)} shared export/progress functions, media formats, and ducking match Mac; downloader matches or has the exact reviewed 1.0.212 connectivity, Windows worker recovery, and cancellation hooks.')

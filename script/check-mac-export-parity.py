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

    def visit_If(self, node):
        # Mac calls its native AVFoundation finalizer after the engine exits.
        # Windows tags MOV tracks before the engine atomically commits them.
        if ast.unparse(node.test) == "video_processing and output.suffix == '.mov'":
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
wt = parsed('engine/youtube_audio.py', windows_root)
mt = parsed('engine/youtube_audio.py', args.mac_root)
wt.body = [node for node in wt.body if not isinstance(node, ast.FunctionDef) or node.name != 'check_cancelled']
wd, md = ast.dump(wt, include_attributes=False), ast.dump(mt, include_attributes=False)
# Reviewed 1.0.176: both platforms use three attempts with 2/4-second backoff.
# Windows retains classified worker errors and cancellation markers. Both exact ASTs must
# match these snapshots; future changes to either downloader still flag drift.
reviewed_downloaders = (
    'c182d6a91605d44811f90f680d0bb6679927e3ccbdac340f427413bd3cbc0a5a',
    'b767c7c690a24f9f13b0b615b6a3b3c76d35584f12cdfcaa89cd6c81de367c97',
)
if wd != md and (hashlib.sha256(wd.encode()).hexdigest(), hashlib.sha256(md.encode()).hexdigest()) != reviewed_downloaders:
    drift.append('youtube_audio.py')
if drift:
    raise SystemExit('Export parity needs review: ' + ', '.join(drift))
print(f'PASS: {len(names)} shared export/progress functions, media formats, and ducking match Mac; downloader matches or has the exact reviewed Windows 1.0.176 worker recovery and cancellation hooks.')

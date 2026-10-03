"""Report export-engine drift without copying source or altering either checkout."""
import argparse
import ast
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mac-root', type=Path, required=True)
args = parser.parse_args()
windows_root = Path(__file__).resolve().parents[1]


class PlatformHooks(ast.NodeTransformer):
    def visit_Expr(self, node):
        # Windows uses a cancellation marker because hidden processes cannot
        # receive the Mac SIGTERM. Both paths unwind the same export cleanup.
        if ast.unparse(node).startswith(('youtube_audio.check_cancelled(', 'check_cancelled(')):
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
if ast.dump(wt, include_attributes=False) != ast.dump(mt, include_attributes=False): drift.append('youtube_audio.py')
if drift:
    raise SystemExit('Export parity needs review: ' + ', '.join(drift))
print(f'PASS: {len(names)} shared export/progress functions, media formats, ducking, and YouTube downloader match Mac (documented Windows hooks excluded).')

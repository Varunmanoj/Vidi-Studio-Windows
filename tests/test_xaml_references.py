import importlib.util
import shutil
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('xaml_references', ROOT/'script/check_xaml_references.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class XamlReferences(unittest.TestCase):
    def setUp(self):
        project = ROOT/'Vidi Studio Windows'
        self.xaml = (project/'MainWindow.xaml').read_text(encoding='utf-8')
        self.sources = {path.name: path.read_text(encoding='utf-8') for path in project.glob('MainWindow*.cs')}
        self.tmp = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_production_inspector_references_match_xaml(self):
        self.assertEqual(checker.missing_inspector_references(self.xaml, self.sources), [])

    def test_removed_button_reference_is_rejected(self):
        for name in ('InspectorRegenerateAllNarrationButton', 'InspectorNarrateSelectedButton'):
            with self.subTest(name=name):
                sources = dict(self.sources, **{'MainWindow.xaml.cs': f'{name}.IsEnabled = true;'})
                errors = checker.missing_inspector_references(self.xaml, sources)
                self.assertEqual(len(errors), 1)
                self.assertIn(name, errors[0])

    def test_existing_inspector_control_remains_valid(self):
        self.assertEqual(checker.missing_inspector_references(self.xaml, {
            'MainWindow.xaml.cs': 'InspectorSynthesizeNarrationButton.IsEnabled = true;',
        }), [])

    def test_build_supports_clean_rebuild_and_reference_preflight(self):
        source = (ROOT/'script/build-windows.ps1').read_text()
        self.assertIn('[switch]$Rebuild', source)
        self.assertIn("if ($Rebuild) { '/t:Rebuild' } else { '/t:Build' }", source)
        self.assertIn('/restore $target', source)
        self.assertIn('check_xaml_references.py', source)
        self.assertIn('Temp\\XamlCompiler', source)
        self.assertIn('Remove-Item $cache -Recurse -Force', source)

    def test_deep_scan_finds_retired_names_in_any_output(self):
        for path in ('bin/ARM64/Debug/MainWindow.g.cs', 'obj/Vidi Studio Windows.csproj.nuget.cache'):
            with self.subTest(path=path):
                project = self.tmp/'Vidi Studio Windows'
                project.mkdir(parents=True, exist_ok=True)
                (project/'MainWindow.xaml').write_text(self.xaml, encoding='utf-8')
                artifact = project/path
                artifact.parent.mkdir(parents=True, exist_ok=True)
                artifact.write_text('InspectorNarrateSelectedButton.IsEnabled = true;', encoding='utf-8')
                hits = checker.deep_retired_identifier_hits(self.tmp)
                self.assertEqual(len(hits), 1)
                self.assertIn('InspectorNarrateSelectedButton', hits[0])
                shutil.rmtree(project)

    def test_deep_scan_is_clean_on_current_checkout(self):
        project = ROOT/'Vidi Studio Windows'
        for name in checker.RETIRED_INSPECTOR_NAMES:
            for path in project.rglob('*'):
                if path.is_file() and name.encode('utf-8') in path.read_bytes():
                    self.fail(f'{name} found in {path}')


if __name__ == '__main__':
    unittest.main()

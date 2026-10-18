import importlib.util
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


if __name__ == '__main__':
    unittest.main()

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
XAML = ROOT / 'Vidi Studio Windows' / 'MainWindow.xaml'
XAML_NS = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'


class InspectorFooter(unittest.TestCase):
    def setUp(self):
        self.tree = ET.parse(XAML)
        self.names = {element.get(XAML_NS): element for element in self.tree.iter() if element.get(XAML_NS)}

    def test_settings_gear_is_in_the_inspector_footer(self):
        gear = self.names['InspectorSettingsButton']
        self.assertEqual(gear.get('Content'), '\uE713')
        self.assertEqual(gear.get('Click'), 'Settings_Click')
        self.assertEqual(gear.get('AutomationProperties.Name'), 'Settings')
        self.assertEqual(gear.get('Grid.Column'), '3')

    def test_header_no_longer_duplicates_project_controls(self):
        self.assertNotIn('TopOpenFolderButton', self.names)
        self.assertNotIn('TopInspectorToggle', self.names)

    def test_inspector_footer_keeps_folder_and_configuration_actions(self):
        clicks = {element.get('Click') for element in self.tree.iter()}
        self.assertIn('Reveal_Click', clicks)
        self.assertIn('OpenConfiguration_Click', clicks)


if __name__ == '__main__':
    unittest.main()

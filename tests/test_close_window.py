from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
MENUS = (ROOT / 'Vidi Studio Windows' / 'MainWindow.Menus.cs').read_text(encoding='utf-8')


class CloseWindowCommand(unittest.TestCase):
    def test_close_window_is_deferred_until_the_menu_handler_returns(self):
        self.assertIn('case "close-window": CloseWindowDeferred(); return;', MENUS)
        self.assertIn('DispatcherQueue.TryEnqueue(() =>', MENUS)

    def test_close_window_has_a_destroy_fallback(self):
        self.assertIn('try { Close(); }', MENUS)
        self.assertIn('AppWindow.Destroy();', MENUS)


if __name__ == '__main__':
    unittest.main()

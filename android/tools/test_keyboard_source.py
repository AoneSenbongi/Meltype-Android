# SPDX-License-Identifier: GPL-3.0-or-later
from pathlib import Path
import unittest
class Tests(unittest.TestCase):
    def test_insets_not_double_added(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        self.assertNotIn('padding + bottom', source)
        self.assertIn('Math.Max(padding, bottom)', source)
    def test_cursor_selection_before_key_fallback(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        method = source.split('private void MoveCursor(Keycode code)')[1].split('private void Enter()')[0]
        self.assertLess(method.index('SetSelection'), method.index('SendKeyEvent'))
if __name__ == '__main__': unittest.main()

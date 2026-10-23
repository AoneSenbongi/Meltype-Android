# SPDX-License-Identifier: GPL-3.0-or-later
from pathlib import Path
import unittest

class Tests(unittest.TestCase):
    def test_flick_is_connected_only_to_visible_symbol_hints(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        self.assertIn('hint == null ? 0 : Dp(24)', source)
        self.assertIn('_symbolPress?.Move(', source)
        self.assertIn('MotionEventActions.Up', source)

    def test_character_menu_contains_only_five_modes(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        menu = source.split('private void ShowCharacterModes()')[1].split('private void ShowExtraSymbols()')[0]
        for title in ['ひらがな', 'カタカナ', '半角カタカナ', '全角英数字', '半角英数字']:
            self.assertIn(title, menu)
        for removed in ['取消', '日本語に変換', '選択文節まで確定', 'すべて確定']:
            self.assertNotIn(removed, menu)
        self.assertIn('longPressEnabled: false', source)

    def test_punctuation_shows_both_choices(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        self.assertIn('hint: "."', source)
        self.assertIn('_punctuationHint.Text', source)
        self.assertIn('_punctuationAlternate', source)

if __name__ == '__main__': unittest.main()

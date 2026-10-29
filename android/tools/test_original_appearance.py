# SPDX-License-Identifier: GPL-3.0-or-later
from pathlib import Path
import re
import unittest


class Tests(unittest.TestCase):
    def test_keyboard_border_is_one_physical_pixel_at_any_density(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/MobileStyle.cs').read_text(encoding='utf-8')
        keyboard_key = source.split('internal static void KeyboardKey(')[1]
        width = re.search(r'shape\.SetStroke\(([^,]+),', keyboard_key)
        self.assertIsNotNone(width)
        self.assertEqual('1', width.group(1).strip(),
                         '1.2.0 uses a physical pixel; 1dp creates heavy borders on high-density screens')


if __name__ == '__main__':
    unittest.main()

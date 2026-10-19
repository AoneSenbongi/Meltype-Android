# SPDX-License-Identifier: GPL-3.0-or-later
"""Check the same clipboard toggle and clearance above Android's IME controls."""
import runpy
import time
import unittest
import io
from PIL import Image
from pathlib import Path

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def find(root, description):
    return next(n for n in root.iter('node') if n.get('package') == package
                and n.get('content-desc') == description)

def tap(bounds):
    l, t, r, b = bounds
    adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
    time.sleep(.3)

def check_clearance(root):
    controls = [n for n in root.iter('node')
                if n.get('resource-id', '').endswith(('/input_method_nav_ime_switcher', '/input_method_nav_back', '/ime_switcher'))]
    assert controls, 'Android IME navigation controls were not visible'
    top = min(edges(n)[1] for n in controls)
    buttons = [n for n in root.iter('node') if n.get('package') == package
               and n.get('class') == 'android.widget.Button' and n.get('clickable') == 'true']
    assert max(edges(n)[3] for n in buttons) <= top, 'Keyboard overlaps Android IME navigation controls'

class Tests(unittest.TestCase):
    def test_bracket_key_colors(self):
        root = tree()
        tap(edges(find(root, '数字・記号と英字配列を切り替える')))
        try:
            root = tree()
            image = Image.open(io.BytesIO(adb('exec-out', 'screencap', '-p'))).convert('RGB')
            def background(description):
                l, t, r, b = edges(find(root, description))
                return image.getpixel((l+8, t+8))
            reference = background('@')
            self.assertEqual(reference, (255, 255, 255))
            self.assertEqual(background('括弧 ()の種類を選ぶ'), reference)
            self.assertEqual(background('括弧 「」の種類を選ぶ'), reference)
            self.assertNotEqual(background('削除、長押しで連続削除'), reference)
        finally:
            tap(edges(find(root, '数字・記号と英字配列を切り替える')))

    def test_navigation_clearance(self):
        check_clearance(tree())

    def test_same_clipboard_toggle(self):
        root = tree()
        toggle = edges(find(root, 'クリップボードを開く'))
        tap(toggle)
        root = tree()
        close = find(root, 'クリップボードを閉じる')
        try:
            self.assertEqual(edges(close), toggle, 'Clipboard closes with a different button')
            self.assertEqual(close.get('text'), '×')
            self.assertIsNotNone(find(root, 'クリップボード履歴を全消去'))
            self.assertFalse(any(n.get('text') == '戻る' and n.get('class') == 'android.widget.Button'
                                 and n.get('package') == package for n in root.iter('node')))
            check_clearance(root)
        finally:
            tap(edges(close))
        root = tree()
        self.assertEqual(edges(find(root, 'クリップボードを開く')), toggle)
        self.assertIsNotNone(find(root, 'q'))
        check_clearance(root)

if __name__ == '__main__':
    unittest.main()

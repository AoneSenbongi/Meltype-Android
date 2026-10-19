"""Verify English-only mode resets after hiding in the same editor."""
import runpy
import time
from pathlib import Path

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def tap(description):
    node = next(n for n in tree().iter('node') if n.get('package') == package
                and n.get('content-desc') == description)
    l, t, r, b = edges(node)
    adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
    time.sleep(.4)

tap('英語専用モードに切り替える')
assert any(n.get('text') == '英語' and n.get('content-desc') == '日英自動判別に戻る'
           for n in tree().iter('node'))
tap('キーボードを閉じる')
root = tree()
editor = next(n for n in root.iter('node') if n.get('resource-id') == package + ':id/test_editor')
l, t, r, b = edges(editor)
adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
time.sleep(1)
assert any(n.get('text') == '自動' and n.get('content-desc') == '英語専用モードに切り替える'
           for n in tree().iter('node')), 'English mode persisted after hiding'
tap('w'); tap('a')
assert any(n.get('content-desc') == '候補 0' for n in tree().iter('node')), 'Automatic conversion did not resume'
print('PASS: English -> hide -> same editor reopen -> automatic mode and conversion')

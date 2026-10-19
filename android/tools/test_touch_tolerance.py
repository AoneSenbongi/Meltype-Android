# SPDX-License-Identifier: GPL-3.0-or-later
"""Synthetic touch drift regression in the learning app trial editor."""
import runpy, time
from pathlib import Path

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def find(description):
    return next(n for n in tree().iter('node')
                if n.get('package') == package and n.get('content-desc') == description)

def value():
    text = next(n.get('text', '') for n in tree().iter('node')
                if n.get('resource-id') == package + ':id/test_editor')
    return '' if text == 'kyouhagoogledekensaku' else text

def tap(description):
    l, t, r, b = edges(find(description))
    adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
    time.sleep(.25)

tap('英語専用モードに切り替える')
adb('shell', 'input', 'keycombination', '113', '29')
adb('shell', 'input', 'keyevent', '67')
assert value() == ''
l, t, r, b = edges(find('q'))
y = (t+b)//2
adb('shell', 'input', 'swipe', str(r-2), str(y), str(r+2), str(y), '150')
time.sleep(.3)
assert value() == 'q', 'Small horizontal drift cancelled letter'
adb('shell', 'input', 'swipe', str((l+r)//2), str(b-2), str((l+r)//2), str(b+2), '150')
time.sleep(.3)
assert value() == 'qq', 'Small vertical drift cancelled letter'
adb('shell', 'input', 'swipe', str(r-2), str(y), str(r+100), str(y), '200')
time.sleep(.3)
assert value() == 'qq', 'Large movement was not cancelled'
tap('削除、長押しで連続削除')
assert value() == 'q'
print('PASS: horizontal/vertical edge drift, large movement cancellation, delete')

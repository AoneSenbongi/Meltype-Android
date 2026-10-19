# SPDX-License-Identifier: GPL-3.0-or-later
"""Verify paired bracket hints and long presses with synthetic editor text."""
import runpy
import time
from pathlib import Path

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def find(description):
    return next(n for n in tree().iter('node') if n.get('package') == package
                and n.get('content-desc') == description)

def tap(description):
    l, t, r, b = edges(find(description))
    adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
    time.sleep(.25)

def hold(description):
    l, t, r, b = edges(find(description))
    x, y = (l+r)//2, (t+b)//2
    adb('shell', 'input', 'swipe', str(x), str(y), str(x), str(y), '600')
    time.sleep(.3)

def value():
    return next(n.get('text') for n in tree().iter('node')
                if n.get('resource-id') == package + ':id/test_editor')

root = tree()
for letter, hint in [('k', '()'), ('l', '/'), ('-', '「」'), ('z', '_'), ('x', "'")]:
    cell = next(n for n in root.iter('node') if any(
        child.get('content-desc') == letter for child in list(n)))
    assert any(child.get('text') == hint for child in list(cell)), (letter, hint)

tap('英語専用モードに切り替える')
hold('k')
assert find('候補 0').get('text') == '()', 'K must open the paired bracket candidates'
tap('確定・改行')
tap('a')
assert value() == '(a)', 'Paired parentheses did not place the cursor inside'
hold('l')
assert value() == '(a/)', 'L did not insert a slash'
hold('-')
assert find('候補 0').get('text') == '「」', 'Rightmost key must open quotation pairs'
tap('確定・改行')
tap('b')
assert value() == '(a/「b」)', 'Quotation pair did not place the cursor inside'
hold('z'); hold('x')
assert value() == "(a/「b_'」)", 'Z/X long press must insert underscore and apostrophe'
tap('数字・記号と英字配列を切り替える')
tap('_'); tap("'")
assert value() == "(a/「b_'_'」)", 'Symbol keys must match Z/X long press'
tap('数字・記号と英字配列を切り替える')
# English punctuation stays available on the shared bottom key.
punctuation = '句読点、文末はピリオド、長押しでピリオド'
tap(punctuation)
assert value() == "(a/「b_'_',」)", 'Shared comma key disappeared'
l, t, r, b = edges(find(punctuation))
x, y = (l+r)//2, (t+b)//2
adb('shell', 'input', 'swipe', str(x), str(y), str(x), str(y), '700')
time.sleep(.3)
assert value() == "(a/「b_'_',.」)", 'Shared punctuation long press lost period or inserted an extra comma'
print('PASS: matching hints, K paired parentheses, L slash, rightmost quotation pair, cursor inside')
print('PASS: Z/X and symbol keys insert underscore/apostrophe; shared punctuation retains comma/period')

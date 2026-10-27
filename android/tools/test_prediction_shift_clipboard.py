# SPDX-License-Identifier: GPL-3.0-or-later
"""On-device checks for predictive touch, locked Shift and clipboard suggestions.

Run test_clause_edit.py first to build/install the conversion fixture.
"""
import argparse
import sys
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT / 'android-sdk/platform-tools/adb.exe'
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--output', type=Path)
parser.add_argument('--clipboard-only', action='store_true')
parser.add_argument('--prediction-only', action='store_true')
options = parser.parse_args()
SERIAL = options.serial
PACKAGE = 'jp.aonesenbongi.meltype.conversionfixture'
OUT = options.output or ROOT / 'distribution/prediction-shift-clipboard-local'
OUT.mkdir(parents=True, exist_ok=True)


def adb(*args):
    return subprocess.check_output([str(ADB), '-s', SERIAL, *map(str, args)], stderr=subprocess.STDOUT)


def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/prediction-shift-clipboard.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/prediction-shift-clipboard.xml'))


def find(description, nodes=None):
    return next(n for n in (nodes if nodes is not None else tree()).iter('node')
                if n.get('content-desc') == description)


def bounds(node):
    return list(map(int, re.findall(r'\d+', node.get('bounds'))))


def tap(description):
    x1, y1, x2, y2 = bounds(find(description))
    adb('shell', 'input', 'tap', (x1+x2)//2, (y1+y2)//2)
    time.sleep(.2)


def start(personal=False, clipboard=None, sensitive=False, password=False, selection=1):
    adb('shell', 'am', 'force-stop', PACKAGE)
    command = ['shell', 'am', 'start', '-W', '-n', PACKAGE+'/.ConversionActivity',
               '--ez', 'personal', str(personal).lower(), '--ez', 'sensitive', str(sensitive).lower(),
               '--ez', 'password', str(password).lower(), '--ei', 'select', str(selection)]
    if clipboard is not None:
        command += ['--es', 'clipboard', clipboard]
    adb(*command)
    time.sleep(.7)
    tap('fixture-editor')


def type_word(word):
    nodes = tree()
    positions = {c: bounds(find(c, nodes)) for c in set(word)}
    for c in word:
        x1, y1, x2, y2 = positions[c]
        adb('shell', 'input', 'tap', (x1+x2)//2, (y1+y2)//2)
        time.sleep(.12)
    time.sleep(.3)


def editor():
    return find('fixture-editor').get('text', '')


results = {}
if not options.clipboard_only:
    start(True)
    type_word('arigatou')
    baseline = editor()
    for personal in (True, False):
        start(personal)
        type_word('arigatiu')
        literal = editor()
        start(personal)
        type_word('arigat')
        x1, y1, x2, y2 = bounds(find('i'))
        adb('shell', 'input', 'tap', int(x2-(x2-x1)*.25), (y1+y2)//2)
        time.sleep(.2)
        type_word('u')
        results['border_'+str(personal)] = editor()
        assert editor() == literal, results
    print('PASS: boundary touch keeps original key before contextual review', flush=True)

    start()
    type_word('warewarehautyuujinda')
    tap('fixture-select-first')
    original = find('候補 0').get('text')
    x1, y1, x2, y2 = bounds(find('Shift、2回押すと大文字固定'))
    x, y = (x1+x2)//2, (y1+y2)//2
    adb('shell', f'input tap {x} {y}; input tap {x} {y}')
    assert find('大文字固定を解除').get('text') == '⇪', 'Shift failed to lock'
    tap('左へ移動')
    shrunk = find('候補 0').get('text')
    assert original != shrunk, (original, shrunk)
    tap('右へ移動')
    assert find('候補 0').get('text') == original
    assert find('大文字固定を解除').get('text') == '⇪'
    assert find('fixture-composing').get('text') != '-1,-1'
    tap('大文字固定を解除')
    results['shift'] = {'before': original, 'shrunk': shrunk, 'restored': original}
    OUT.joinpath('shift.png').write_bytes(adb('exec-out', 'screencap', '-p'))
    print('PASS: locked Shift resizes selected clause in both directions without confirmation', flush=True)

if options.prediction_only:
    OUT.joinpath('verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    sys.exit(0)

copy_text = 'clipboard-test-20261019'
start(clipboard=copy_text)
tap('fixture-copy')
tap('fixture-editor')
assert find('コピーしたテキストを貼り付け').get('text') == copy_text
assert find('q') is not None  # The history panel was never opened.
OUT.joinpath('clipboard.png').write_bytes(adb('exec-out', 'screencap', '-p'))
tap('コピーしたテキストを貼り付け')
assert editor() == copy_text, editor()
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
tap('キーボードを閉じる')
tap('fixture-editor')
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
tap('クリップボードを開く')
assert any(n.get('content-desc') == 'クリップボード項目' and n.get('text') == copy_text for n in tree().iter('node'))
tap('クリップボードを閉じる')
tap('fixture-copy')
tap('fixture-editor')
assert find('コピーしたテキストを貼り付け').get('text') == copy_text
tap('キーボードを閉じる')
tap('fixture-editor')
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
tap('fixture-copy')
tap('fixture-editor')
assert find('コピーしたテキストを貼り付け').get('text') == copy_text
type_word('nihongo')
nodes = tree()
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in nodes.iter('node'))
assert find('候補 0', nodes) is not None
tap('確定・改行')
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
tap('キーボードを閉じる')
tap('fixture-editor')
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
adb('shell', 'am', 'force-stop', 'jp.aonesenbongi.meltype')
adb('shell', 'ime', 'set', 'jp.aonesenbongi.meltype/crc64cc9ac1f257f62241.KeyboardService')
start()
assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
results['clipboard_paste'] = copy_text
results['once_per_copy_reopen_commit_history_recopy_restart'] = True

for sensitive, password in ((True, False), (False, True)):
    start(clipboard='private-test', sensitive=sensitive, password=password)
    tap('fixture-copy')
    tap('fixture-editor')
    assert not any(n.get('content-desc') == 'コピーしたテキストを貼り付け' for n in tree().iter('node'))
results['sensitive_and_password_hidden'] = True
OUT.joinpath('verification.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS: reopen refresh, direct clipboard paste, conversion priority and private-field/sensitive protection', flush=True)

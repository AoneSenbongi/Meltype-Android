# SPDX-License-Identifier: GPL-3.0-or-later
"""Touch-only symbol flick regression on an emulator with the conversion fixture."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if not args.serial.startswith('emulator-'):
    raise SystemExit('Use an emulator with the conversion fixture installed')
args.output.mkdir(parents=True, exist_ok=True)
adb_path = Path(__file__).resolve().parents[3] / 'android-sdk/platform-tools/adb.exe'
package = 'jp.aonesenbongi.meltype'
fixture = package + '.conversionfixture'

def adb(*command):
    return subprocess.check_output([str(adb_path), '-s', args.serial, *map(str, command)])

def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/symbol-flick.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/symbol-flick.xml'))

def find(description, root=None):
    for _ in range(6 if root is None else 1):
        node = next((n for n in (root if root is not None else tree()).iter('node')
                     if n.get('content-desc') == description), None)
        if node is not None: return node
        time.sleep(.5)
    raise AssertionError('Missing UI element: '+description)

def bounds(node):
    return list(map(int, re.findall(r'\d+', node.get('bounds'))))

def tap(description):
    l,t,r,b = bounds(find(description))
    adb('shell', 'input', 'tap', (l+r)//2, (t+b)//2)
    time.sleep(.15)

def swipe(description, dx=0, dy=-40, duration=120, near_top=False):
    l,t,r,b = bounds(find(description))
    density = int(re.search(rb'(?:Physical|Override) density: (\d+)', adb('shell', 'wm', 'density')).group(1)) / 160
    x,y = (l+r)//2, t+int(3*density) if near_top else (t+b)//2
    adb('shell', 'input', 'swipe', x,y,x+int(dx*density),y+int(dy*density),duration)
    time.sleep(.15)

def value():
    return find('fixture-editor').get('text', '')

def start(english=True):
    adb('shell', 'am', 'force-stop', fixture)
    adb('shell', 'am', 'start', '-W', '-n', fixture+'/.ConversionActivity', '--ez', 'personal', 'false')
    time.sleep(.5)
    tap('fixture-editor')
    if english:
        tap('英語専用モードに切り替える')

old_ime = adb('shell', 'settings', 'get', 'secure', 'default_input_method').decode().strip()
results = {}
try:
    ime = package+'/crc64cc9ac1f257f62241.KeyboardService'
    adb('shell', 'ime', 'enable', ime)
    adb('shell', 'ime', 'set', ime)
    start()
    expected = ''
    for key, symbol in [('q','1'), ('a','@'), ('n','!'), ('m','?')]:
        swipe(key)
        expected += symbol
        assert value() == expected, (key, value(), expected)
    results['symbol_flick'] = expected
    swipe('q', near_top=True)
    expected += '1'
    assert value() == expected, value()
    swipe('a', dx=80, dy=0)
    assert value() == expected, value()
    tap('q'); expected += 'q'
    swipe('q',dy=0,duration=600); expected += '1'
    assert value() == expected, value()
    results['tap_hold_top_edge_side_cancel'] = True
    punctuation = '句読点、右上の文字は上フリック・長押しで入力'
    root = tree()
    assert find(punctuation, root).get('text') == ','
    assert any(n.get('text') == '.' for n in root.iter('node'))
    swipe(punctuation); expected += '.'
    tap(punctuation); expected += ','
    assert value() == expected, value()
    results['english_punctuation'] = True
    start(False)
    root = tree()
    comma = find(punctuation, root).get('text')
    p = find(punctuation, root)
    hint = next(n.get('text') for parent in root.iter('node') if p in list(parent)
                for n in parent if n is not p)
    swipe(punctuation)
    assert value() == hint, (value(), hint)
    results['japanese_punctuation'] = {'tap': comma, 'flick': hint}
    start(False)
    for c in 'desu': tap(c)
    root = tree()
    p = find(punctuation, root)
    hint = next(n.get('text') for parent in root.iter('node') if p in list(parent)
                for n in parent if n is not p)
    assert p.get('text') != comma, (p.attrib, comma)
    before = value()
    swipe(punctuation)
    assert value() == before+hint, (before, hint, value())
    results['predicted_period_alternate_comma'] = True
    start(False)
    swipe('k')
    assert find('候補 0').get('text') in ('（）','()'), 'Bracket flick did not open candidates'
    tap('確定・改行')
    assert value() in ('（）','()'), value()
    results['bracket_flick'] = True
    args.output.joinpath('keyboard.png').write_bytes(adb('exec-out','screencap','-p'))
    args.output.joinpath('result.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS:', json.dumps(results,ensure_ascii=False), flush=True)
finally:
    if old_ime and old_ime != 'null': adb('shell', 'ime', 'set', old_ime)
    adb('shell', 'am', 'force-stop', fixture)

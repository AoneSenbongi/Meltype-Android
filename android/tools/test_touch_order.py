# SPDX-License-Identifier: GPL-3.0-or-later
"""Requires TouchInjection.dex in /data/local/tmp on the selected emulator."""
import os, runpy, time
from pathlib import Path

if not os.environ.get('MELTYPE_TEST_SERIAL', '').startswith('emulator-'):
    raise SystemExit('Select an emulator with MELTYPE_TEST_SERIAL for synthetic multi-touch')

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def center(description):
    node = next(n for n in tree().iter('node')
                if n.get('package') == package and n.get('content-desc') == description)
    l,t,r,b = edges(node)
    return (l+r)//2, (t+b)//2

def value():
    text = next(n.get('text', '') for n in tree().iter('node')
                if n.get('resource-id') == package + ':id/test_editor')
    return '' if text == 'kyouhagoogledekensaku' else text

def overlap(first, second):
    coords = (*center(first), *center(second))
    adb('shell', 'CLASSPATH=/data/local/tmp/TouchInjection.dex',
        'app_process', '/system/bin', 'TouchInjection', *map(str, coords))
    time.sleep(.3)

x,y = center('英語専用モードに切り替える')
adb('shell','input','tap',str(x),str(y))
adb('shell','input','keycombination','113','29')
adb('shell','input','keyevent','67')
assert value() == ''
overlap('a', 'b')
assert value() == 'ab', 'Releasing second key first reversed text: ' + value()
overlap('c', '削除、長押しで連続削除')
assert value() == 'ab', 'Delete overtook pending letter: ' + value()
overlap('Shift、2回押すと大文字固定', 'd')
assert value() == 'abD', 'Overlapping Shift lost uppercase letter: ' + value()
print('PASS: injected multi-touch order, pending letter before delete, Shift overlap')

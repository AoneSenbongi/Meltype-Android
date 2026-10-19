# SPDX-License-Identifier: GPL-3.0-or-later
"""Verify punctuation long hold and delete repeat after touch coordination."""
import runpy, time
from pathlib import Path
ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def center(description):
    node = next(n for n in tree().iter('node')
                if n.get('package') == package and n.get('content-desc') == description)
    l,t,r,b = edges(node)
    return (l+r)//2, (t+b)//2

def value():
    return next(n.get('text', '') for n in tree().iter('node')
                if n.get('resource-id') == package + ':id/test_editor')

x,y = center('英語専用モードに切り替える')
adb('shell','input','tap',str(x),str(y))
x,y = center('句読点、文末はピリオド、長押しでピリオド')
adb('shell','input','swipe',str(x),str(y),str(x),str(y),'600')
time.sleep(.3)
assert value() == '.', 'Punctuation hold also inserted comma'
adb('shell','input','text','abcde')
assert value() == '.abcde'
x,y = center('削除、長押しで連続削除')
adb('shell','input','swipe',str(x),str(y),str(x),str(y),'1100')
time.sleep(.3)
assert value() in ('', 'kyouhagoogledekensaku'), 'Delete hold did not repeat'
adb('shell','input','text','x')
time.sleep(.5)
assert value() == 'x', 'Delete repeat survived release'
print('PASS: punctuation hold without comma, delete repeat and release cancellation')

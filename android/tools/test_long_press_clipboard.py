# SPDX-License-Identifier: GPL-3.0-or-later
"""Run later against the learning app's trial editor; uses only synthetic text."""
import runpy,time,os
from pathlib import Path
ns=runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb,tree,edges,package=(ns[k] for k in ('adb','tree','edges','PACKAGE'))
def find(description):
    return next(n for n in tree().iter('node') if n.get('package')==package and n.get('content-desc')==description)
def tap(description):
    l,t,r,b=edges(find(description));adb('shell','input','tap',str((l+r)//2),str((t+b)//2));time.sleep(.25)
def value():
    return next(n.get('text') for n in tree().iter('node') if n.get('resource-id')==package+':id/test_editor')
tap('英語専用モードに切り替える')
l,t,r,b=edges(find('q'));x,y=(l+r)//2,(t+b)//2
adb('shell','input','swipe',str(x),str(y),str(x),str(y),'600');time.sleep(.3)
assert value()=='1','Long press also inserted q'
tap('a');tap('b');tap('c')
assert value()=='1abc'
tap('左へ移動');tap('x');assert value()=='1abxc'
tap('数字・記号と英字配列を切り替える')
left=edges(find('!'));right=edges(find('?'));delete=edges(find('削除、長押しで連続削除'))
assert left[0]<right[0]<delete[0] and left[1]==right[1]==delete[1], 'Punctuation keys not grouped at right'
tap('数字・記号と英字配列を切り替える')
for name in ('n','m'):
    l,t,r,b=edges(find(name));x,y=(l+r)//2,(t+b)//2
    adb('shell','input','swipe',str(x),str(y),str(x),str(y),'600');time.sleep(.3)
assert value()=='1abx!?c', 'Right-side long presses inserted wrong symbols'
tap('クリップボードを開く')
assert find('クリップボードを閉じる') is not None
assert find('クリップボード履歴を全消去') is not None
tap('クリップボードを閉じる');assert find('q') is not None
print('PASS: long press without letter, cursor insertion, clipboard panel opening and closing')

if os.environ.get('MELTYPE_TEST_SERIAL','').startswith('emulator-'):
    sample=value()
    adb('shell','input','keycombination','113','29');adb('shell','input','keycombination','113','31')
    tap('クリップボードを開く')
    assert find('クリップボード項目').get('text')==sample
    adb('shell','input','keycombination','113','29');adb('shell','input','keyevent','67')
    assert value() in ('','kyouhagoogledekensaku'), 'Trial editor was not cleared'
    tap('クリップボード項目');assert value()==sample, 'Copied item did not paste'
    print('PASS: emulator-only copy, selection from clipboard, paste into cleared trial editor')

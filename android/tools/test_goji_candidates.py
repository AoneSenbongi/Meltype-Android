# SPDX-License-Identifier: GPL-3.0-or-later
"""Whole-reading homophones after live input and manual clause resize."""
from pathlib import Path
helper=Path(__file__).with_name('test_prediction_shift_clipboard.py')
exec(compile(helper.read_text(encoding='utf-8').split('results = {}')[0],str(helper),'exec'))

def candidates():
    return [n.get('text') for n in tree().iter('node') if n.get('content-desc','').startswith('候補 ')]
def choose(text):
    n=next(n for n in tree().iter('node') if n.get('text')==text and n.get('content-desc','').startswith('候補 '))
    a,b,c,d=bounds(n);adb('shell','input','tap',(a+c)//2,(b+d)//2);time.sleep(.25)
def lock_shift():
    a,b,c,d=bounds(find('Shift、2回押すと大文字固定'));x,y=(a+c)//2,(b+d)//2
    adb('shell',f'input tap {x} {y}; input tap {x} {y}')
    assert find('大文字固定を解除') is not None
results={}
for convert in [False,True]:
    start(True);type_word('goji')
    if convert:tap('空白・変換')
    words=candidates();assert '誤字' in words and '護持' in words,words
    choose('誤字');assert editor()=='誤字',editor()
    results['space' if convert else 'live']=words
    print('PASS: goji live/Space homophones and selection',flush=True)
for raw,word,arrows in [('goji','誤字',['左へ移動','右へ移動']),
                        ('nidai','荷台',['右へ移動','右へ移動']),
                        ('camera','カメラ',['左へ移動','右へ移動'])]:
    start(True, selection=0)
    type_word(raw)
    if raw=='camera':assert editor()=='camera',editor()
    tap('fixture-select-first')
    original=editor();lock_shift()
    for arrow in arrows:tap(arrow)
    words=candidates();assert word in words,(raw,words)
    assert find('大文字固定を解除') is not None
    assert find('fixture-composing').get('text')!='-1,-1'
    OUT.joinpath(raw+'-resized.png').write_bytes(adb('exec-out','screencap','-p'))
    choose(word);assert editor()==word,(raw,editor())
    results[raw]={'before':original,'candidates':words,'committed':editor()}
    print('PASS: locked Shift resize for '+raw+'; candidates, composing range and commit',flush=True)
OUT.joinpath('verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')

# SPDX-License-Identifier: GPL-3.0-or-later
"""Settings-to-keyboard feature checks on the selected real Android device."""
import hashlib, sys
from pathlib import Path
helper = Path(__file__).with_name('test_prediction_shift_clipboard.py')
exec(compile(helper.read_text(encoding='utf-8').split('results = {}')[0],str(helper),'exec'))
IME='jp.aonesenbongi.meltype'
results={}
def tap_text(text):
    node=next(n for n in tree().iter('node') if n.get('text')==text)
    a,b,c,d=bounds(node);adb('shell','input','tap',(a+c)//2,(b+d)//2);time.sleep(.25)
def hold(description):
    a,b,c,d=bounds(find(description));x,y=(a+c)//2,(b+d)//2
    adb('shell','input','swipe',x,y,x,y,650);time.sleep(.3)
def menu(text):
    node=next(n for n in tree().iter('node') if n.get('content-desc') in ('英語専用モードに切り替える','未確定文字をカタカナにする','未確定文字を自動判別に戻す') or n.get('text') in ('日英','カナ','自動','英語'))
    a,b,c,d=bounds(node);x,y=(a+c)//2,(b+d)//2
    adb('shell','input','swipe',x,y,x,y,650);time.sleep(.3);tap_text(text)
def candidate(text):
    node=next(n for n in tree().iter('node') if n.get('text')==text and (n.get('content-desc','').startswith('候補 ') or n.get('content-desc','').startswith('予測候補 ')))
    a,b,c,d=bounds(node);adb('shell','input','tap',(a+c)//2,(b+d)//2);time.sleep(.3)
def read_prefs():return json.loads(adb('shell','run-as',IME,'cat','files/input-preferences.json'))
def write_prefs(**changes):
    settings=read_prefs();settings.update(changes)
    p=OUT/'fixture-preferences.json';p.write_text(json.dumps(settings),encoding='utf-8')
    adb('push',str(p),'/data/local/tmp/meltype-feature-prefs.json')
    adb('shell','am','force-stop',IME)
    adb('shell','run-as',IME,'cp','/data/local/tmp/meltype-feature-prefs.json','files/input-preferences.json')
    adb('shell','rm','/data/local/tmp/meltype-feature-prefs.json')
    adb('shell','ime','set',IME+'/crc64cc9ac1f257f62241.KeyboardService')
def start_main():
    adb('shell','am','start','-W','-n',IME+'/jp.aonesenbongi.meltype.MainActivity');time.sleep(.6)
def scroll():
    adb('shell','input','swipe',540,2050,540,1250,350);time.sleep(.25)
# Verify actual settings widgets, callback persistence and labels by scrolling the screen.
start_main();seen=set();toggled=set()
for _ in range(20):
    nodes=tree()
    for n in nodes.iter('node'):
        description=n.get('content-desc','')
        if description.startswith('設定 '):
            key=description[3:];seen.add(key)
            if key in ('CalculatorCandidates','ConsonantAbbreviations','Predictions','EnglishNamePredictions') and key not in toggled:
                if n.get('checked')!='true':
                    a,b,c,d=bounds(n);adb('shell','input','tap',(a+c)//2,(b+d)//2);time.sleep(.25)
                assert read_prefs()[key] is True,key
                toggled.add(key)
    if len(seen)==22:break
    scroll()
assert len(seen)==22 and len(toggled)==4,(seen,toggled)
OUT.joinpath('settings.png').write_bytes(adb('exec-out','screencap','-p'))
adb('shell','am','force-stop',IME);start_main()
assert all(read_prefs()[k] for k in toggled)
results['settings_widgets']=sorted(seen)
print('PASS: 22 setting widgets; four UI toggles persisted after restart',flush=True)
write_prefs(CalculatorCandidates=True,ConsonantAbbreviations=True,Predictions=True,EnglishNamePredictions=True,ShowTypedKeys=True,
    ClipboardCandidates=True,FullWidthParentheses=True,CursorIntoBrackets=True,SquareBrackets=True,FullWidthSlash=True,LaughterAfterKana=True,
    EscStepsBack=True,EscRevertsSelectedClause=True,FunctionKeysAffectSelectedClause=True,AlphanumericKeyStartsAsTyped=True,
    EnglishFirstInput=False,FullWidthSpaceByDefault=False,ShiftSpaceHalfWidth=False)
start(True);tap('数字・記号と英字配列を切り替える');type_word('1+2')
hold('数字・記号と英字配列を切り替える');tap_text('*');type_word('3');tap('空白・変換')
assert any(n.get('text')=='7' and n.get('content-desc','').startswith('候補 ') for n in tree().iter('node'))
OUT.joinpath('calculator.png').write_bytes(adb('exec-out','screencap','-p'));candidate('7');assert editor()=='7'
start(True);type_word('ast');tap('空白・変換');candidate('明日');assert editor()=='明日'
results['calculator_and_abbreviation']=True
print('PASS: touch-only multiplication menu, calculator result and ast abbreviation',flush=True)
start(True);type_word('shaomi');candidate('Xiaomi');assert editor()=='Xiaomi'
start(True);type_word('nihongo');assert find('入力したローマ字').get('text')=='nihongo'
menu('半角カタカナ');assert 'ﾆﾎﾝｺﾞ' in editor(),editor()
menu('カタカナ');assert 'ニホンゴ' in editor(),editor()
menu('ひらがな');assert 'にほんご' in editor(),editor()
menu('全角英数字');assert 'ｎｉｈｏｎｇｏ' in editor(),editor()
menu('半角英数字');assert 'nihongo' in editor(),editor()
menu('取消・一段戻す')
start(True);type_word('nihongo');menu('取消・一段戻す');assert editor()=='にほんご',editor()
menu('取消・一段戻す');assert editor()=='nihongo',editor()
menu('取消・一段戻す');assert editor()=='',editor()
results['prediction_and_function_menu']=True
print('PASS: English-name prediction, typed-key display and F6/F8/F9/F10 touch commands',flush=True)
start(True);tap('数字・記号と英字配列を切り替える');tap('括弧 ()の種類を選ぶ');tap('確定・改行')
assert editor()=='（）',editor();assert find('fixture-selection').get('text')=='1,1'
start(True);type_word('nihongo');tap('数字・記号と英字配列を切り替える');tap('/')
assert '／' in editor(),editor()
start(True);hold('数字・記号と英字配列を切り替える');tap_text('[');assert editor()=='［',editor()
start(True);type_word('new');assert editor()=='ねw',editor()
results['brackets_slash_laughter']=True
print('PASS: full-width pair and inside cursor, slash, square bracket and kana+w',flush=True)
start(True,clipboard='feature-clipboard');tap('fixture-copy');tap('fixture-editor');type_word('pe-suto');tap('空白・変換')
candidate('feature-clipboard');assert editor()=='feature-clipboard',editor()
start(False,clipboard='private-clipboard');tap('fixture-copy');tap('fixture-editor');type_word('pe-suto');tap('空白・変換')
assert not any(n.get('text')=='private-clipboard' and n.get('content-desc','').startswith('候補 ') for n in tree().iter('node'))
results['clipboard_conversion_and_protection']=True
print('PASS: clipboard reading conversion and no-personalized-learning protection',flush=True)
write_prefs(FullWidthSpaceByDefault=True)
start(True);tap('空白・変換');assert editor()=='　',repr(editor())
tap('Shift、2回押すと大文字固定');tap('空白・変換');assert editor()=='　 ',repr(editor())
write_prefs(FullWidthSpaceByDefault=False,EnglishFirstInput=True)
start(True);type_word('nihongo');assert editor()=='nihongo',editor();menu('日本語に変換');assert editor()!='nihongo',editor()
results['spaces_and_english_first']=True
print('PASS: full-width/Shift half-width spaces and English-first to Japanese command',flush=True)
OUT.joinpath('features-verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')

"""Exercise the requested keyboard actions in the app's trial field."""
import runpy
import time
from pathlib import Path
ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))
cache = {}
def find(description):
    cache.clear()
    cache.update({n.get('content-desc'): n for n in tree().iter('node') if n.get('package') == package and n.get('content-desc')})
    return cache[description]
def tap(description):
    l,t,r,b = edges(cache.get(description) if description in cache else find(description)); adb('shell','input','tap',str((l+r)//2),str((t+b)//2)); time.sleep(.15)
    if description in ('数字・記号と英字配列を切り替える','キーボードを閉じる'): cache.clear()
def text():
    return next(n.get('text') for n in tree().iter('node') if n.get('resource-id') == package + ':id/test_editor')
def fresh():
    cache.clear()
    runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
assert find('英語専用モードに切り替える').get('text') == '自動'
tap('英語専用モードに切り替える')
assert find('日英自動判別に戻る').get('text') == '英語'
tap('Shift、2回押すと大文字固定'); tap('Shift、2回押すと大文字固定')
tap('a'); tap('b')
assert text() == 'AB', 'Caps Lock did not persist'
tap('大文字固定を解除'); tap('c')
assert text() == 'ABc', 'Caps Lock did not release'
expected = 'ABc'
for pair in ('()', '「」', '{}', '[]', '【】', '『』'):
    tap('数字・記号と英字配列を切り替える')
    group = '「」' if pair[0] in '「【『' else '()'
    tap('括弧 ' + group + 'の種類を選ぶ')
    index = ('「」','『』','【】').index(pair) if group == '「」' else ('()','{}','[]').index(pair)
    assert find('候補 ' + str(index)).get('text') == pair, 'Bracket candidate missing'
    if pair == '{}':
        tap('空白・変換'); tap('確定・改行')
    else: tap('候補 ' + str(index))
    tap('数字・記号と英字配列を切り替える'); tap('x')
    expected += pair[0] + 'x'
assert text() == expected + '』】]}」)', 'Grouped bracket cursors did not move inside all six pairs'

tap('数字・記号と英字配列を切り替える'); tap('キーボードを閉じる')
root = tree(); editor = next(n for n in root.iter('node') if n.get('resource-id') == package + ':id/test_editor')
l,t,r,b = edges(editor); adb('shell','input','tap',str((l+r)//2),str((t+b)//2)); time.sleep(.5)
find('q')
fresh()
for c in 'nihongo': tap(c)
tap('空白・変換')
assert find('未確定文字をカタカナにする').get('text') == 'カナ'
tap('未確定文字をカタカナにする')
assert text() == 'ニホンゴ', 'Katakana action committed or lost conversion'
assert find('未確定文字をひらがなにする').get('text') == 'かな'; tap('未確定文字をひらがなにする')
assert text() == 'にほんご', 'Hiragana action failed'
print('PASS: Caps Lock, symbol layout reset, all six bracket cursors, kana conversion and key labels')

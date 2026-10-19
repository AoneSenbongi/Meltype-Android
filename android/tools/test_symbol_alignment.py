# SPDX-License-Identifier: GPL-3.0-or-later
"""Compare every symbol key's touch bounds with the matching romaji key."""
import runpy
import time
from pathlib import Path

ns = runpy.run_path(str(Path(__file__).with_name('test_setup_editor.py')))
adb, tree, edges, package = (ns[k] for k in ('adb', 'tree', 'edges', 'PACKAGE'))

def keys():
    return {n.get('content-desc'): edges(n) for n in tree().iter('node')
            if n.get('package') == package and n.get('class') == 'android.widget.Button'
            and n.get('content-desc')}

romaji = keys()
toggle = '数字・記号と英字配列を切り替える'
l, t, r, b = romaji[toggle]
adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
time.sleep(.3)
try:
    symbols = keys()
    rows = [(list('qwertyuiop'), list('1234567890')),
            (list('asdfghjkl-'), ['@', '#', '¥', '%', '&', '-', '+', '括弧 ()の種類を選ぶ', '/', '括弧 「」の種類を選ぶ']),
            (['Shift、2回押すと大文字固定', *list('zxcvbnm'), '削除、長押しで連続削除'],
             ['英字配列に戻る', '_', "'", ':', ';', '"', '!', '?', '削除、長押しで連続削除'])]
    for letters, marks in rows:
        for letter, mark in zip(letters, marks):
            assert symbols[mark] == romaji[letter], f'{mark} differs from {letter}: {symbols[mark]} vs {romaji[letter]}'
    for control in (toggle, '英語専用モードに切り替える', '空白・変換', '左へ移動', '右へ移動', '確定・改行'):
        assert symbols[control] == romaji[control], f'Control moved: {control}'
finally:
    adb('shell', 'input', 'tap', str((l+r)//2), str((t+b)//2))
print('PASS: symbol and romaji keys share the same size and position in all rows')

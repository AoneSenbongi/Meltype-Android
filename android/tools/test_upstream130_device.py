# SPDX-License-Identifier: GPL-3.0-or-later
"""Additional real-device input regressions using the conversion fixture."""
import sys
from pathlib import Path
# Reuse only the fixture helpers; do not execute the other test's cases.
helper = Path(__file__).with_name('test_prediction_shift_clipboard.py')
exec(compile(helper.read_text(encoding='utf-8').split('results = {}')[0], str(helper), 'exec'))
results = {}
start(True)
type_word('chinamoni')
assert editor() == 'ちなみに', editor()
tap('削除、長押しで連続削除')
assert editor() != 'ちなみに', editor()
type_word('ni')
assert editor() == 'ちなみに', editor()
tap('確定・改行')
assert editor() == 'ちなみに', editor()
results['personal_delete_retype_commit'] = True
print('PASS: personal correction, original-key deletion/retyping and Enter', flush=True)
start()
type_word('open')
assert editor() == 'open', editor()
tap('確定・改行')
assert editor() == 'open', editor()
results['english_visible_commit'] = True
start()
type_word('nihongo')
assert editor() == '日本語', editor()
tap('確定・改行')
assert editor() == '日本語', editor()
results['japanese_visible_commit'] = True
start()
type_word('desu')
tap('句読点、文末はピリオド、長押しでピリオド')
assert editor() in ('です．', 'です。', 'です.'), editor()
visible = editor()
tap('確定・改行')
assert editor() == visible
results['punctuation'] = visible
print('PASS: Japanese/English visible confirmation and sentence punctuation', flush=True)
start()
type_word('zh')
assert editor() == '←', editor()
tap('確定・改行')
assert editor() == '←', editor()
results['upstream_zh_arrow'] = True
start()
tap('数字・記号と英字配列を切り替える')
for c in '2026': tap(c)
tap('空白・変換')
nodes = tree()
candidates = [n.get('text') for n in nodes.iter('node') if n.get('content-desc', '').startswith('候補 ')]
assert any(c in candidates for c in ('二〇二六', '二千二十六', '令和8年')), candidates
results['upstream_number_candidates'] = candidates
OUT.joinpath('number-candidates.png').write_bytes(adb('exec-out', 'screencap', '-p'))
OUT.joinpath('upstream-smoke.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS: upstream zh arrow and numeric year candidates', flush=True)

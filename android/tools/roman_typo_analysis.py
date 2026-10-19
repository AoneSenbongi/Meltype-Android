# SPDX-License-Identifier: GPL-3.0-or-later
"""Local reading normalization and conservative review/model extraction."""
import collections
import datetime
import difflib
import json
from pathlib import Path
import re
import sqlite3
import sys
import unicodedata

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tools/reading-runtime'))
from sudachipy import dictionary, tokenizer

def hiragana(text):
    return ''.join(chr(ord(c)-96) if 'ァ' <= c <= 'ヶ' else c for c in text)

class Romanizer:
    def __init__(self):
        self.tokenizer = dictionary.Dictionary().tokenizer()
        source = (ROOT / 'source/src/Meltype.Core/Detection/RomajiDetector.cs').read_text(encoding='utf-8')
        self.spellings = {}
        for kana, roman in re.findall(r'\("([^"]+)", \["([a-z]+)"', source):
            self.spellings.setdefault(kana, roman)
        self.spellings['ん'] = 'nn'
        self.spellings['ー'] = '-'

    def kana(self, text):
        text = hiragana(unicodedata.normalize('NFKC', text))
        result = ''
        i = 0
        while i < len(text):
            if text[i] == 'っ' and i+1 < len(text):
                following = next((self.spellings[text[i+1:i+1+n]] for n in (2,1) if text[i+1:i+1+n] in self.spellings), '')
                if following and following[0] not in 'aiueon':
                    result += following[0]; i += 1; continue
            match = next((text[i:i+n] for n in (2,1) if text[i:i+n] in self.spellings), None)
            if match:
                result += self.spellings[match]; i += len(match)
            else:
                result += text[i].lower(); i += 1
        return result

    def __call__(self, text):
        text = unicodedata.normalize('NFKC', text)
        pieces = []
        unknown = False
        for token in self.tokenizer.tokenize(text, tokenizer.Tokenizer.SplitMode.A):
            surface = token.surface()
            # Kana and literal Latin fragments preserve spelling; only kanji need a reading.
            if re.search(r'[\u3400-\u9fff々]', surface):
                reading = token.reading_form()
                if token.is_oov() or not reading or re.search(r'[\u3400-\u9fff々]', reading):
                    unknown = True
                pieces.append(reading or surface)
            else:
                pieces.append(surface)
        return self.kana(''.join(pieces)), not unknown

def minimal_change(before, after):
    start = 0
    while start < min(len(before), len(after)) and before[start] == after[start]: start += 1
    suffix = 0
    while suffix < min(len(before)-start, len(after)-start) and before[-suffix-1] == after[-suffix-1]: suffix += 1
    return before[start:len(before)-suffix if suffix else len(before)], after[start:len(after)-suffix if suffix else len(after)], start, suffix

def classify(before, after, before_roman, after_roman, reliable=True):
    if not reliable: return 'ambiguous', 'unknown_reading'
    if before_roman == after_roman: return 'not_typo', 'same_reading'
    # Literal continuation contains no replacement or removal of existing letters.
    if after.startswith(before) and len(after) - len(before) >= 4: return 'not_typo', 'literal_continuation'
    def content(text):
        return ''.join(c for c in unicodedata.normalize('NFKC', text) if not c.isspace() and not unicodedata.category(c).startswith(('P','S')))
    if content(before) == content(after): return 'not_typo', 'punctuation_or_spacing'
    wrong, right, start, suffix = minimal_change(before_roman, after_roman)
    if start + suffix >= 4 and re.fullmatch('[a-z]*', wrong+right) and (
        max(len(wrong),len(right)) <= 1 or len(wrong)==len(right)==2 and wrong==right[::-1]):
        return 'likely_typo', 'small_roman_edit'
    return 'ambiguous', 'rewrite_or_uncertain_edit'

def analyze(rows, romanizer):
    result = []
    for row in rows:
        if row['kind'] != 'edit': continue
        before, b_ok = romanizer(row['before']); after, a_ok = romanizer(row['after'])
        category, reason = classify(row['before'], row['after'], before, after, b_ok and a_ok)
        wrong, right, start, suffix = minimal_change(before, after)
        result.append(dict(row, roman_before=before, roman_after=after, reliable=b_ok and a_ok,
                           category=category, reason=reason, wrong=wrong, right=right, start=start, suffix=suffix))
    return result

def extract_model(rows, min_support=3):
    groups = collections.defaultdict(list)
    negatives = collections.Counter()
    for row in rows:
        if row['category'] != 'likely_typo': continue
        key = (row['wrong'], row['right'])
        if row['label'] == 'typo': groups[key].append(row)
        elif row['label'] == 'not_typo': negatives[key] += 1
    rules = []
    for (wrong,right), examples in sorted(groups.items()):
        # Count independent pairs, not repeated copies of a single captured edit.
        support = len({row['pair'] for row in examples})
        if support < min_support or negatives[(wrong,right)]: continue
        contexts = []
        for row in examples:
            if 'roman_before' not in row: continue
            at = row['start']; end = at + len(wrong); text = row['roman_before']
            context = dict(Left=text[max(0,at-3):at], Following=text[end:end+3],
                           AtStart=at < 3, AtEnd=end+3 > len(text))
            if context not in contexts: contexts.append(context)
        rules.append(dict(Wrong=wrong, Right=right, Support=support, Contexts=contexts))
    protected = sorted({row['roman_before'] for row in rows if row['label'] == 'not_typo' and row.get('reliable') and re.fullmatch('[a-z]{4,64}', row['roman_before'])})
    return dict(Version=1, MinSupport=max(3,min_support), Rules=rules, Protected=protected)

def load_rows(db):
    db.row_factory = sqlite3.Row
    return [dict(r) for r in db.execute("""SELECT e.pair,e.before,e.after,e.kind,COUNT(*) AS count,
        COALESCE(l.label,'pending') AS label FROM edits e LEFT JOIN labels l ON e.pair=l.pair GROUP BY e.pair""")]

def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', type=Path, default=ROOT/'training-data')
    parser.add_argument('--apply-obvious-non-typos', action='store_true')
    args = parser.parse_args()
    folder = args.data / ('roman-analysis-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
    folder.mkdir()
    with sqlite3.connect(args.data/'review.sqlite3') as db:
        assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
        with sqlite3.connect(folder/'before.sqlite3') as backup: db.backup(backup)
        rows = analyze(load_rows(db), Romanizer())
        applied = []
        if args.apply_obvious_non_typos:
            db.execute('BEGIN IMMEDIATE')
            for row in rows:
                if row['label'] != 'pending' or row['category'] != 'not_typo': continue
                current = db.execute('SELECT label FROM labels WHERE pair=?',(row['pair'],)).fetchone()
                if current and current[0] != 'pending': continue
                db.execute("INSERT INTO labels(pair,label) VALUES(?,'not_typo') ON CONFLICT(pair) DO UPDATE SET label='not_typo' WHERE labels.label='pending'", (row['pair'],))
                applied.append(dict(pair=row['pair'], previous=current[0] if current else None, reason=row['reason']))
            db.commit()
        # Re-read labels changed by the user during analysis; never train from a stale label snapshot.
        labels = {r['pair']:r['label'] for r in load_rows(db)}
        for row in rows: row['label'] = labels[row['pair']]
        assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    model = extract_model(rows)
    (folder/'analysis.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
    (folder/'label-changes.json').write_text(json.dumps(applied,ensure_ascii=False,indent=2),encoding='utf-8')
    (folder/'personal-romaji-typos.json').write_text(json.dumps(model,ensure_ascii=False,indent=2),encoding='utf-8')
    summary = dict(folder=str(folder), labels=dict(collections.Counter(r['label'] for r in rows)),
        categories=dict(collections.Counter(r['category'] for r in rows)), auto_not_typo=len(applied),
        model=model, pending_likely_typo=sum(r['label']=='pending' and r['category']=='likely_typo' for r in rows))
    (folder/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False))

if __name__ == '__main__': main()

# SPDX-License-Identifier: GPL-3.0-or-later
"""On-device checks; run test_clause_edit.py first to install the editor fixture."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--skip-prediction', action='store_true')
parser.add_argument('--output', type=Path)
args = parser.parse_args()
serial = args.serial
ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT/'android-sdk/platform-tools/adb.exe'
PACKAGE = 'jp.aonesenbongi.meltype.conversionfixture'
OUT = args.output or ROOT/'distribution/adaptive-input-local'
OUT.mkdir(parents=True, exist_ok=True)

def adb(*parts):
    return subprocess.check_output([str(ADB),'-s',serial,*map(str,parts)],stderr=subprocess.STDOUT)

def tree():
    adb('shell','uiautomator','dump','--windows','/sdcard/adaptive-input.xml')
    return ET.fromstring(adb('shell','cat','/sdcard/adaptive-input.xml'))

def find(description, nodes=None):
    return next(n for n in (tree() if nodes is None else nodes).iter('node') if n.get('content-desc')==description)

def bounds(n): return list(map(int,re.findall(r'\d+',n.get('bounds'))))

def tap(description):
    x1,y1,x2,y2=bounds(find(description))
    adb('shell','input','tap',(x1+x2)//2,(y1+y2)//2)
    time.sleep(.2)

def start(personal):
    adb('shell','am','force-stop',PACKAGE)
    adb('shell','am','start','-W','-n',PACKAGE+'/.ConversionActivity','--ez','personal',str(personal).lower())
    time.sleep(.7)
    tap('fixture-editor')

def type_word(word):
    nodes=tree()
    positions={c:bounds(find(c,nodes)) for c in set(word)}
    for c in word:
        x1,y1,x2,y2=positions[c]
        adb('shell','input','tap',(x1+x2)//2,(y1+y2)//2)
        time.sleep(.12)
    time.sleep(.4)

def text(): return find('fixture-editor').get('text','')

results={}
if not args.skip_prediction:
    start(True); type_word('arigatou'); baseline=text()
    results['baseline']=baseline
    for personal in (True,False):
        start(personal); type_word('arigatiu'); literal=text()
        start(personal); type_word('arigat')
        nodes=tree(); x1,y1,x2,y2=bounds(find('i',nodes))
        adb('shell','input','tap',x2-1,(y1+y2)//2)
        time.sleep(.2); type_word('u')
        results['border_'+str(personal)]=text()
        assert text()==literal,results

start(True); type_word('chinamini'); correct=text()
start(True); type_word('chinamoni'); results['typo_live']=text()
assert text()==correct,results
tap('確定・改行'); assert text()==correct,results
start(False); type_word('chinamoni'); results['typo_no_learning']=text()
assert text()!=correct,results

start(False); type_word('koukouniiku'); tap('fixture-select-first')
first_bounds=bounds(find('候補 0'))
x1,y1,x2,y2=first_bounds
screen_width=int(adb('shell','wm','size').decode().split(':')[-1].strip().split('x')[0])
adb('shell','input','swipe',screen_width-25,(y1+y2)//2,25,(y1+y2)//2,450)
time.sleep(.25)
nodes=tree()
visible=[n for n in nodes.iter('node') if n.get('content-desc','').startswith('候補 ')]
assert visible and not any(n.get('content-desc')=='候補 0' for n in visible),'Candidate bar did not scroll'
tap(visible[0].get('content-desc'))
after=bounds(find('候補 0'))
assert after[0]==first_bounds[0],(first_bounds,after)
results['candidate_scroll']={'before':first_bounds,'after':after}
OUT.joinpath('runtime-verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
OUT.joinpath('after-next-clause.png').write_bytes(adb('exec-out','screencap','-p'))
print(json.dumps(results,ensure_ascii=False))
print('PASS: live/Enter typo correction, private-field protection, and next-clause scroll reset')
if not args.skip_prediction: print('PASS: boundary touch keeps original key before contextual review')

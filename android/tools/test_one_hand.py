"""One-hand layout UI regression on the selected device."""
import argparse
from pathlib import Path
import subprocess
import re
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--output', type=Path)
options = parser.parse_args()

ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT / 'android-sdk/platform-tools/adb.exe'
OUT = options.output or ROOT / 'distribution/one-hand-cycle-local'
IME = 'jp.aonesenbongi.meltype.learning'
PACKAGE = 'jp.aonesenbongi.meltype.conversionfixture'

def adb(*args):
    return subprocess.check_output([str(ADB), '-s', options.serial, *args], stderr=subprocess.STDOUT)

def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/one-hand-test.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/one-hand-test.xml'))

def find(description=None, text=None):
    deadline = time.monotonic()+15
    while True:
        matches = [n for n in tree().iter('node')
                   if description is not None and n.get('content-desc') == description
                   or text is not None and n.get('text') == text]
        if matches: return matches[0]
        if time.monotonic() >= deadline: raise AssertionError('Missing UI item: '+str(description or text))

def bounds(node):
    return tuple(map(int, re.findall(r'\d+', node.get('bounds'))))

def tap(description=None, text=None):
    x1,y1,x2,y2 = bounds(find(description,text))
    adb('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2))
    time.sleep(.2)

def editor():
    return find('fixture-editor').get('text','')

def geometry():
    deadline = time.monotonic()+15
    while True:
        nodes = {n.get('content-desc'): n for n in tree().iter('node') if n.get('content-desc')}
        if 'q' in nodes and 'p' in nodes:
            left,top,_,bottom = bounds(nodes['q'])
            _,_,right,_ = bounds(nodes['p'])
            return left,top,right,bottom
        if time.monotonic() >= deadline: raise AssertionError('Keyboard did not appear')

def screenshot(name):
    (OUT/name).write_bytes(adb('exec-out','screencap','-p'))

BUTTON = '片手モードを右寄せ・左寄せ・全幅の順に切り替える'
OUT.mkdir(parents=True, exist_ok=True)
adb('shell','am','force-stop',IME)
adb('shell','run-as',IME,'rm','-f','shared_prefs/keyboard-layout.xml')
adb('shell','ime','set',IME+'/crc64cc9ac1f257f62241.KeyboardService')
adb('shell','am','force-stop',PACKAGE)
adb('shell','am','start','-W','-n',PACKAGE+'/.ConversionActivity')
time.sleep(.6)
tap('fixture-editor')
full = geometry()
tap(BUTTON)
right = geometry()
assert right[2] == full[2] and right[0] > full[0], (full,right)
assert abs((right[2]-right[0])/(full[2]-full[0])-.8) < .015
assert right[1] == full[1] and right[3] == full[3]
assert not any(n.get('text') in ('全幅','⇄') for n in tree().iter('node')), 'Extra side controls retained'
screenshot('right.png')
tap(BUTTON)
left = geometry()
print('Measured full/right/left:', full, right, left, flush=True)
# Weighted layouts may distribute the last physical pixel to different children.
assert left[0] == full[0] and abs((left[2]-left[0])-(right[2]-right[0])) <= 1, (full,right,left)
screenshot('left.png')
tap(BUTTON)
assert geometry() == full, 'Third tap did not restore full width'
print('PASS: right/left/full cycle without popup or side controls', flush=True)
tap(BUTTON)
keys = {n.get('content-desc'): bounds(n) for n in tree().iter('node') if n.get('content-desc')}
for c in 'nihongo':
    x1,y1,x2,y2 = keys[c]
    adb('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2))
    time.sleep(.15)
assert editor() == '日本語'
nodes = list(tree().iter('node'))
assert any(n.get('content-desc','').startswith('候補 ') for n in nodes)
assert not any(n.get('content-desc') == BUTTON for n in nodes), 'One-hand button visible alongside candidates'
assert geometry() == right, 'Candidate display changed alignment'
screenshot('candidates.png')
tap('確定・改行')
assert editor() == '日本語'
find(BUTTON)
tap('キーボードを閉じる')
tap('fixture-editor')
assert geometry() == right, 'Reopen lost alignment'
adb('shell','am','force-stop',IME)
adb('shell','ime','set',IME+'/crc64cc9ac1f257f62241.KeyboardService')
tap('fixture-editor')
deadline = time.monotonic()+15
while True:
    try:
        restarted = geometry()
        break
    except StopIteration:
        if time.monotonic() >= deadline: raise
assert restarted == right, 'Service restart lost saved alignment'
tap('数字・記号と英字配列を切り替える')
assert bounds(find('1'))[0] == right[0]
tap('英字配列に戻る')
tap('クリップボードを開く')
assert bounds(find('クリップボード履歴を全消去'))[0] >= right[0]
tap('クリップボードを閉じる')
assert geometry() == right
print('PASS: single tap right/left/full cycle, no popup/side controls, hidden during candidates, restored after confirmation, saved alignment, symbols and clipboard', flush=True)
print('Geometry:',full,right,left)

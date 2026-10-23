# SPDX-License-Identifier: GPL-3.0-or-later
"""Check navigation overlap/gaps using Android 15 gesture and three-button modes."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if not args.serial.startswith('emulator-'):
    raise SystemExit('Use an emulator with the conversion fixture installed')
args.output.mkdir(parents=True, exist_ok=True)
adb_path = Path(__file__).resolve().parents[3] / 'android-sdk/platform-tools/adb.exe'
package = 'jp.aonesenbongi.meltype'
fixture = package + '.conversionfixture'

def adb(*command):
    return subprocess.check_output([str(adb_path), '-s', args.serial, *map(str, command)])

def tree():
    for _ in range(6):
        result = adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/navigation-insets.xml')
        if b'dumped to:' in result:
            return ET.fromstring(adb('shell', 'cat', '/sdcard/navigation-insets.xml'))
        time.sleep(.5)
    raise AssertionError('Could not capture the accessibility tree')

def find(description):
    for _ in range(6):
        node = next((n for n in tree().iter('node') if n.get('content-desc') == description), None)
        if node is not None: return node
        time.sleep(.5)
    raise AssertionError('Missing '+description)

def bounds(node):
    return list(map(int,re.findall(r'\d+',node.get('bounds'))))

def tap(description):
    l,t,r,b = bounds(find(description))
    adb('shell','input','tap',(l+r)//2,(t+b)//2)
    time.sleep(.2)

def measure():
    root = tree()
    enter = next(n for n in root.iter('node') if n.get('content-desc') == '確定・改行')
    bottom = bounds(enter)[3]
    buttons = [bounds(n) for n in root.iter('node')
               if n.get('content-desc') in ('Back','Home','Recents','Recent apps','Switch input method')
               and bounds(n)[1] > screen_height*.75]
    assert buttons, 'System navigation buttons are missing'
    nav_top = min(b[1] for b in buttons)
    result = {'key_bottom':bottom,'navigation_button_top':nav_top,'gap_px':nav_top-bottom}
    assert 0 <= nav_top-bottom <= density*6, result
    return result

overlays = adb('shell','cmd','overlay','list').decode()
original_overlay = next(line.split()[-1] for line in overlays.splitlines()
                        if line.startswith('[x]') and '.navbar.' in line)
original_ime = adb('shell','settings','get','secure','default_input_method').decode().strip()
density = int(re.search(rb'(?:Physical|Override) density: (\d+)',adb('shell','wm','density')).group(1))/160
screen_height = int(re.search(rb'\d+x(\d+)',adb('shell','wm','size')).group(1))
results = {}
try:
    for mode in ('threebutton','gestural'):
        adb('shell','cmd','overlay','enable-exclusive','--category','com.android.internal.systemui.navbar.'+mode)
        time.sleep(2)
        ime = package+'/crc64cc9ac1f257f62241.KeyboardService'
        adb('shell','ime','enable',ime); adb('shell','ime','set',ime)
        adb('shell','am','force-stop',fixture)
        adb('shell','am','start','-W','-n',fixture+'/.ConversionActivity','--ez','personal','false')
        tap('fixture-editor')
        find('q')
        first = measure()
        args.output.joinpath(mode+'.png').write_bytes(adb('exec-out','screencap','-p'))
        tap('数字・記号と英字配列を切り替える')
        assert measure()['key_bottom'] == first['key_bottom']
        tap('数字・記号と英字配列を切り替える')
        tap('片手モードを右寄せ・左寄せ・全幅の順に切り替える')
        assert measure()['key_bottom'] == first['key_bottom']
        for _ in range(2): tap('片手モードを右寄せ・左寄せ・全幅の順に切り替える')
        tap('キーボードを閉じる'); tap('fixture-editor')
        assert measure() == first, 'Reopening accumulated bottom padding'
        tap('英語専用モードに切り替える')
        l,t,r,b = bounds(find('q'));x,y=(l+r)//2,(t+b)//2
        adb('shell','input','swipe',x,y,x,y-int(40*density),120)
        assert find('fixture-editor').get('text') == '1', 'Symbol flick regressed'
        punctuation = '句読点、右上の文字は上フリック・長押しで入力'
        tap(punctuation)
        assert find('fixture-editor').get('text') == '1,', 'Bottom-row input overlapped navigation'
        results[mode] = first
        args.output.joinpath('result.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
        print('PASS:',mode,json.dumps(first),flush=True)
finally:
    adb('shell','cmd','overlay','enable-exclusive','--category',original_overlay)
    if original_ime and original_ime != 'null': adb('shell','ime','set',original_ime)
    adb('shell','am','force-stop',fixture)

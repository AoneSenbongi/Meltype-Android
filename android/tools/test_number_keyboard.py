"""Numeric keyboard regression; settings mutations are restricted to the emulator."""
from pathlib import Path
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--serial', default='emulator-5554')
parser.add_argument('--numeric-only', action='store_true')
options = parser.parse_args()

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / 'android-sdk/build-tools/36.0.0'
JAVA = ROOT / 'jdk/bin'
SDK = ROOT / 'android-sdk/platforms/android-36/android.jar'
FIXTURE = Path(__file__).with_name('number-fixture')
BUILD = ROOT / 'distribution/panel-number-local/fixture'
PACKAGE = 'jp.aonesenbongi.meltype.numberfixture'
IME = 'jp.aonesenbongi.meltype.learning'
ADB = ROOT / 'android-sdk/platform-tools/adb.exe'
ENV = dict(os.environ, JAVA_HOME=str(ROOT / 'jdk'))

def run(*args):
    return subprocess.check_output([str(a) for a in args], env=ENV, stderr=subprocess.STDOUT)
def adb(*args):
    return run(ADB, '-s', options.serial, *args)
def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/number-fixture.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/number-fixture.xml'))
def node(description):
    return next(n for n in tree().iter('node') if n.get('content-desc') == description)
def tap(description):
    x1,y1,x2,y2 = map(int, re.findall(r'\d+', node(description).get('bounds')))
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(.15)
def text(description):
    return node(description).get('text', '')
def type_word(word):
    keys = {n.get('content-desc'): n.get('bounds') for n in tree().iter('node')}
    for c in word:
        x1,y1,x2,y2 = map(int, re.findall(r'\d+', keys[c]))
        adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
        time.sleep(.15)
def delete_five():
    x1,y1,x2,y2 = map(int, re.findall(r'\d+', node('削除、長押しで連続削除').get('bounds')))
    for _ in range(5):
        adb('shell','input','tap',(x1+x2)//2,(y1+y2)//2)
        time.sleep(.15)

BUILD.mkdir(parents=True, exist_ok=True)
classes = BUILD / 'classes'
classes.mkdir(exist_ok=True)
run(TOOLS/'aapt.exe', 'package', '-f', '-M', FIXTURE/'AndroidManifest.xml', '-I', SDK, '-F', BUILD/'unsigned.apk')
run(JAVA/'javac.exe', '-source', '8', '-target', '8', '-classpath', SDK, '-d', classes, FIXTURE/'NumberActivity.java')
run(TOOLS/'d8.bat', '--lib', SDK, '--output', BUILD, *classes.rglob('*.class'))
import zipfile
with zipfile.ZipFile(BUILD/'unsigned.apk', 'a') as archive:
    archive.write(BUILD/'classes.dex', 'classes.dex')
key = BUILD/'fixture.keystore'
if not key.exists():
    run(JAVA/'keytool.exe', '-genkeypair', '-keystore', key, '-storepass', 'fixture-test', '-keypass', 'fixture-test', '-alias', 'fixture', '-keyalg', 'RSA', '-validity', '30', '-dname', 'CN=Local emulator fixture')
run(TOOLS/'zipalign.exe', '-f', '4', BUILD/'unsigned.apk', BUILD/'fixture.apk')
run(TOOLS/'apksigner.bat', 'sign', '--ks', key, '--ks-pass', 'pass:fixture-test', '--key-pass', 'pass:fixture-test', BUILD/'fixture.apk')
adb('install', '-r', str(BUILD/'fixture.apk'))
adb('shell', 'ime', 'enable', IME+'/crc64cc9ac1f257f62241.KeyboardService')
adb('shell', 'ime', 'set', IME+'/crc64cc9ac1f257f62241.KeyboardService')


def check_layout(input_type, extra, word):
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', PACKAGE+'/.NumberActivity', '--ei', 'type', str(input_type))
    time.sleep(.7)
    tap('number-editor')
    nodes = list(tree().iter('node'))
    keys = {n.get('content-desc'):n for n in nodes if n.get('package') == IME}
    assert all(c in keys for c in '0123456789'), keys.keys()
    assert not any(c in keys for c in ('q','空白・変換','数字・記号と英字配列を切り替える')), keys.keys()
    assert ('クリップボードを開く' in keys) == (input_type != 0x12), keys.keys()
    for c in '.-*#': assert (c in keys) == (c in extra), (input_type, c, keys.keys())
    tap('number-copy'); tap('number-editor')
    if input_type != 0x12:
        assert node('コピーしたテキストを貼り付け').get('text') == '12345'
        tap('コピーしたテキストを貼り付け')
        assert text('number-editor') == '12345'
        delete_five()
        tap('クリップボードを開く'); tap('クリップボード項目')
        assert text('number-editor') == '12345'
        assert node('1') is not None
        delete_five()
    else:
        assert not any(n.get('content-desc') in ('コピーしたテキストを貼り付け','クリップボードを開く') for n in tree().iter('node'))
    type_word(word)
    if input_type != 0x12: assert text('number-editor') == word, (input_type, text('number-editor'))
    if input_type == 2:
        with (BUILD.parent/'number-pad.png').open('wb') as f:
            subprocess.run([str(ADB), '-s', options.serial, 'exec-out', 'screencap', '-p'], stdout=f, check=True)
        tap('削除、長押しで連続削除')
        assert text('number-editor') == word[:-1]
        tap('確定・次へ')
        time.sleep(.3)
        assert any(n.get('content-desc') == 'q' for n in tree().iter('node')), 'Next did not restore text keyboard'
        tap('number-editor')
        assert any(n.get('content-desc') == '1' for n in tree().iter('node')), 'Returning to number did not rebuild'
    tap('text-editor')
    assert any(n.get('content-desc') == 'q' for n in tree().iter('node')), 'Text keyboard did not return'
    print('PASS: numeric editor', input_type, extra, 'direct input and return to text')

for args in [(2,'','120'),(0x1002,'-','-12'),(0x2002,'.','12.5'),(0x3002,'-.','-12.5'),(0x12,'','12'),(3,'*#','12#')]:
    check_layout(*args)

if options.numeric_only:
    raise SystemExit(0)
if options.serial != 'emulator-5554':
    raise SystemExit('Settings mutations are only allowed on the emulator; use --numeric-only.')

# Settings tests are isolated to the emulator.
adb('shell','am','force-stop',IME)
adb('shell','am','start','-W','-n',IME+'/jp.aonesenbongi.meltype.MainActivity')
time.sleep(.7)
def switch(label):
    return next(n for n in tree().iter('node') if n.get('class') == 'android.widget.Switch' and n.get('text') == label)
def tap_node(n):
    x1,y1,x2,y2=map(int,re.findall(r'\d+',n.get('bounds')))
    adb('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2))
    time.sleep(.3)
def by_text(label): return next(n for n in tree().iter('node') if n.get('text') == label)
def pref():
    import json
    return json.loads(adb('shell','run-as',IME,'cat','files/input-preferences.json'))
label='変換の学習'
old=switch(label).get('checked') == 'true'
tap_node(switch(label)); assert pref()['LearningEnabled'] == (not old)
adb('shell','am','force-stop',IME)
adb('shell','am','start','-W','-n',IME+'/jp.aonesenbongi.meltype.MainActivity')
time.sleep(.4)
assert (switch(label).get('checked') == 'true') == (not old)
if switch(label).get('checked') == 'false': tap_node(switch(label))
collection='編集記録の収集（7日間）'
assert switch(collection).get('checked') == 'false'
tap_node(switch(collection)); tap_node(by_text('取消'))
assert switch(collection).get('checked') == 'false'
tap_node(switch(collection)); tap_node(by_text('開始'))
assert switch(collection).get('checked') == 'true'
tap_node(switch(label))
assert switch(collection).get('checked') == 'false' and switch(collection).get('enabled') == 'false'
tap_node(switch(label))
assert switch(collection).get('checked') == 'false'
if not old: tap_node(switch(label))
with (BUILD.parent/'control-panel.png').open('wb') as f:
    subprocess.run([str(ADB),'-s','emulator-5554','exec-out','screencap','-p'],stdout=f,check=True)
print('PASS: learning switch persists; collection cancel/start/stop and no automatic restart')

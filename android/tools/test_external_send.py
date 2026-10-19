"""On-device regression: app clears an unconfirmed message without changing editor."""
import argparse
from pathlib import Path
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
options = parser.parse_args()

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / 'android-sdk/build-tools/36.0.0'
JAVA = ROOT / 'jdk/bin'
SDK = ROOT / 'android-sdk/platforms/android-36/android.jar'
FIXTURE = Path(__file__).with_name('send-fixture')
BUILD = ROOT / 'distribution/send-reset-local/fixture'
PACKAGE = 'jp.aonesenbongi.meltype.sendfixture'
IME = 'jp.aonesenbongi.meltype.learning'
ADB = ROOT / 'android-sdk/platform-tools/adb.exe'
ENV = dict(os.environ, JAVA_HOME=str(ROOT / 'jdk'))

def run(*args):
    return subprocess.check_output([str(a) for a in args], env=ENV, stderr=subprocess.STDOUT)
def adb(*args):
    return run(ADB, '-s', options.serial, *args)
def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/send-fixture.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/send-fixture.xml'))
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
def start():
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', PACKAGE + '/.SendActivity')
    time.sleep(.7)
    tap('fixture-editor')

BUILD.mkdir(parents=True, exist_ok=True)
classes = BUILD / 'classes'
classes.mkdir(exist_ok=True)
run(TOOLS/'aapt.exe', 'package', '-f', '-M', FIXTURE/'AndroidManifest.xml', '-I', SDK, '-F', BUILD/'unsigned.apk')
run(JAVA/'javac.exe', '-source', '8', '-target', '8', '-classpath', SDK, '-d', classes, FIXTURE/'SendActivity.java')
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

for next_action in ('typing', 'confirm'):
    start()
    type_word('nihongo')
    before = text('fixture-editor')
    assert before and any(n.get('content-desc') == '候補 0' for n in tree().iter('node')), 'No pending composition'
    tap('fixture-send')
    assert text('fixture-sent') == before and text('fixture-editor') == '', 'Fixture did not send and clear'
    if next_action == 'typing':
        type_word('a')
        assert text('fixture-editor') == 'あ', 'Old message reappeared after typing: '+text('fixture-editor')
    else:
        tap('確定・改行')
        assert not text('fixture-editor').strip(), 'Old message reappeared after Enter: '+text('fixture-editor')
    print('PASS: unconfirmed send -> '+next_action+'; previous text not reinserted')

start()
type_word('nihongo')
before = text('fixture-editor')
tap('確定・改行')
assert text('fixture-editor') == before, 'Normal confirmation changed text'
type_word('a')
assert text('fixture-editor') == before+'あ', 'Normal confirmation did not preserve text'
print('PASS: normal confirmation preserves preceding message')

start()
type_word('nihongo')
before = text('fixture-editor')
tap('fixture-finish')
type_word('a')
actual = text('fixture-editor')
assert actual == before+'あ', 'App-side confirmation duplicated text: expected '+before+'あ, got '+actual
print('PASS: app-side confirmation retains text without reinserting it')

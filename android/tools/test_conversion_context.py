"""Conversion and Shift+Space regression in a non-learning editor."""
from pathlib import Path
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
options = parser.parse_args()

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / 'android-sdk/build-tools/36.0.0'
JAVA = ROOT / 'jdk/bin'
SDK = ROOT / 'android-sdk/platforms/android-36/android.jar'
FIXTURE = Path(__file__).with_name('conversion-fixture')
BUILD = ROOT / 'distribution/conversion-context-local/fixture'
PACKAGE = 'jp.aonesenbongi.meltype.conversionfixture'
IME = 'jp.aonesenbongi.meltype.learning'
ADB = ROOT / 'android-sdk/platform-tools/adb.exe'
ENV = dict(os.environ, JAVA_HOME=str(ROOT / 'jdk'))

def run(*args):
    return subprocess.check_output([str(a) for a in args], env=ENV, stderr=subprocess.STDOUT)
def adb(*args):
    return run(ADB, '-s', options.serial, *args)
def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/conversion-fixture.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/conversion-fixture.xml'))
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
BUILD.mkdir(parents=True, exist_ok=True)
classes = BUILD / 'classes'
classes.mkdir(exist_ok=True)
run(TOOLS/'aapt.exe', 'package', '-f', '-M', FIXTURE/'AndroidManifest.xml', '-I', SDK, '-F', BUILD/'unsigned.apk')
run(JAVA/'javac.exe', '-source', '8', '-target', '8', '-classpath', SDK, '-d', classes, FIXTURE/'ConversionActivity.java')
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



def start():
    adb('shell','am','force-stop',PACKAGE)
    adb('shell','am','start','-W','-n',PACKAGE+'/.ConversionActivity')
    time.sleep(.3)
    tap('fixture-editor')

def capture(name):
    (BUILD.parent/name).write_bytes(adb('exec-out','screencap','-p'))

for raw in ('open','data','close','camera','code','type','line','user','test','save','file'):
    start(); type_word(raw)
    before=text('fixture-editor')
    assert before == raw, (raw, 'preview', before)
    tap('確定・改行')
    assert text('fixture-editor') == raw, (raw, 'confirmation', text('fixture-editor'))
    if raw=='open': capture('english-open.png')
    print('PASS: visible English -> Enter:', raw)

for raw in ('open','data','kyouhaopen'):
    start(); type_word(raw)
    before=text('fixture-editor')
    assert before.endswith('open') or before=='data', before
    tap('空白・変換')
    assert text('fixture-editor') == before+' ', (before, text('fixture-editor'))
    print('PASS: visible English -> Space:', raw)

for raw in ('', 'open', 'nihongo'):
    start()
    if raw: type_word(raw)
    before=text('fixture-editor')
    tap('Shift、2回押すと大文字固定')
    tap('空白・変換')
    assert text('fixture-editor') == before+'　', (raw, repr(text('fixture-editor')))
    if raw=='nihongo': capture('fullwidth-space.png')
    # A consumed one-shot Shift must type lower-case after Space.
    type_word('open'); tap('確定・改行')
    assert text('fixture-editor') == before+'　open', repr(text('fixture-editor'))
    print('PASS: Shift+Space and one-shot Shift release:', raw or '(empty)')

start(); tap('英語専用モードに切り替える'); type_word('open')
tap('Shift、2回押すと大文字固定'); tap('空白・変換')
assert text('fixture-editor')=='open　', repr(text('fixture-editor'))
print('PASS: Shift+Space in English-only mode')

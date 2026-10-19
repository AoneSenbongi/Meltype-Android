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
BUILD = ROOT / 'distribution/clause-edit-local/fixture'
PACKAGE = 'jp.aonesenbongi.meltype.conversionfixture'
IME = 'jp.aonesenbongi.meltype'
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
    time.sleep(.6)
    tap('fixture-editor')
start()
type_word('warewarehautyuujinda')
print('Initial:', text('fixture-editor'), text('fixture-composing'), flush=True)
tap('fixture-select-first')
nodes=list(tree().iter('node'))
print('First candidates:',[(n.get('text'),n.get('content-desc')) for n in nodes if n.get('content-desc','').startswith('候補 ')],flush=True)
choice=next(n for n in nodes if n.get('text') in ('ワレワレハ','ワレワレは') and n.get('content-desc','').startswith('候補 '))
tap(choice.get('content-desc'))
print('After first:',text('fixture-editor'),text('fixture-composing'),flush=True)
(BUILD.parent/'after-first.png').write_bytes(adb('exec-out','screencap','-p'))
a,b=map(int,text('fixture-composing').split(','))
assert a>=0 and b>a, 'Later clause was confirmed after changing the first clause'
nodes=list(tree().iter('node'))
print('Later candidates:', [(n.get('text'),n.get('content-desc')) for n in nodes if n.get('content-desc','').startswith('候補 ')],flush=True)
choice=next(n for n in nodes if n.get('text')=='ウチュウジンだ' and n.get('content-desc','').startswith('候補 '))
tap(choice.get('content-desc'))
assert text('fixture-editor')=='ワレワレはウチュウジンだ', text('fixture-editor')
assert text('fixture-composing')=='-1,-1',text('fixture-composing')
print('PASS: first candidate leaves later clause composing; both katakana candidates produce ワレワレはウチュウジンだ',flush=True)
(BUILD.parent/'final.png').write_bytes(adb('exec-out','screencap','-p'))

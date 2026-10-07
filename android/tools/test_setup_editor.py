"""Verify the learning app's complete trial editor remains above its keyboard."""
import re
import subprocess
import time
import xml.etree.ElementTree as ET

ADB = 'E:/Prog/Meltype/Android/android-sdk/platform-tools/adb.exe'
PACKAGE = 'jp.aonesenbongi.meltype.learning'
def adb(*args): return subprocess.check_output([ADB, '-d', *args])
def tree():
    adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/meltype-editor-test.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/meltype-editor-test.xml'))
def edges(node): return list(map(int, re.findall(r'\d+', node.get('bounds'))))
adb('shell', 'am', 'force-stop', PACKAGE)
adb('shell', 'am', 'start', '-n', PACKAGE + '/jp.aonesenbongi.meltype.MainActivity')
adb('shell', 'ime', 'set', PACKAGE + '/crc64cc9ac1f257f62241.KeyboardService')
time.sleep(.5)
root = tree()
scroll = next(n for n in root.iter('node') if n.get('package') == PACKAGE and n.get('class') == 'android.widget.ScrollView')
l,t,r,b = edges(scroll); x = (l+r)//2
adb('shell','input','swipe',str(x),str(t+(b-t)*3//4),str(x),str(t+(b-t)*1//4),'350')
root = tree()
editor = next(n for n in root.iter('node') if n.get('resource-id') == PACKAGE + ':id/test_editor')
l,t,r,b = edges(editor)
adb('shell','input','tap',str((l+r)//2),str((t+b)//2))
time.sleep(1)
root = tree()
editor = next((n for n in root.iter('node') if n.get('resource-id') == PACKAGE + ':id/test_editor'), None)
assert editor is not None, 'Trial editor is hidden by keyboard'
q = next(n for n in root.iter('node') if n.get('package') == PACKAGE and n.get('content-desc') == 'q')
hide = next(n for n in root.iter('node') if n.get('package') == PACKAGE and n.get('content-desc') == 'キーボードを閉じる')
_,top,_,bottom = edges(editor); _,qt,_,qb=edges(q)
assert bottom-top >= (qb-qt)*120/54-3, 'Trial editor is clipped: ' + str((top,bottom))
assert bottom <= edges(hide)[1], 'Trial editor overlaps keyboard'
print('PASS: complete trial editor automatically remains above keyboard')

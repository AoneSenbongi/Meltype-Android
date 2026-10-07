"""Check the visible Meltype learning keyboard's full-cell row alignment over USB."""
import argparse
import re
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--adb', default='E:/Prog/Meltype/Android/android-sdk/platform-tools/adb.exe')
args = parser.parse_args()
def adb(*commands):
    return subprocess.check_output([args.adb, '-d', *commands])
adb('shell', 'uiautomator', 'dump', '--windows', '/sdcard/meltype-layout-test.xml')
tree = ET.fromstring(adb('shell', 'cat', '/sdcard/meltype-layout-test.xml'))
keys = {n.get('content-desc'): list(map(int, re.findall(r'\d+', n.get('bounds'))))
        for n in tree.iter('node') if n.get('package') == 'jp.aonesenbongi.meltype.learning'
        and n.get('class') == 'android.widget.Button' and n.get('content-desc')}
for row in [list('qwertyuiop'), list('asdfghjkl-'),
            ['Shift、2回押すと大文字固定', *list('zxcvbnm'), '削除、長押しで連続削除'],
            ['数字・記号と英字配列を切り替える', '英語専用モードに切り替える',
             '句読点、文末はピリオド、長押しでピリオド', '空白・変換', '左へ移動', '右へ移動', '確定・改行']]:
    missing = [key for key in row if key not in keys]
    assert not missing, 'Show the learning keyboard in automatic Japanese mode: ' + str(missing)
    edges = {(keys[key][1], keys[key][3]) for key in row}
    assert len(edges) == 1, 'Key cells are vertically misaligned: ' + str(edges)
print('PASS: all four keyboard rows have aligned, equal-height touch cells')

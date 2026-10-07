"""Exercise the real keyboard in the Android emulator and capture its screens."""
import pathlib
import atexit
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

output = pathlib.Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)


def adb(*args):
    return subprocess.check_output(["adb", *args], text=True).strip()


def tree():
    for _ in range(4):
        try:
            adb("shell", "uiautomator", "dump", "--windows", "/sdcard/meltype-window.xml")
            return ET.fromstring(adb("shell", "cat", "/sdcard/meltype-window.xml"))
        except (subprocess.CalledProcessError, ET.ParseError):
            time.sleep(1)
    raise AssertionError("Android accessibility tree is unavailable after waking the emulator")


def find(attribute, value):
    for _ in range(8):
        for node in tree().iter("node"):
            if node.get(attribute) == value:
                return node
        time.sleep(.5)
    raise AssertionError(f"Missing UI element: {attribute}={value}")


def tap(node):
    import re
    left, top, right, bottom = map(int, re.findall(r"\d+", node.attrib["bounds"]))
    assert right > left and bottom > top, "UI target is not visible"
    adb("shell", "input", "tap", str((left + right) // 2), str((top + bottom) // 2))


def capture(name):
    with (output / name).open("wb") as file:
        subprocess.run(["adb", "exec-out", "screencap", "-p"], stdout=file, check=True)


def capture_final_state():
    capture("android-latest.png")
    (output / "ANDROID_UI_TREE.xml").write_text(adb("shell", "cat", "/sdcard/meltype-window.xml"), encoding="utf-8")
    (output / "ANDROID_UI_LOGCAT.txt").write_text(adb("logcat", "-d"), encoding="utf-8")


atexit.register(capture_final_state)


package = "jp.aonesenbongi.meltype"
activity = package + "/" + package + ".MainActivity"
methods = [line.strip() for line in adb("shell", "ime", "list", "-a", "-s").splitlines()]
method = next((line for line in methods if line.startswith(package + "/")), None)
assert method, f"Installed Meltype IME is missing: {methods}"
adb("shell", "am", "force-stop", package)
adb("shell", "ime", "enable", method)
adb("shell", "ime", "set", method)
adb("shell", "input", "keyevent", "224")
adb("shell", "wm", "dismiss-keyguard")
adb("shell", "input", "keyevent", "82")
adb("shell", "am", "start", "-n", activity)
time.sleep(2)
capture("android-setup.png")
find("content-desc", "Meltypeのアイコン")
assert adb("shell", "settings", "get", "secure", "default_input_method") == method, "Meltype is not the selected input method"
editor_id = package + ":id/test_editor"
tap(find("resource-id", editor_id))
find("content-desc", "英語専用モードに切り替える")
tap(find("content-desc", "大文字・小文字を切り替える"))
assert find("content-desc", "q").get("text") == "Q", "Shift does not update letter labels"
tap(find("content-desc", "大文字・小文字を切り替える"))
assert find("content-desc", "q").get("text") == "q"
keys = {node.get("content-desc"): node for node in tree().iter("node") if node.get("class") == "android.widget.Button"}
find("content-desc", "キーボードを閉じる")
for letter in "kyouhagoogledekensaku":
    tap(keys[letter])
    time.sleep(.08)
time.sleep(1)
preview = find("resource-id", editor_id).get("text", "")
assert "google" in preview and "検索" in preview, f"Mixed live conversion failed: {preview}"
find("content-desc", "候補 0")
capture("android-keyboard.png")
tap(find("content-desc", "数字・記号と英字配列を切り替える"))
tap(find("content-desc", "?"))
tap(find("content-desc", "数字・記号と英字配列を切り替える"))
time.sleep(.5)
assert "検索" in find("resource-id", editor_id).get("text", ""), "Question mark lost live conversion"
tap(find("content-desc", "空白・変換"))
find("content-desc", "候補 0")
capture("android-candidates.png")
tap(find("content-desc", "候補 0"))
tap(find("content-desc", "英語専用モードに切り替える"))
mode = find("content-desc", "日英自動判別に戻る")
assert find("content-desc", "句読点、文末はピリオド、長押しでピリオド").get("text") == ",", "English punctuation label is not comma"
assert mode.get("text") == "ABC"
tap(find("content-desc", "大文字・小文字を切り替える"))
tap(find("content-desc", "a"))
assert find("content-desc", "b").get("text") == "b", "Shift remained active after one character"
tap(find("content-desc", "b"))
assert find("resource-id", editor_id).get("text", "").endswith("Ab"), "Shift affected the second character"
for letter in "abc":
    tap(find("content-desc", letter))
assert find("resource-id", editor_id).get("text", "").endswith("abc"), "English mode is not direct input"
capture("android-english.png")
tap(mode)
assert find("content-desc", "英語専用モードに切り替える").get("text") == "日英"

# Start with an empty editor, then verify the unconfirmed suffix separately
# from the editor text (which also contains the confirmed prefix).
adb("shell", "am", "force-stop", package)
adb("shell", "am", "start", "-n", activity)
time.sleep(1)
tap(find("resource-id", editor_id))
keys = {node.get("content-desc"): node for node in tree().iter("node") if node.get("class") == "android.widget.Button"}
for letter in "warewarehautyuujinda":
    tap(keys[letter])
    time.sleep(.04)
assert find("content-desc", "句読点、文末はピリオド、長押しでピリオド").get("text") == "．", "Sentence-ending punctuation label is not period"
tap(find("content-desc", "空白・変換"))
tap(find("content-desc", "候補 0"))
assert find("resource-id", editor_id).get("text") == "我々は宇宙人だ", "Partial confirmation lost editor text"
assert not any(node.get("content-desc") == "未確定文字" for node in tree().iter("node")), "Duplicate preedit row remains"
assert not any("Meltype ·" in node.get("text", "") for node in tree().iter("node")), "Status header remains"
assert not any(node.get("content-desc") == "キーボードを閉じる" for node in tree().iter("node")), "Hide button consumes candidate space"
capture("android-partial-confirm.png")
tap(find("content-desc", "数字・記号と英字配列を切り替える"))
tap(find("content-desc", "「"))
tap(find("content-desc", "」"))
assert "「」" in find("resource-id", editor_id).get("text", ""), "Corner brackets were not inserted"
tap(find("content-desc", "数字・記号と英字配列を切り替える"))
tap(find("content-desc", "英語専用モードに切り替える"))
keys = {node.get("content-desc"): node for node in tree().iter("node") if node.get("class") == "android.widget.Button"}
for letter in "abcdefghij":
    tap(keys[letter])
delete = find("content-desc", "削除、長押しで連続削除")
import re
left, top, right, bottom = map(int, re.findall(r"\d+", delete.attrib["bounds"]))
x, y = str((left + right) // 2), str((top + bottom) // 2)
before = find("resource-id", editor_id).get("text", "")
adb("shell", "input", "swipe", x, y, x, y, "650")
after = find("resource-id", editor_id).get("text", "")
assert len(before) - len(after) >= 3, "Held backspace did not repeat"
time.sleep(.3)
assert find("resource-id", editor_id).get("text", "") == after, "Deletion continued after release"
tap(find("content-desc", "左へ移動"))
tap(find("content-desc", "x"))
expected = after[:-1] + "x" + after[-1:]
assert find("resource-id", editor_id).get("text", "") == expected, "Empty composition left arrow did not move editor cursor"
tap(find("content-desc", "右へ移動"))
tap(find("content-desc", "y"))
after = expected + "y"
assert find("resource-id", editor_id).get("text", "") == after, "Right arrow did not move editor cursor"
tap(find("content-desc", "キーボードを閉じる"))
time.sleep(.3)
assert find("resource-id", editor_id).get("text", "") == after, "Hiding keyboard changed entered text"
assert not any(node.get("content-desc") == "q" for node in tree().iter("node")), "Keyboard did not close"
(output / "ANDROID_UI_TEST_RESULT.txt").write_text(
    "PASS: setup, keyboard taps, conversion, partial confirmation, brackets, held deletion and English mode toggle\n",
    encoding="utf-8",
)
print((output / "ANDROID_UI_TEST_RESULT.txt").read_text())

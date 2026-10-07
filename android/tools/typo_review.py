#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""USB-only acquisition and a localhost review page. Personal data stays outside Git."""
import argparse
import contextlib
import datetime
import hashlib
import json
import pathlib
import secrets
import sqlite3
import subprocess
import threading
import uuid
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PACKAGE = "jp.aonesenbongi.meltype.learning"
BASE = pathlib.Path(__file__).resolve().parents[3]
LABELS = {"pending", "typo", "not_typo"}


class ReviewStore:
    def __init__(self, folder):
        self.folder = pathlib.Path(folder)
        self.folder.mkdir(parents=True, exist_ok=True)
        self.db = self.folder / "review.sqlite3"
        with self.connect() as db:
            db.executescript("""
            CREATE TABLE IF NOT EXISTS edits(id TEXT PRIMARY KEY, pair TEXT, day TEXT, before TEXT, after TEXT);
            CREATE TABLE IF NOT EXISTS labels(pair TEXT PRIMARY KEY, label TEXT NOT NULL);
            """)

    @contextlib.contextmanager
    def connect(self):
        db = sqlite3.connect(self.db, timeout=10)
        try:
            with db:
                yield db
        finally:
            db.close()

    def import_bytes(self, data):
        if len(data) > 8_000_000:
            raise ValueError("記録のサイズが上限を超えています。")
        count = 0
        with self.connect() as db:
            for line in data.splitlines(keepends=True):
                if not line.endswith(b"\n"):  # A concurrent append may leave an incomplete last record.
                    continue
                try:
                    item = json.loads(line)
                    key = str(uuid.UUID(item["id"]))
                    before, after = item["before"], item["after"]
                    day = datetime.date.fromisoformat(item["day"]).isoformat()
                    if not all(isinstance(v, str) and 0 < len(v) <= 64 for v in (before, after)) or before == after:
                        continue
                    pair = hashlib.sha256(json.dumps([before, after], ensure_ascii=True).encode()).hexdigest()
                    result = db.execute("INSERT OR IGNORE INTO edits VALUES(?,?,?,?,?)", (key, pair, day, before, after))
                    count += result.rowcount
                except (ValueError, KeyError, TypeError, UnicodeError):
                    continue
        return count

    def rows(self):
        with self.connect() as db:
            rows = db.execute("""SELECT e.pair, e.before, e.after, COUNT(*), MAX(e.day), COALESCE(l.label,'pending')
                FROM edits e LEFT JOIN labels l ON e.pair=l.pair GROUP BY e.pair ORDER BY COUNT(*) DESC, MAX(e.day) DESC""")
            return [dict(zip(("pair", "before", "after", "count", "day", "label"), row)) for row in rows]

    def label(self, pair, label):
        if label not in LABELS or not isinstance(pair, str) or len(pair) != 64:
            raise ValueError("判定の指定が正しくありません。")
        with self.connect() as db:
            if not db.execute("SELECT 1 FROM edits WHERE pair=?", (pair,)).fetchone():
                raise ValueError("記録が見つかりません。")
            db.execute("INSERT OR REPLACE INTO labels VALUES(?,?)", (pair, label))

    def export(self):
        rows = [r for r in self.rows() if r["label"] == "typo"]
        path = self.folder / "confirmed-typos.json"
        temp = path.with_suffix(".tmp")
        temp.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
        temp.replace(path)
        return len(rows)


def pull_usb(adb, store, run=subprocess.run):
    def command(*args):
        return run([str(adb), "-d", *args], capture_output=True, timeout=20)
    state = command("get-state")
    if state.returncode or state.stdout.strip() != b"device":
        return "USB端末が未接続、未許可、または複数接続されています。"
    result = command("exec-out", "run-as", PACKAGE, "cat", "files/typo-training/events.jsonl")
    if result.returncode:
        return "学習用アプリの記録を取得できません。インストールと収集開始を確認してください。"
    count = store.import_bytes(result.stdout)
    return f"USB取得完了 · 新しい記録 {count}件 · {datetime.datetime.now():%H:%M}"


PAGE = """<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Meltype 編集履歴の確認</title><style>
body{font-family:system-ui,sans-serif;background:#f4f7f8;color:#1c3038;max-width:1100px;margin:30px auto;padding:0 18px}
button,input,select{font:inherit;border:1px solid #a5bac3;border-radius:8px;padding:9px;margin:4px;background:white;color:inherit}
button{cursor:pointer}button[aria-pressed=true]{background:#2c6778;color:white}button:disabled{opacity:.5;cursor:wait}
.record{background:white;border-radius:12px;padding:18px;margin:12px 0;box-shadow:0 1px 3px #0002}
.pair{display:grid;grid-template-columns:1fr 32px 1fr;align-items:center;gap:10px}
.text{white-space:pre-wrap;overflow-wrap:anywhere;font-size:19px}mark{background:#ffe79b;border-radius:3px}
.muted{color:#536872;font-size:14px}h1{font-size:24px}#status{min-height:24px}
@media(max-width:600px){.pair{grid-template-columns:1fr}.arrow{display:none}}
</style><h1>Meltype 編集履歴の確認</h1>
<p>削除前と打ち直し後を比較して判定してください。言い換えなどは「誤字ではない」を選びます。</p>
<p class="muted">確認用アプリを起動している間、USB接続を15秒ごとに確認します。未確認の記録は自動修正に使いません。</p>
<button id="pull">USBから取得</button><button id="export">「誤字」を書き出す</button><p id="status"></p>
<input id="search" placeholder="文字で検索" aria-label="文字で検索"><select id="filter" aria-label="判定で絞り込み">
<option value="pending">未確認</option><option value="all">すべて</option><option value="typo">誤字</option><option value="not_typo">誤字ではない</option></select>
<span id="counts" class="muted"></span><div id="list"></div>
<script>
const token=__TOKEN__;let rows=[];
async function api(path,body){const r=await fetch('/api/'+path,{method:body?'POST':'GET',headers:{'X-Review-Token':token,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});const d=await r.json();if(!r.ok)throw Error(d.error);return d}
function node(tag,text,cls){const e=document.createElement(tag);if(text!==undefined)e.textContent=text;if(cls)e.className=cls;return e}
function diff(text,other){const a=Array.from(text),b=Array.from(other);let s=0,e=0;while(s<Math.min(a.length,b.length)&&a[s]===b[s])s++;while(e<Math.min(a.length-s,b.length-s)&&a[a.length-1-e]===b[b.length-1-e])e++;const d=node('div',undefined,'text');d.append(document.createTextNode(a.slice(0,s).join('')),node('mark',a.slice(s,a.length-e).join('')),document.createTextNode(a.slice(a.length-e).join('')));return d}
function render(){const list=document.getElementById('list');list.replaceChildren();const q=document.getElementById('search').value,f=document.getElementById('filter').value;
document.getElementById('counts').textContent=`${rows.length}組 · 未確認 ${rows.filter(r=>r.label==='pending').length}組`;
const shown=rows.filter(r=>(f==='all'||r.label===f)&&(!q||r.before.includes(q)||r.after.includes(q)));
if(!shown.length)list.append(node('p','表示する記録はありません。'));
for(const r of shown){const box=node('div',undefined,'record');box.append(node('p',`${r.count}回 · 最終 ${r.day}`,'muted'));const pair=node('div',undefined,'pair');pair.append(diff(r.before,r.after),node('span','→','arrow'),diff(r.after,r.before));box.append(pair);
for(const [value,title]of [['typo','誤字'],['not_typo','誤字ではない'],['pending','保留']]){const b=node('button',title);b.setAttribute('aria-pressed',String(r.label===value));b.onclick=async()=>{b.disabled=true;try{await api('label',{pair:r.pair,label:value});r.label=value;render()}catch(e){status(e.message);b.disabled=false}};box.append(b)}list.append(box)}}
function status(s){document.getElementById('status').textContent=s}
async function refresh(){try{const d=await api('rows');rows=d.rows;status(d.status);render()}catch(e){status(e.message)}}
document.getElementById('search').oninput=render;document.getElementById('filter').onchange=render;
document.getElementById('pull').onclick=async()=>{const b=document.getElementById('pull');b.disabled=true;try{status((await api('pull',{})).status);await refresh()}catch(e){status(e.message)}finally{b.disabled=false}};
document.getElementById('export').onclick=async()=>{try{const d=await api('export',{});status(`誤字 ${d.count}組を書き出しました。保存先: ${d.path}`)}catch(e){status(e.message)}};
refresh();setInterval(refresh,15000);
</script></html>"""


class ReviewApp:
    def __init__(self, store, adb):
        self.store, self.adb = store, adb
        self.token = secrets.token_urlsafe(32)
        self.status = "USB接続を確認しています。"
        self.stop = threading.Event()
        self.pull_lock = threading.Lock()

    def pull(self):
        if not self.pull_lock.acquire(blocking=False):
            return self.status
        try:
            self.status = pull_usb(self.adb, self.store)
        except (OSError, subprocess.TimeoutExpired, ValueError):
            self.status = "USB取得に失敗しました。ADBの場所と端末の接続を確認してください。"
        finally:
            self.pull_lock.release()
        return self.status

    def watch(self):
        while not self.stop.is_set():
            self.pull()
            self.stop.wait(15)

    def handler(self):
        app = self
        class Handler(BaseHTTPRequestHandler):
            def setup(self):
                super().setup()
                self.connection.settimeout(5)

            def log_message(self, *_):
                pass

            def send(self, data, code=200, kind="application/json"):
                content = json.dumps(data, ensure_ascii=True).encode() if kind == "application/json" else data.encode("utf-8")
                self.send_response(code)
                self.send_header("Content-Type", kind + "; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.send_header("Cache-Control", "no-store")
                self.send_header("X-Content-Type-Options", "nosniff")
                self.send_header("Content-Security-Policy", "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'")
                self.end_headers(); self.wfile.write(content)

            def trusted(self, api=False):
                host = self.headers.get("Host", "")
                expected = f"127.0.0.1:{self.server.server_port}"
                origin = self.headers.get("Origin")
                if host != expected or origin not in (None, "http://" + expected):
                    return False
                return not api or secrets.compare_digest(self.headers.get("X-Review-Token", ""), app.token)

            def do_GET(self):
                if not self.trusted(self.path != "/"):
                    self.send({"error": "アクセスを許可できません。"}, 403); return
                if self.path == "/":
                    self.send(PAGE.replace("__TOKEN__", json.dumps(app.token)), kind="text/html")
                elif self.path == "/api/rows":
                    self.send({"rows": app.store.rows(), "status": app.status})
                else:
                    self.send({"error": "ページが見つかりません。"}, 404)

            def do_POST(self):
                if not self.trusted(True):
                    self.send({"error": "アクセスを許可できません。"}, 403); return
                try:
                    length = int(self.headers.get("Content-Length", "0"))
                    if not 0 < length <= 1024:
                        raise ValueError("操作の指定が正しくありません。")
                    data = json.loads(self.rfile.read(length))
                    if self.path == "/api/label":
                        app.store.label(data["pair"], data["label"]); self.send({"ok": True})
                    elif self.path == "/api/pull":
                        self.send({"status": app.pull()})
                    elif self.path == "/api/export":
                        self.send({"count": app.store.export(), "path": str(app.store.folder / "confirmed-typos.json")})
                    else:
                        self.send({"error": "操作が見つかりません。"}, 404)
                except (ValueError, TypeError, KeyError, OSError, sqlite3.Error):
                    self.send({"error": "操作を保存できません。指定内容と保存先の空きを確認してください。"}, 400)
        return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default=str(BASE / "android-sdk/platform-tools/adb.exe"))
    parser.add_argument("--data", type=pathlib.Path, default=BASE / "training-data")
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    app = ReviewApp(ReviewStore(args.data), args.adb)
    if args.once:
        print(app.pull()); return
    server = ThreadingHTTPServer(("127.0.0.1", 0), app.handler())
    server.daemon_threads = True
    threading.Thread(target=app.watch, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_port}/"
    print("確認画面: " + url + "\n終了する時はこのウィンドウでCtrl+Cを押してください。")
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        app.stop.set(); server.server_close()


if __name__ == "__main__":
    main()

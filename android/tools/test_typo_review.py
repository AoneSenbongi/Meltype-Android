# SPDX-License-Identifier: GPL-3.0-or-later
import http.client
import json
import pathlib
import subprocess
import tempfile
import threading
import unittest
import uuid
from http.server import ThreadingHTTPServer
from typo_review import ReviewStore, ReviewApp, pull_usb, PAGE


def record(before="helo", after="hello", key=None):
    return (json.dumps(dict(id=key or str(uuid.uuid4()), before=before, after=after, day="2026-10-07")) + "\n").encode()


class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = ReviewStore(self.temp.name)

    def tearDown(self):
        folder = pathlib.Path(self.temp.name).resolve()
        self.assertEqual(folder.parent, pathlib.Path(tempfile.gettempdir()).resolve())
        for file in folder.iterdir():
            file.unlink()
        self.temp.cleanup()

    def test_dedup_partial_invalid(self):
        data = record()
        self.assertEqual(self.store.import_bytes(data + b'{"id"'), 1)
        self.assertEqual(self.store.import_bytes(data), 0)
        self.assertEqual(self.store.import_bytes(record("x" * 65) + b'null\n' + b'{}\n'), 0)

    def test_group_and_persist_labels_export_only_approved(self):
        self.store.import_bytes(record() + record() + record("later", "latter"))
        first = self.store.rows()[0]
        self.assertEqual(first["count"], 2)
        self.store.label(first["pair"], "not_typo")
        reopened = ReviewStore(self.temp.name)
        self.assertEqual(reopened.rows()[0]["label"], "not_typo")
        self.assertEqual(reopened.export(), 0)
        reopened.label(first["pair"], "typo")
        self.assertEqual(reopened.export(), 1)
        self.assertEqual(len(json.loads((pathlib.Path(self.temp.name) / "confirmed-typos.json").read_text(encoding="utf-8"))), 1)

    def test_punctuation_separate_from_typo_export(self):
        data = json.dumps(dict(id=str(uuid.uuid4()), day="2026-10-08", before="文章です", after="．", kind="punctuation")).encode()+b"\n"
        self.assertEqual(self.store.import_bytes(data), 1)
        row = self.store.rows()[0]
        self.assertEqual(row["kind"], "punctuation")
        self.store.label(row["pair"], "typo")
        self.assertEqual(self.store.export(), 0)
        self.assertEqual(ReviewStore(self.temp.name).rows()[0]["label"], "typo")

    def test_legacy_database_migration_retains_labels(self):
        import sqlite3
        self.store.import_bytes(record())
        pair = self.store.rows()[0]["pair"]
        self.store.label(pair, "not_typo")
        import contextlib
        with contextlib.closing(sqlite3.connect(self.store.db)) as db:
            db.executescript("CREATE TABLE old_edits(id TEXT PRIMARY KEY,pair TEXT,day TEXT,before TEXT,after TEXT); INSERT INTO old_edits SELECT id,pair,day,before,after FROM edits; DROP TABLE edits; ALTER TABLE old_edits RENAME TO edits;")
            db.commit()
        reopened = ReviewStore(self.temp.name)
        self.assertEqual(reopened.rows()[0]["label"], "not_typo")
        self.assertEqual(reopened.rows()[0]["kind"], "edit")

    def test_usb_only(self):
        commands = []
        def run(args, **kwargs):
            commands.append(args)
            return subprocess.CompletedProcess(args, 0, b"device\n" if args[-1] == "get-state" else record(), b"")
        self.assertIn("1件", pull_usb("adb", self.store, run))
        self.assertTrue(all(c[1] == "-d" for c in commands))
        def multiple(args, **kwargs):
            return subprocess.CompletedProcess(args, 1, b"", b"more than one device")
        self.assertIn("複数", pull_usb("adb", self.store, multiple))
        self.assertEqual(len(self.store.rows()), 1)

    def test_web_access_and_escaping(self):
        self.store.import_bytes(record("<img src=x onerror=alert(1)>", "safe"))
        app = ReviewApp(self.store, "nonexistent-adb")
        server = ThreadingHTTPServer(("127.0.0.1", 0), app.handler())
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        try:
            def request(method, path, body=None, headers=None):
                conn = http.client.HTTPConnection("127.0.0.1", server.server_port)
                conn.request(method, path, body=body, headers=headers or {})
                response = conn.getresponse(); content = response.read(); code = response.status; conn.close()
                return code, content
            self.assertEqual(request("GET", "/api/rows")[0], 403)
            headers = {"X-Review-Token": app.token}
            self.assertEqual(request("GET", "/api/rows", headers=headers)[0], 200)
            self.assertEqual(request("GET", "/", headers={"Host": "evil.example"})[0], 403)
            self.assertEqual(request("GET", "/api/rows", headers={**headers, "Origin": "https://evil.example"})[0], 403)
            self.assertEqual(request("POST", "/api/label", "{}", headers=headers)[0], 400)
            pair = self.store.rows()[0]["pair"]
            self.assertEqual(request("POST", "/api/label", json.dumps({"pair": pair, "label": "not_typo"}), headers)[0], 200)
            self.assertIn("textContent", PAGE)
            self.assertNotIn("innerHTML", PAGE)
        finally:
            server.shutdown(); server.server_close(); thread.join()


if __name__ == "__main__":
    unittest.main()

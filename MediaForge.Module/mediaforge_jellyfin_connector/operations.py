"""Durable, fail-closed receipts for connector download handoffs.

A reservation is committed BEFORE calling the core queue handler. A process
crash leaves an uncertain receipt, never an invitation to queue again.
"""

import hashlib
import json
import os
import sqlite3
from contextlib import contextmanager
from pathlib import Path


class OperationLedger:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        if os.name != "nt":
            # Receipts include media selections; keep them private like the
            # rest of MediaForge's configuration, including the SQLite journal.
            descriptor = os.open(self.path, os.O_CREAT | os.O_WRONLY, 0o600)
            os.close(descriptor)
            self.path.chmod(0o600)
        with self.connect() as conn:
            conn.execute("CREATE TABLE IF NOT EXISTS receipts (id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, queue_id INTEGER, payload TEXT, watermark INTEGER)")
            columns = {row[1] for row in conn.execute("PRAGMA table_info(receipts)")}
            for column, kind in (("payload", "TEXT"), ("watermark", "INTEGER")):
                if column not in columns:
                    conn.execute(f"ALTER TABLE receipts ADD COLUMN {column} {kind}")

    @contextmanager
    def connect(self):
        conn = sqlite3.connect(self.path, timeout=15)
        try:
            conn.execute("PRAGMA synchronous=FULL")
            with conn:
                yield conn
        finally:
            conn.close()

    def reserve(self, operation_id, body, watermark=None):
        fingerprint = hashlib.sha256(json.dumps(body, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        with self.connect() as conn:
            conn.execute("BEGIN IMMEDIATE")
            row = conn.execute("SELECT fingerprint, queue_id FROM receipts WHERE id=?", (operation_id,)).fetchone()
            if row:
                return ("conflict", None) if row[0] != fingerprint else ("confirmed" if row[1] else "uncertain", row[1])
            conn.execute("INSERT INTO receipts(id,fingerprint,payload,watermark) VALUES (?,?,?,?)", (operation_id, fingerprint, json.dumps(body), watermark))
            return "new", None

    def confirm(self, operation_id, queue_id):
        with self.connect() as conn:
            conn.execute("UPDATE receipts SET queue_id=? WHERE id=?", (queue_id, operation_id))

    def lookup(self, operation_id):
        with self.connect() as conn:
            row = conn.execute("SELECT queue_id FROM receipts WHERE id=?", (operation_id,)).fetchone()
        return {"state": "missing" if row is None else "confirmed" if row[0] else "uncertain", "queue_id": row[0] if row else None}

    def reconcile(self, operation_id, queue):
        with self.connect() as conn:
            row = conn.execute("SELECT payload, watermark, queue_id FROM receipts WHERE id=?", (operation_id,)).fetchone()
        if row is None or row[2] or row[1] is None or not row[0]:
            return self.lookup(operation_id)
        body = json.loads(row[0])
        candidates = []
        for item in queue:
            if not isinstance(item, dict) or type(item.get("id")) is not int or item["id"] <= row[1]:
                continue
            episodes = item.get("episodes")
            if isinstance(episodes, str):
                try:
                    episodes = json.loads(episodes)
                except ValueError:
                    continue
            if (not isinstance(episodes, list) or not all(isinstance(e, str) for e in episodes)
                    or set(episodes) != set(body["episodes"])):
                continue
            if any(item.get(key) != body.get(key) for key in ("title", "series_url", "language", "provider", "custom_path_id")):
                continue
            if bool(item.get("upscale")) != bool(body.get("upscale")):
                continue
            candidates.append(item["id"])
        if len(candidates) == 1:
            self.confirm(operation_id, candidates[0])
        return self.lookup(operation_id)

"""Small self-hosted room directory. It does not carry race traffic."""
from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import re
import secrets
import ssl
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit


class Directory:
    def __init__(self, lease_seconds: float = 120):
        self.lease_seconds = lease_seconds
        self.rooms: dict[str, dict] = {}
        self.lock = threading.Lock()

    def prune(self):
        now = time.monotonic()
        for key in list(self.rooms):
            if self.rooms[key]["deadline"] <= now:
                del self.rooms[key]

    def listings(self):
        with self.lock:
            self.prune()
            return [dict(entry["room"]) for _, entry in sorted(self.rooms.items())]

    def announce(self, room_id, payload, address, token):
        if not re.fullmatch(r"[A-Za-z0-9_-]{8,64}", room_id):
            raise ValueError("Invalid room ID")
        if not isinstance(payload, dict) or payload.get("private", False) is not False:
            raise ValueError("Private rooms must not be published")
        name = payload.get("name")
        if not isinstance(name, str) or not name.strip() or len(name) > 64 or any(ord(c) < 32 for c in name):
            raise ValueError("Room name must contain 1-64 printable characters")
        port, players, capacity = (payload.get(k) for k in ("port", "players", "capacity"))
        if any(type(v) is not int for v in (port, players, capacity)):
            raise ValueError("Port, players and capacity must be integers")
        if not 1 <= port <= 65535 or not 1 <= capacity <= 60 or not 1 <= players <= capacity:
            raise ValueError("Invalid endpoint or player counts")
        with self.lock:
            self.prune()
            old = self.rooms.get(room_id)
            created = old is None
            if old:
                if not self.authorized(old, token):
                    raise PermissionError("Room lease token is invalid")
                issued_token = None
                digest = old["token_digest"]
            else:
                if len(self.rooms) >= 10000:
                    raise ValueError("Directory is full")
                issued_token = secrets.token_urlsafe(32)
                digest = hashlib.sha256(issued_token.encode()).digest()
            room = {"roomId": room_id, "name": name.strip(), "address": address,
                    "port": port, "players": players, "capacity": capacity, "protocol": 1}
            self.rooms[room_id] = {"room": room, "token_digest": digest,
                                   "deadline": time.monotonic() + self.lease_seconds}
            result = {"room": room, "leaseSeconds": self.lease_seconds}
            if issued_token:
                result["leaseToken"] = issued_token
            return result, created

    @staticmethod
    def authorized(entry, token):
        return isinstance(token, str) and len(token) <= 128 and hmac.compare_digest(
            entry["token_digest"], hashlib.sha256(token.encode()).digest())

    def remove(self, room_id, token):
        with self.lock:
            self.prune()
            entry = self.rooms.get(room_id)
            if entry is None:
                return False
            if not self.authorized(entry, token):
                raise PermissionError("Room lease token is invalid")
            del self.rooms[room_id]
            return True


class DirectoryServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, directory=None):
        self.directory = directory or Directory()
        super().__init__(address, Handler)


class Handler(BaseHTTPRequestHandler):
    server_version = "OfflineLabDirectory/1"

    def setup(self):
        super().setup()
        self.connection.settimeout(5)

    def log_message(self, fmt, *args):
        # Avoid recording nicknames, lease tokens or client addresses.
        return

    def reply(self, status, data):
        raw = json.dumps(data, ensure_ascii=True, separators=(",", ":")).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(raw)

    def token(self):
        header = self.headers.get("Authorization", "")
        return header[7:] if header.startswith("Bearer ") else ""

    def route_room(self):
        path = urlsplit(self.path).path
        prefix = "/v1/rooms/"
        return path[len(prefix):] if path.startswith(prefix) else None

    def do_GET(self):
        path = urlsplit(self.path).path
        if path == "/health":
            return self.reply(200, {"status": "ok", "protocol": 1})
        if path == "/v1/rooms":
            return self.reply(200, {"rooms": self.server.directory.listings()})
        self.reply(404, {"error": "Not found"})

    def do_POST(self):
        room_id = self.route_room()
        if room_id is None:
            return self.reply(404, {"error": "Not found"})
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 1 <= size <= 4096:
                return self.reply(413, {"error": "Body must contain 1-4096 bytes"})
            payload = json.loads(self.rfile.read(size))
            result, created = self.server.directory.announce(
                room_id, payload, self.client_address[0], self.token())
            self.reply(201 if created else 200, result)
        except PermissionError as exc:
            self.reply(403, {"error": str(exc)})
        except (ValueError, UnicodeDecodeError) as exc:
            self.reply(400, {"error": str(exc)})

    def do_DELETE(self):
        room_id = self.route_room()
        if room_id is None:
            return self.reply(404, {"error": "Not found"})
        try:
            removed = self.server.directory.remove(room_id, self.token())
            self.reply(200 if removed else 404, {"removed": removed})
        except PermissionError as exc:
            self.reply(403, {"error": str(exc)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=47782)
    parser.add_argument("--cert", help="TLS certificate chain in PEM format")
    parser.add_argument("--key", help="TLS private key in PEM format")
    args = parser.parse_args()
    if bool(args.cert) != bool(args.key):
        parser.error("--cert and --key must be supplied together")
    with DirectoryServer((args.bind, args.port)) as server:
        if args.cert:
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.load_cert_chain(args.cert, args.key)
            server.socket = context.wrap_socket(server.socket, server_side=True)
        print(f"Room directory listening on {args.bind}:{server.server_port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()

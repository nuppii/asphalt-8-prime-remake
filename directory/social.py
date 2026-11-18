"""Opt-in self-hosted identities, friends, presence and room invitation inboxes.

Runs alongside the room directory. It never starts or joins a game for a player.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import ipaddress
import json
import re
import secrets
import sqlite3
import ssl
import threading
import time
import unicodedata
from pathlib import Path
from urllib.parse import urlsplit

from server import Directory, DirectoryServer, Handler, room_name, valid_room_id


class SocialError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def text(value, maximum, label, empty=False):
    if not isinstance(value, str):
        raise SocialError(400, f"{label} must be text")
    value = unicodedata.normalize("NFC", value.strip())
    if (not empty and not value) or len(value) > maximum or any(unicodedata.category(c).startswith("C") for c in value):
        raise SocialError(400, f"Invalid {label.lower()}")
    return value


def identifier(value, prefix):
    if not isinstance(value, str) or not re.fullmatch(re.escape(prefix) + r"[A-Za-z0-9_-]{20,48}", value):
        raise SocialError(400, "Invalid reference ID")
    return value


def canonical_url(value):
    if not isinstance(value, str) or len(value) > 2048:
        raise SocialError(400, "Invalid directory URL")
    try:
        parsed = urlsplit(value)
    except ValueError as exc:
        raise SocialError(400, "Invalid directory URL") from exc
    if parsed.username or parsed.password or parsed.query or parsed.fragment or not parsed.hostname:
        raise SocialError(400, "Invalid directory URL")
    try:
        loopback = parsed.hostname.lower() == "localhost" or ipaddress.ip_address(parsed.hostname).is_loopback
    except ValueError:
        loopback = False
    if parsed.scheme != "https" and not (parsed.scheme == "http" and loopback):
        raise SocialError(400, "Use HTTPS; HTTP is permitted only on loopback")
    try:
        port = parsed.port
    except ValueError as exc:
        raise SocialError(400, "Invalid directory port") from exc
    host = parsed.hostname.lower()
    if ":" in host:
        host = f"[{host}]"
    default = 443 if parsed.scheme == "https" else 80
    suffix = f":{port}" if port and port != default else ""
    return f"{parsed.scheme}://{host}{suffix}{parsed.path.rstrip('/')}"


def parse_room_reference(reference):
    if not isinstance(reference, str) or not reference.startswith("a8ol:v1:") or len(reference) > 6000:
        raise SocialError(400, "Invalid room invitation reference")
    encoded = reference[8:]
    if not re.fullmatch(r"[A-Za-z0-9_-]+", encoded) or len(encoded) % 4 == 1:
        raise SocialError(400, "Invalid room invitation encoding")
    try:
        raw = base64.b64decode(encoded + "=" * (-len(encoded) % 4), altchars=b"-_", validate=True)
        if len(raw) > 4096:
            raise ValueError("size")
        room = json.loads(raw)
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise SocialError(400, "Invalid room invitation payload") from exc
    allowed = {"Version", "RoomId", "RoomName", "DirectoryUrl", "Address", "Port", "IsPrivate"}
    if not isinstance(room, dict) or set(room) - allowed or type(room.get("Version")) is not int or room["Version"] != 1:
        raise SocialError(400, "Unsupported room invitation payload")
    if not valid_room_id(room.get("RoomId")) or type(room.get("IsPrivate")) is not bool:
        raise SocialError(400, "Invalid room invitation identity")
    try:
        room["RoomName"] = room_name(room.get("RoomName"))
    except ValueError as exc:
        raise SocialError(400, str(exc)) from exc
    directory = room.get("DirectoryUrl")
    if directory:
        if room["IsPrivate"] or room.get("Address") is not None or room.get("Port", 0) != 0:
            raise SocialError(400, "Invalid directory invitation endpoint")
        room["DirectoryUrl"] = canonical_url(directory)
    else:
        address, port = room.get("Address"), room.get("Port")
        if not isinstance(address, str) or len(address) > 253 or type(port) is not int or not 1 <= port <= 65535:
            raise SocialError(400, "Invalid direct invitation endpoint")
        try:
            ip = ipaddress.ip_address(address)
            if ip.is_unspecified or ip.is_multicast or str(ip) == "255.255.255.255":
                raise SocialError(400, "Invitation address must be unicast")
        except ValueError:
            if not re.fullmatch(r"(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)*[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?", address):
                raise SocialError(400, "Invalid direct invitation host")
    return room


class SocialStore:
    def __init__(self, path, presence_seconds=45, invitation_seconds=900, clock=time.time):
        if not 1 <= presence_seconds <= 3600 or not 1 <= invitation_seconds <= 86400:
            raise ValueError("Invalid social TTL configuration")
        self.clock, self.presence_seconds, self.invitation_seconds = clock, presence_seconds, invitation_seconds
        self.lock = threading.RLock()
        self.limits = {}
        path = Path(path).resolve()
        path.parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(path, check_same_thread=False)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA foreign_keys=ON")
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS players(
                id TEXT PRIMARY KEY, token_hash BLOB NOT NULL, nickname TEXT NOT NULL,
                status TEXT NOT NULL, last_seen REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS requests(
                id TEXT PRIMARY KEY, sender TEXT NOT NULL REFERENCES players(id),
                recipient TEXT NOT NULL REFERENCES players(id), state TEXT NOT NULL,
                created REAL NOT NULL, expires REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS request_recipient ON requests(recipient,state);
            CREATE TABLE IF NOT EXISTS friends(
                low TEXT NOT NULL REFERENCES players(id), high TEXT NOT NULL REFERENCES players(id),
                created REAL NOT NULL, PRIMARY KEY(low,high));
            CREATE TABLE IF NOT EXISTS invitations(
                id TEXT PRIMARY KEY, sender TEXT NOT NULL REFERENCES players(id),
                recipient TEXT NOT NULL REFERENCES players(id), reference TEXT NOT NULL,
                created REAL NOT NULL, expires REAL NOT NULL, state TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS invitation_recipient ON invitations(recipient,state);
        """)

    def close(self):
        with self.lock:
            self.db.close()

    def prune(self):
        # Keep only active requests and inbox entries; identities and friendships persist.
        now = self.clock()
        self.db.execute("DELETE FROM invitations WHERE expires<=? OR state!='pending'", (now,))
        self.db.execute("DELETE FROM requests WHERE expires<=? OR state!='pending'", (now,))

    def limit(self, key, maximum, window=60):
        with self.lock:
            now = self.clock()
            start, count = self.limits.get(key, (now, 0))
            if now - start >= window:
                start, count = now, 0
            if count >= maximum:
                raise SocialError(429, "Request rate limit exceeded")
            self.limits[key] = start, count + 1
            if len(self.limits) > 20000:
                self.limits = {k: v for k, v in self.limits.items() if now - v[0] < window}

    def register(self, nickname):
        nickname = text(nickname, 24, "Nickname")
        token = secrets.token_urlsafe(32)
        player_id = "p_" + secrets.token_urlsafe(18)
        with self.lock, self.db:
            if self.db.execute("SELECT count(*) FROM players").fetchone()[0] >= 10000:
                raise SocialError(503, "Identity capacity reached")
            self.db.execute("INSERT INTO players VALUES(?,?,?,?,?)",
                            (player_id, hashlib.sha256(token.encode()).digest(), nickname, "Ready", self.clock()))
            return {"playerId": player_id, "authToken": token, "profile": self.profile(player_id)}

    def authenticate(self, player_id, token):
        if not isinstance(player_id, str) or not isinstance(token, str) or len(player_id) > 64 or not 1 <= len(token) <= 128:
            raise SocialError(401, "Authentication required")
        with self.lock:
            row = self.db.execute("SELECT token_hash FROM players WHERE id=?", (player_id,)).fetchone()
            expected = bytes(row[0]) if row else bytes(32)
            matches = hmac.compare_digest(expected, hashlib.sha256(token.encode()).digest())
            if not row or not matches:
                raise SocialError(401, "Authentication required")
            return player_id

    def profile(self, player_id):
        row = self.db.execute("SELECT id,nickname,status,last_seen FROM players WHERE id=?", (player_id,)).fetchone()
        if row is None:
            raise SocialError(404, "Player not found")
        return {"playerId": row["id"], "nickname": row["nickname"], "status": row["status"],
                "online": self.clock() - row["last_seen"] < self.presence_seconds, "lastSeenUtc": row["last_seen"]}

    def update_presence(self, player_id, payload):
        if not isinstance(payload, dict) or set(payload) - {"nickname", "status"}:
            raise SocialError(400, "Invalid profile update")
        with self.lock, self.db:
            old = self.profile(player_id)
            nickname = text(payload.get("nickname", old["nickname"]), 24, "Nickname")
            status = text(payload.get("status", old["status"]), 64, "Status", empty=True)
            self.db.execute("UPDATE players SET nickname=?,status=?,last_seen=? WHERE id=?",
                            (nickname, status, self.clock(), player_id))
            return self.profile(player_id)

    def are_friends(self, first, second):
        low, high = sorted((first, second))
        return self.db.execute("SELECT 1 FROM friends WHERE low=? AND high=?", (low, high)).fetchone() is not None

    def friends(self, player_id):
        with self.lock:
            rows = self.db.execute("SELECT low,high FROM friends WHERE low=? OR high=? ORDER BY created LIMIT 500", (player_id, player_id))
            return {"friends": [self.profile(r["high"] if r["low"] == player_id else r["low"]) for r in rows]}

    def request_friend(self, sender, recipient):
        identifier(recipient, "p_")
        if sender == recipient:
            raise SocialError(400, "You cannot request yourself")
        with self.lock, self.db:
            self.prune()
            self.profile(recipient)
            if self.are_friends(sender, recipient):
                raise SocialError(409, "This player is already your friend")
            old = self.db.execute("SELECT * FROM requests WHERE state='pending' AND expires>? AND ((sender=? AND recipient=?) OR (sender=? AND recipient=?))",
                                  (self.clock(), sender, recipient, recipient, sender)).fetchone()
            if old:
                if old["sender"] == sender:
                    return self.request_view(old)
                raise SocialError(409, "Accept or reject this player's existing request instead")
            for person in (sender, recipient):
                count = self.db.execute("SELECT count(*) FROM requests WHERE state='pending' AND expires>? AND (sender=? OR recipient=?)", (self.clock(), person, person)).fetchone()[0]
                if count >= 500:
                    raise SocialError(409, "Pending friend request limit reached")
            request_id = "q_" + secrets.token_urlsafe(18)
            self.db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?)", (request_id, sender, recipient, "pending", self.clock(), self.clock() + 604800))
            return self.request_view(self.db.execute("SELECT * FROM requests WHERE id=?", (request_id,)).fetchone())

    def request_view(self, row):
        return {"requestId": row["id"], "sender": self.profile(row["sender"]), "recipient": self.profile(row["recipient"]),
                "state": row["state"], "createdUtc": row["created"], "expiresUtc": row["expires"]}

    def requests(self, player_id):
        with self.lock:
            rows = self.db.execute("SELECT * FROM requests WHERE state='pending' AND expires>? AND (sender=? OR recipient=?) ORDER BY created LIMIT 500", (self.clock(), player_id, player_id)).fetchall()
            return {"requests": [self.request_view(r) for r in rows]}

    def decide_request(self, player_id, request_id, accept):
        identifier(request_id, "q_")
        with self.lock, self.db:
            row = self.db.execute("SELECT * FROM requests WHERE id=? AND recipient=?", (request_id, player_id)).fetchone()
            if row is None:
                raise SocialError(404, "Friend request not found")
            if row["state"] != "pending" or row["expires"] <= self.clock():
                raise SocialError(409, "Friend request is no longer pending")
            if accept:
                low, high = sorted((row["sender"], row["recipient"]))
                for person in (low, high):
                    if self.db.execute("SELECT count(*) FROM friends WHERE low=? OR high=?", (person, person)).fetchone()[0] >= 500:
                        raise SocialError(409, "Friend list is full")
                self.db.execute("INSERT OR IGNORE INTO friends VALUES(?,?,?)", (low, high, self.clock()))
            self.db.execute("UPDATE requests SET state=? WHERE id=?", ("accepted" if accept else "rejected", request_id))
            return {"state": "accepted" if accept else "rejected"}

    def send_invitation(self, sender, recipient, reference):
        identifier(recipient, "p_")
        with self.lock, self.db:
            self.prune()
            if not self.are_friends(sender, recipient):
                raise SocialError(403, "Invitations require an accepted mutual friendship")
            if self.db.execute("SELECT count(*) FROM invitations WHERE recipient=? AND state='pending' AND expires>?", (recipient, self.clock())).fetchone()[0] >= 100:
                raise SocialError(409, "Recipient invitation inbox is full")
            invitation_id = "i_" + secrets.token_urlsafe(18)
            self.db.execute("INSERT INTO invitations VALUES(?,?,?,?,?,?,?)", (invitation_id, sender, recipient, reference,
                            self.clock(), self.clock() + self.invitation_seconds, "pending"))
            return {"invitationId": invitation_id, "state": "pending", "expiresUtc": self.clock() + self.invitation_seconds}

    def invitations(self, recipient):
        with self.lock:
            rows = self.db.execute("SELECT * FROM invitations WHERE recipient=? AND state='pending' AND expires>? ORDER BY created LIMIT 100", (recipient, self.clock())).fetchall()
            return {"invitations": [{"invitationId": r["id"], "sender": self.profile(r["sender"]), "reference": r["reference"],
                                     "createdUtc": r["created"], "expiresUtc": r["expires"]} for r in rows]}

    def dismiss(self, recipient, invitation_id):
        identifier(invitation_id, "i_")
        with self.lock, self.db:
            row = self.db.execute("SELECT id FROM invitations WHERE id=? AND recipient=?", (invitation_id, recipient)).fetchone()
            if row is None:
                raise SocialError(404, "Invitation not found")
            self.db.execute("UPDATE invitations SET state='dismissed' WHERE id=?", (invitation_id,))
            return {"state": "dismissed"}


class SocialDirectoryServer(DirectoryServer):
    def __init__(self, address, store, directory=None, public_url=None):
        if public_url is None:
            try:
                if not ipaddress.ip_address(address[0]).is_loopback:
                    raise ValueError("A canonical --public-url is required for non-loopback deployment")
            except ValueError:
                raise ValueError("A canonical --public-url is required for non-loopback deployment") from None
        self.social = store
        self.workers = threading.BoundedSemaphore(64)
        super().__init__(address, directory or Directory())
        self.public_url = canonical_url(public_url or f"http://127.0.0.1:{self.server_port}")
        self.RequestHandlerClass = SocialHandler

    def process_request(self, request, client_address):
        if not self.workers.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except BaseException:
            self.workers.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.workers.release()


class SocialHandler(Handler):
    def do_GET(self):
        if urlsplit(self.path).path.startswith("/v1/social/"):
            return self.social_request("GET")
        return super().do_GET()

    def do_POST(self):
        if urlsplit(self.path).path.startswith("/v1/social/"):
            return self.social_request("POST")
        return super().do_POST()

    def social_request(self, method):
        store = self.server.social
        parsed = urlsplit(self.path)
        try:
            if len(self.path) > 4096 or parsed.query or parsed.fragment:
                raise SocialError(400, "Social routes do not accept query parameters")
            path = parsed.path
            body = None
            if method == "POST":
                try:
                    size = int(self.headers.get("Content-Length", "0"))
                    if not 2 <= size <= 8192:
                        raise SocialError(413, "Social request body must contain 2-8192 bytes")
                    body = json.loads(self.rfile.read(size))
                except (ValueError, UnicodeError, RecursionError) as exc:
                    raise SocialError(400, "Invalid JSON request body") from exc
                if not isinstance(body, dict):
                    raise SocialError(400, "A JSON object is required")
            if method == "POST" and path == "/v1/social/identities":
                store.limit(("register", self.client_address[0]), 60)
                if set(body) != {"nickname"}:
                    raise SocialError(400, "Only a nickname is required to create an identity")
                return self.reply(201, store.register(body["nickname"]))
            actor = store.authenticate(self.headers.get("X-Player-ID", ""), self.token())
            store.limit(("actor", actor), 180)
            if path == "/v1/social/me":
                if method == "GET":
                    with store.lock:
                        return self.reply(200, store.profile(actor))
                return self.reply(200, store.update_presence(actor, body))
            if method == "GET" and path == "/v1/social/friends":
                return self.reply(200, store.friends(actor))
            if path == "/v1/social/friend-requests":
                if method == "GET":
                    return self.reply(200, store.requests(actor))
                store.limit(("friend-request", actor), 20)
                if set(body) != {"recipientId"}:
                    raise SocialError(400, "A recipient player ID is required")
                return self.reply(201, store.request_friend(actor, body["recipientId"]))
            decision = re.fullmatch(r"/v1/social/friend-requests/(q_[A-Za-z0-9_-]{20,48})/(accept|reject)", path)
            if method == "POST" and decision:
                if body:
                    raise SocialError(400, "No additional data is required")
                return self.reply(200, store.decide_request(actor, decision[1], decision[2] == "accept"))
            if path == "/v1/social/invitations":
                if method == "GET":
                    return self.reply(200, store.invitations(actor))
                store.limit(("invite", actor), 20)
                if set(body) - {"recipientId", "reference", "roomLeaseToken"}:
                    raise SocialError(400, "Invalid invitation fields")
                reference = body.get("reference")
                room = parse_room_reference(reference)
                if room.get("DirectoryUrl") and room["DirectoryUrl"] != self.server.public_url:
                    raise SocialError(400, "Invitation references another directory")
                if not room["IsPrivate"]:
                    directory = self.server.directory
                    with directory.lock:
                        directory.prune()
                        entry = directory.rooms.get(room["RoomId"])
                        if entry is None or not directory.authorized(entry, body.get("roomLeaseToken")):
                            raise SocialError(403, "The sender must own the public room lease")
                        if entry["room"]["players"] >= entry["room"]["capacity"]:
                            raise SocialError(409, "The public room is full")
                        if not room.get("DirectoryUrl") and (room["Address"] != entry["room"]["address"] or room["Port"] != entry["room"]["port"]):
                            raise SocialError(409, "The public room invitation endpoint is stale")
                return self.reply(201, store.send_invitation(actor, body.get("recipientId"), reference))
            dismissal = re.fullmatch(r"/v1/social/invitations/(i_[A-Za-z0-9_-]{20,48})/dismiss", path)
            if method == "POST" and dismissal:
                if body:
                    raise SocialError(400, "No additional data is required")
                return self.reply(200, store.dismiss(actor, dismissal[1]))
            raise SocialError(404, "Social route not found")
        except SocialError as exc:
            return self.reply(exc.status, {"error": str(exc)})
        except (sqlite3.Error, OSError):
            return self.reply(503, {"error": "The social store is unavailable"})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", required=True, help="Persistent SQLite database path")
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=47782)
    parser.add_argument("--public-url", help="Canonical directory URL; required outside loopback")
    parser.add_argument("--cert")
    parser.add_argument("--key")
    args = parser.parse_args()
    if bool(args.cert) != bool(args.key):
        parser.error("--cert and --key must be supplied together")
    store = SocialStore(args.database)
    try:
        with SocialDirectoryServer((args.bind, args.port), store, public_url=args.public_url) as server:
            if args.cert:
                context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
                context.load_cert_chain(args.cert, args.key)
                server.socket = context.wrap_socket(server.socket, server_side=True)
            print(f"Social directory listening on {args.bind}:{server.server_port}", flush=True)
            try:
                server.serve_forever()
            except KeyboardInterrupt:
                pass
    finally:
        store.close()


if __name__ == "__main__":
    main()

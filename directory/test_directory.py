import json
from pathlib import Path
import subprocess
import threading
import time
import unittest
import urllib.error
import urllib.request
import urllib.parse

from server import Directory, DirectoryServer


class DirectoryTests(unittest.TestCase):
    def setUp(self):
        self.directory = Directory(lease_seconds=1)
        self.server = DirectoryServer(("127.0.0.1", 0), self.directory)
        self.worker = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.worker.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        self.room = {"name": "Test room", "port": 47781, "players": 1, "capacity": 40}

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.worker.join(2)

    def request(self, method, path, body=None, token=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        req = urllib.request.Request(self.url + path, json.dumps(body).encode() if body is not None else None,
                                     headers, method=method)
        try:
            response = urllib.request.urlopen(req, timeout=3)
        except urllib.error.HTTPError as exc:
            response = exc
        with response:
            return response.status, json.load(response)

    def test_create_renew_list_and_delete(self):
        code, result = self.request("POST", "/v1/rooms/test-room", self.room)
        self.assertEqual(code, 201)
        token = result["leaseToken"]
        self.assertEqual(result["room"]["address"], "127.0.0.1")
        self.assertEqual(self.request("GET", "/v1/rooms")[1]["rooms"][0]["capacity"], 40)
        self.assertNotIn("leaseToken", str(self.request("GET", "/v1/rooms")))
        self.assertEqual(self.request("POST", "/v1/rooms/test-room", self.room)[0], 403)
        self.assertEqual(self.request("DELETE", "/v1/rooms/test-room", token="wrong")[0], 403)
        self.assertEqual(self.request("POST", "/v1/rooms/test-room", self.room, token)[0], 200)
        self.assertEqual(self.request("DELETE", "/v1/rooms/test-room", token=token)[0], 200)
        self.assertEqual(self.request("GET", "/v1/rooms")[1]["rooms"], [])

    def test_private_and_invalid_announcements_rejected(self):
        for change in ({"private": True}, {"capacity": 61}, {"players": 41}, {"port": 0},
                       {"players": True}, {"name": "bad\nname"}):
            self.assertEqual(self.request("POST", "/v1/rooms/test-room", dict(self.room, **change))[0], 400)

    def test_expiry_and_source_address(self):
        code, result = self.request("POST", "/v1/rooms/test-room", dict(self.room, address="192.0.2.1"))
        self.assertEqual(code, 201)
        self.assertEqual(result["room"]["address"], "127.0.0.1")
        with self.directory.lock:
            self.directory.rooms["test-room"]["deadline"] = time.monotonic() - 1
        self.assertEqual(self.request("GET", "/v1/rooms")[1]["rooms"], [])

    def test_exact_name_search_preserves_duplicate_choices(self):
        for room_id, name in (("room-one", "Night Riders"), ("room-two", "night riders"),
                              ("room-three", "Night Riders Europe"), ("room-four", "Other")):
            self.assertEqual(self.request("POST", "/v1/rooms/" + room_id, dict(self.room, name=name))[0], 201)
        query = urllib.parse.urlencode({"name": "  NIGHT RIDERS  ", "limit": 1})
        code, result = self.request("GET", "/v1/rooms?" + query)
        self.assertEqual(code, 200)
        self.assertEqual(result["totalMatches"], 2)
        self.assertEqual(len(result["rooms"]), 1)
        self.assertEqual(result["nextOffset"], 1)
        second = self.request("GET", "/v1/rooms?" + query + "&offset=1")[1]
        self.assertEqual(len(second["rooms"]), 1)
        self.assertNotEqual(second["rooms"][0]["roomId"], result["rooms"][0]["roomId"])
        self.assertIsNone(second["nextOffset"])
        prefix = self.request("GET", "/v1/rooms?name=night&match=prefix")[1]
        self.assertEqual(prefix["totalMatches"], 3)
        self.assertEqual(self.request("GET", "/v1/rooms?name=Riders")[1]["rooms"], [])

    def test_resolve_tracks_renewed_endpoint_and_expiry(self):
        _, result = self.request("POST", "/v1/rooms/test-room", self.room)
        token = result["leaseToken"]
        self.assertEqual(self.request("GET", "/v1/rooms/test-room")[1]["room"]["port"], 47781)
        self.request("POST", "/v1/rooms/test-room", dict(self.room, port=47799), token)
        resolved = self.request("GET", "/v1/rooms/test-room")
        self.assertEqual(resolved[1]["room"]["port"], 47799)
        self.assertNotIn("leaseToken", str(resolved))
        self.assertEqual(self.request("GET", "/v1/rooms/absent-room")[0], 404)
        self.request("POST", "/v1/rooms/private-room", dict(self.room, private=True))
        self.assertEqual(self.request("GET", "/v1/rooms/private-room")[0], 404)
        with self.directory.lock:
            self.directory.rooms["test-room"]["deadline"] = time.monotonic() - 1
        self.assertEqual(self.request("GET", "/v1/rooms/test-room")[0], 404)

    def test_search_bounds_and_invalid_names(self):
        for query in ("name=", "name=%20%20", "name=bad%0Aname", "name=bad%7Fname", "name=" + "x" * 65,
                      "name=a&name=b", "name=a&match=fuzzy", "limit=0", "limit=257", "offset=-1", "offset=10001",
                      "unknown=value"):
            self.assertEqual(self.request("GET", "/v1/rooms?" + query)[0], 400, query)

    def test_full_rooms_stay_visible_as_explicit_choices(self):
        self.request("POST", "/v1/rooms/full-room", dict(self.room, name="Full", players=40))
        room = self.request("GET", "/v1/rooms?name=FULL")[1]["rooms"][0]
        self.assertEqual(room["players"], room["capacity"])

    def test_unicode_casefold_and_named_id_lookup(self):
        self.request("POST", "/v1/rooms/unicode-room", dict(self.room, name="Straße Café"))
        query = urllib.parse.urlencode({"name": "STRASSE CAFÉ"})
        self.assertEqual(self.request("GET", "/v1/rooms?" + query)[1]["totalMatches"], 1)
        self.assertEqual(self.request("GET", "/v1/rooms/unicode-room?" + query)[0], 200)
        self.assertEqual(self.request("GET", "/v1/rooms/unicode-room?name=Other")[0], 404)

    def test_csharp_invite_and_named_room_client(self):
        executable = Path(__file__).resolve().parents[1] / "build" / "multiplayer" / "InviteCheck.exe"
        if not executable.is_file():
            self.skipTest("Build multiplayer/Build-Rooms.ps1 to run the C# integration check")
        self.directory.lease_seconds = 30
        result = subprocess.run([str(executable), "directory", self.url], capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("PASS:", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)

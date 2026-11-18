import base64
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

from social import SocialDirectoryServer, SocialError, SocialStore


def invitation(private=True, directory=None, **extra):
    payload = {"Version": 1, "RoomId": "private-room" if private else "public-room",
               "RoomName": "Friends Night", "IsPrivate": private}
    if directory:
        payload["DirectoryUrl"] = directory
    else:
        payload.update(Address="127.0.0.1", Port=47781)
    payload.update(extra)
    return "a8ol:v1:" + base64.urlsafe_b64encode(json.dumps(payload).encode()).decode().rstrip("=")


class SocialTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="offline-lab-social-")
        self.path = Path(self.temporary.name) / "social.sqlite"
        self.now = 1700000000.0
        self.store = SocialStore(self.path, presence_seconds=10, invitation_seconds=30, clock=lambda: self.now)
        self.start_server()

    def start_server(self):
        self.server = SocialDirectoryServer(("127.0.0.1", 0), self.store)
        self.worker = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.worker.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def stop_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.worker.join(2)

    def tearDown(self):
        self.stop_server()
        self.store.close()
        self.temporary.cleanup()

    def request(self, method, path, body=None, identity=None, lease=None):
        headers = {"Content-Type": "application/json"}
        if identity:
            headers["X-Player-ID"] = identity["playerId"]
            headers["Authorization"] = "Bearer " + identity["authToken"]
        if lease:
            headers["Authorization"] = "Bearer " + lease
        request = urllib.request.Request(self.url + path, None if body is None else json.dumps(body).encode(), headers, method=method)
        try:
            response = urllib.request.urlopen(request, timeout=5)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.status, json.load(response)

    def register(self, nickname):
        code, identity = self.request("POST", "/v1/social/identities", {"nickname": nickname})
        self.assertEqual(code, 201)
        self.assertNotIn("@", identity["playerId"])
        return identity

    def friends(self):
        first, second = self.register("Alice"), self.register("Bob")
        code, request = self.request("POST", "/v1/social/friend-requests", {"recipientId": second["playerId"]}, first)
        self.assertEqual(code, 201)
        code, result = self.request("POST", f'/v1/social/friend-requests/{request["requestId"]}/accept', {}, second)
        self.assertEqual((code, result["state"]), (200, "accepted"))
        return first, second

    def test_identities_auth_and_mutual_acceptance(self):
        first, second = self.register("Driver"), self.register("Driver")
        self.assertNotEqual(first["playerId"], second["playerId"])
        self.assertEqual(self.request("GET", "/v1/social/me")[0], 401)
        self.assertEqual(self.request("GET", "/v1/social/me", identity=dict(first, authToken="wrong"))[0], 401)
        self.assertEqual(self.request("GET", "/v1/social/me", identity=first)[1]["playerId"], first["playerId"])
        code, request = self.request("POST", "/v1/social/friend-requests", {"recipientId": second["playerId"]}, first)
        self.assertEqual(code, 201)
        self.assertEqual(self.request("GET", "/v1/social/friends", identity=first)[1]["friends"], [])
        pending = self.request("GET", "/v1/social/friend-requests", identity=second)[1]["requests"]
        self.assertEqual(pending[0]["sender"]["playerId"], first["playerId"])
        self.assertNotIn("authToken", json.dumps(pending))
        self.assertEqual(self.request("POST", f'/v1/social/friend-requests/{request["requestId"]}/accept', {}, first)[0], 404)
        self.assertEqual(self.request("POST", f'/v1/social/friend-requests/{request["requestId"]}/accept', {}, second)[0], 200)
        self.assertEqual(self.request("GET", "/v1/social/friends", identity=first)[1]["friends"][0]["playerId"], second["playerId"])
        self.assertEqual(self.request("GET", "/v1/social/friends", identity=second)[1]["friends"][0]["playerId"], first["playerId"])

    def test_rejection_and_nonfriend_invites(self):
        first, second = self.register("Alice"), self.register("Bob")
        payload = {"recipientId": second["playerId"], "reference": invitation()}
        self.assertEqual(self.request("POST", "/v1/social/invitations", payload, first)[0], 403)
        _, request = self.request("POST", "/v1/social/friend-requests", {"recipientId": second["playerId"]}, first)
        self.assertEqual(self.request("POST", f'/v1/social/friend-requests/{request["requestId"]}/reject', {}, second)[1]["state"], "rejected")
        self.assertEqual(self.request("GET", "/v1/social/friends", identity=first)[1]["friends"], [])
        self.assertEqual(self.request("GET", "/v1/social/friend-requests", identity=second)[1]["requests"], [])

    def test_profile_and_presence_ttl(self):
        first, second = self.friends()
        self.now += 11
        profile = self.request("GET", "/v1/social/friends", identity=second)[1]["friends"][0]
        self.assertFalse(profile["online"])
        code, profile = self.request("POST", "/v1/social/me", {"nickname": "Alice Renamed", "status": "In a race"}, first)
        self.assertEqual(code, 200)
        self.assertEqual(profile["playerId"], first["playerId"])
        friend = self.request("GET", "/v1/social/friends", identity=second)[1]["friends"][0]
        self.assertTrue(friend["online"])
        self.assertEqual((friend["nickname"], friend["status"]), ("Alice Renamed", "In a race"))

    def test_real_invitation_inbox_auth_and_expiry(self):
        first, second = self.friends()
        reference = invitation()
        code, result = self.request("POST", "/v1/social/invitations", {"recipientId": second["playerId"], "reference": reference}, first)
        self.assertEqual(code, 201)
        self.assertEqual(self.request("GET", "/v1/social/invitations", identity=first)[1]["invitations"], [])
        inbox = self.request("GET", "/v1/social/invitations", identity=second)[1]["invitations"]
        self.assertEqual(inbox[0]["reference"], reference)
        self.assertEqual(inbox[0]["sender"]["playerId"], first["playerId"])
        self.assertEqual(self.request("POST", f'/v1/social/invitations/{result["invitationId"]}/dismiss', {}, first)[0], 404)
        self.now += 31
        self.assertEqual(self.request("GET", "/v1/social/invitations", identity=second)[1]["invitations"], [])

    def test_public_room_ownership_and_private_secret_rejection(self):
        first, second = self.friends()
        _, room = self.request("POST", "/v1/rooms/public-room", {"name": "Public", "port": 47781, "players": 1, "capacity": 4})
        payload = {"recipientId": second["playerId"], "reference": invitation(False, self.url)}
        self.assertEqual(self.request("POST", "/v1/social/invitations", payload, first)[0], 403)
        self.assertEqual(self.request("POST", "/v1/social/invitations", dict(payload, roomLeaseToken="wrong"), first)[0], 403)
        self.assertEqual(self.request("POST", "/v1/social/invitations", dict(payload, roomLeaseToken=room["leaseToken"]), first)[0], 201)
        inbox = self.request("GET", "/v1/social/invitations", identity=second)[1]
        self.assertNotIn(room["leaseToken"], json.dumps(inbox))
        secret = {"recipientId": second["playerId"], "reference": invitation(Password="do-not-send")}
        self.assertEqual(self.request("POST", "/v1/social/invitations", secret, first)[0], 400)
        foreign = dict(payload, reference=invitation(False, "https://unrelated.invalid"), roomLeaseToken=room["leaseToken"])
        self.assertEqual(self.request("POST", "/v1/social/invitations", foreign, first)[0], 400)
        malformed = dict(payload, reference=invitation(False, "https://[broken"), roomLeaseToken=room["leaseToken"])
        self.assertEqual(self.request("POST", "/v1/social/invitations", malformed, first)[0], 400)
        self.request("POST", "/v1/rooms/public-room", {"name": "Public", "port": 47781, "players": 4, "capacity": 4}, lease=room["leaseToken"])
        self.assertEqual(self.request("POST", "/v1/social/invitations", dict(payload, roomLeaseToken=room["leaseToken"]), first)[0], 409)

    def test_sqlite_identity_friend_and_inbox_persistence(self):
        first, second = self.friends()
        self.request("POST", "/v1/social/invitations", {"recipientId": second["playerId"], "reference": invitation()}, first)
        self.stop_server()
        self.store.close()
        self.store = SocialStore(self.path, presence_seconds=10, invitation_seconds=30, clock=lambda: self.now)
        self.start_server()
        self.assertEqual(self.request("GET", "/v1/social/me", identity=first)[1]["playerId"], first["playerId"])
        self.assertEqual(self.request("GET", "/v1/social/friends", identity=first)[1]["friends"][0]["playerId"], second["playerId"])
        self.assertEqual(len(self.request("GET", "/v1/social/invitations", identity=second)[1]["invitations"]), 1)
        raw = self.path.read_bytes()
        self.assertNotIn(first["authToken"].encode(), raw)

    def test_request_limits(self):
        self.assertEqual(self.request("POST", "/v1/social/identities", {"nickname": "x" * 9000})[0], 413)
        self.assertEqual(self.request("POST", "/v1/social/identities", {"nickname": "Bad\nName"})[0], 400)
        for _ in range(3):
            self.store.limit(("test", "limit"), 3)
        with self.assertRaises(SocialError) as caught:
            self.store.limit(("test", "limit"), 3)
        self.assertEqual(caught.exception.status, 429)

    def test_csharp_consumer_over_real_http(self):
        executable = Path(__file__).resolve().parents[1] / "build" / "multiplayer" / "SocialCheck.exe"
        if not executable.exists():
            self.skipTest("Build multiplayer/Build-Rooms.ps1 to exercise the C# consumer")
        result = subprocess.run([str(executable), self.url], capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("PASS:", result.stdout)

    def test_native_bridge_over_real_http(self):
        executable = Path(__file__).resolve().parents[1] / "build" / "multiplayer" / "NativeBridgeCheck.exe"
        if not executable.exists():
            self.skipTest("Build multiplayer/Build-NativeBridgeCheck.ps1 to exercise the bridge fixture")
        result = subprocess.run([str(executable), self.url], capture_output=True, text=True, timeout=40)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("PASS:", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)

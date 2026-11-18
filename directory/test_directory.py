import json
import threading
import time
import unittest
import urllib.error
import urllib.request

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


if __name__ == "__main__":
    unittest.main(verbosity=2)

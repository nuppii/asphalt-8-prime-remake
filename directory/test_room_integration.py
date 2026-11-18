"""Run after multiplayer/Build-Rooms.ps1, on an isolated Windows LAN test host."""
import os
from pathlib import Path
import subprocess
import threading
import unittest

from server import Directory, DirectoryServer


class RoomIntegrationTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt", "Requires the Windows .NET Framework build")
    def test_csharp_discovery_and_directory(self):
        executable = Path(__file__).resolve().parents[1] / "build/multiplayer/DiscoveryCheck.exe"
        if not executable.exists():
            self.skipTest("Run multiplayer/Build-Rooms.ps1 first")
        server = DirectoryServer(("127.0.0.1", 0), Directory())
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            result = subprocess.run(
                [str(executable), f"http://127.0.0.1:{server.server_port}"],
                capture_output=True, text=True, timeout=25,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS empty LAN scan", result.stdout)
            self.assertIn("PASS directory cleanup", result.stdout)
        finally:
            server.shutdown()
            server.server_close()
            worker.join(2)


if __name__ == "__main__":
    unittest.main(verbosity=2)

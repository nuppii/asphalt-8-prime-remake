# Room directory service

This optional service helps players find public self-hosted rooms. Race traffic stays with the room host. It is separate from the native game bridge and is not a playable-game server.

Requires Python 3.11 or later, with no third-party dependencies.

For local integration tests:

```powershell
python .\directory\server.py --bind 127.0.0.1 --port 47782
```

Set the interface's public-directory URL to `http://127.0.0.1:47782`. Plain HTTP is accepted by the client only for loopback tests.

For a chosen public deployment with a valid certificate and matching private key:

```text
python directory/server.py --bind 0.0.0.0 --port 47782 --cert /path/fullchain.pem --key /path/privkey.pem
```

Configure clients with the matching reachable **HTTPS** hostname and port. Certificates, keys and deployment credentials must remain outside the repository. No public hostname, server or certificate has been provisioned by this project. This direct TLS setup uses the actual client socket address; a reverse proxy needs explicit trusted-client-address handling before it can advertise the correct host address.

## API

| Method | Path | Result |
| --- | --- | --- |
| GET | `/health` | Service and protocol status |
| GET | `/v1/rooms` | Current unexpired public listings |
| POST | `/v1/rooms/{roomId}` | Create or renew a public lease |
| DELETE | `/v1/rooms/{roomId}` | Remove a lease |

Creation returns a random lease token. Renewals and deletion require `Authorization: Bearer <leaseToken>`. The token is never included in browse results. Leases expire after 120 seconds; the C# host renews every 30 seconds. Private announcements and invalid player/port counts are rejected. Requests and responses have bounded sizes; the service is an initial implementation, without production abuse controls or a deployment SLA.

The announced host IP comes from the request's socket peer. The advertised TCP port must be reachable independently. This directory does not provide relay, NAT traversal, reachability verification, player accounts, persistent friends or clans. Announcements and logs contain no room passwords.

Run the API checks with:

```text
python -m unittest discover -s directory -p test_directory.py -v
```

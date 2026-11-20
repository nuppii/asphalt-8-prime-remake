# Room directory and social service

This optional self-hosted service lists public rooms and delivers friend requests and invitations between registered players. Race traffic stays with the room host. It does not run or synchronize the game.

Requires Python 3.11 or later, with no third-party dependencies.

For local integration tests:

```powershell
python .\directory\social.py --database '<private-data-directory>\social.sqlite' --bind 127.0.0.1 --port 47782
```

Set Connection's directory URL to `http://127.0.0.1:47782`. `social.py` includes the room-directory routes; `server.py` provides the directory alone. Store the SQLite database outside the repository. Plain HTTP is accepted by clients only for loopback tests. Clearing the native Connection URL disconnects social polling.

For a chosen public deployment with a valid certificate and matching private key:

```text
python directory/social.py --database /private/social.sqlite --bind 0.0.0.0 --port 47782 --public-url https://your-host.example:47782 --cert /private/fullchain.pem --key /private/privkey.pem
```

Configure clients with the matching reachable **HTTPS** hostname and port. Certificates, keys and deployment credentials must remain outside the repository. No public hostname, server or certificate has been provisioned by this project. This direct TLS setup uses the actual client socket address; a reverse proxy needs explicit trusted-client-address handling before it can advertise the correct host address.

## API

| Method | Path | Result |
| --- | --- | --- |
| GET | `/health` | Service and protocol status |
| GET | `/v1/rooms` | Current unexpired public listings |
| GET | `/v1/rooms?name=Night%20Race&match=exact&limit=8&offset=0` | Case-insensitive name search; duplicate names remain separate choices |
| GET | `/v1/rooms/{roomId}` | Resolve one active public room |
| POST | `/v1/rooms/{roomId}` | Create or renew a public lease |
| DELETE | `/v1/rooms/{roomId}` | Remove a lease |

Creation returns a random lease token. Renewals and deletion require `Authorization: Bearer <leaseToken>`. The token is never included in browse results. Leases expire after 120 seconds; the C# advertiser renews every 30 seconds. Private announcements and invalid player/port counts are rejected. Name search accepts exact or prefix matching with bounded pagination. Ambiguous names require explicit room selection.

The announced host IP comes from the request's socket peer. The advertised endpoint must be reachable independently. The service does not provide relay, NAT traversal, reachability verification, external accounts or clans. Announcements and logs contain no room passwords.

## Room transports and native publication

`transport` distinguishes `room-control-tcp` (the default for older announcements) from `asphalt480-udp`. Native announcements require `nativeRoomId` containing exactly 16 hexadecimal characters and `discoveryPort` equal to 6529. Their `port` is the actual native UDP lobby port, not the prototype TCP listener or the discovery port. IDs are normalized to lowercase. The random directory `roomId` remains separate from the native ID; a lease cannot change transport or native identity.

```json
{"name":"ASPHALT8","transport":"asphalt480-udp","nativeRoomId":"f968940900000001","discoveryPort":6529,"port":54551,"players":1,"capacity":8,"private":false}
```

The managed native bridge automatically publishes when it observes a real public host room and a configured directory. It withdraws the listing when the room ends, becomes private or loses its directory configuration. A clean installed session verified actual publication and withdrawal against an owned loopback service at `127.0.0.1:47785`; the game's UDP lobby and discovery ports were independently checked in Windows.

Native Join Selected resolves the lease again, checks transport/identity, IPv4 endpoint, port and available capacity, then queues the original browser/discovery path. A controlled nonresponding fixture verified browser dispatch without a crash. **No native peer connection or Internet race was established.** The prototype TCP invitation resolver rejects native UDP listings; metadata publication does not make either transport interchangeable or prove reachability. No public directory has been deployed.

## Social API

`POST /v1/social/identities` with `{"nickname":"Driver"}` returns an opaque `playerId`, random `authToken` and profile. Nicknames are display names, not identity proofs. The token is issued once; keep it private. Other social requests require both `X-Player-ID: <playerId>` and `Authorization: Bearer <authToken>`.

| Method | Path | Result |
| --- | --- | --- |
| GET / POST | `/v1/social/me` | Read profile / update nickname, status and presence heartbeat |
| GET | `/v1/social/friends` | Accepted mutual friends and current presence |
| GET / POST | `/v1/social/friend-requests` | List pending requests / request `recipientId` |
| POST | `/v1/social/friend-requests/{requestId}/accept` or `/reject` | Recipient explicitly accepts or rejects |
| GET / POST | `/v1/social/invitations` | Poll own inbox / send `recipientId` and `reference` |
| POST | `/v1/social/invitations/{invitationId}/dismiss` | Recipient dismisses an invitation |

Acceptance, rejection and dismissal use an empty JSON object. A friendship is mutual only after acceptance. Sending an invitation requires an accepted friendship. Public references additionally require the sender's valid `roomLeaseToken`; private references contain an endpoint and room ID, never a password. The recipient explicitly chooses whether to resolve or join; inbox delivery never starts a game automatically.

SQLite persists identities, friendships and unexpired inbox entries. Tokens are stored as SHA-256 hashes and compared in constant time. Presence expires after 45 seconds without a heartbeat; invitations expire after 15 minutes. The managed client protects its separate per-directory credential file with Windows DPAPI for the current Windows user. Credentials, databases and backups must remain private and outside version control. The service bounds request sizes, concurrent workers, rates and per-player lists; it remains experimental rather than a production hosting platform.

## Validation

Run the API checks with:

```text
.\multiplayer\Build-NativeBridgeCheck.ps1
python -m unittest discover -s directory -p 'test_*.py' -v
```

The suite starts its own loopback services and invokes `InviteCheck.exe`, `SocialCheck.exe`, `NativeTransportCheck.exe` and `NativeBridgeCheck.exe` against them. The latest 23 Python integration checks cover search ambiguity, leases, native transport metadata, incompatible TCP refusal, queued native joins, automatic host publication/private withdrawal, friend acceptance/rejection, authorization, expiry, persistence and protected credentials. The native bridge fixture uses simulated shared memory with real HTTP requests; it does not establish native peer acceptance or race joining. No external contacts are messaged and no public deployment is performed by these tests.

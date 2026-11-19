# Player-hosted Internet races

The intended race server is the current player's PC. A dedicated game server or rented VPS is not a prerequisite. On a graceful host change, the race must pause, transfer to another player's PC and resume from the acknowledged state. Changing a label or transferring the experimental TCP lobby does not satisfy this requirement.

## Discovery and hosting are separate

Direct address / invitation connections must work without the optional directory. A directory can list public rooms and resolve names, but never owns the simulation. The existing directory remains optional; no public service has been deployed or made mandatory.

As a local comparison, the installed Unreal Tournament configuration includes player-hosted networking and several `UdpServerUplink` / `ListFactories` master-server entries. Those entries advertise/find servers; they are not evidence that a third-party machine hosts a player's match or that UT's race/state migration exists in Asphalt.

Without an external directory, a direct invitation needs a reachable address or endpoint information exchanged outside the game. A room name alone cannot discover an arbitrary remote machine. Direct Internet connectivity also depends on the host network: a public endpoint or suitable NAT traversal is needed. An inaccessible CGNAT/restrictive network cannot be guaranteed reachable by changing a game menu. No router, firewall or Windows isolation setting is changed by these tools.

The reference protocols are [STUN (RFC 8489)](https://www.rfc-editor.org/rfc/rfc8489.html) for observed UDP endpoints and [ICE (RFC 8445)](https://www.rfc-editor.org/rfc/rfc8445) for candidate exchange and connectivity checks. Relay fallback is a separate, optional infrastructure decision; STUN alone is not a relay, peer authentication or a guarantee of Internet connectivity.

## Implemented UDP foundation

`multiplayer/StunBinding.cs` implements an unauthenticated STUN Binding subset for IPv4 and IPv6. It creates a cryptographically random transaction, decodes XOR-MAPPED-ADDRESS, checks the optional FINGERPRINT, rejects unknown required attributes and rejects integrity attributes that this subset cannot verify. Responses must match both the transaction and the explicitly selected server endpoint. Retransmissions keep the same socket and transaction; timeouts are bounded and the caller's receive timeout is restored.

The API accepts a caller-owned socket because a public mapping belongs to that socket. Its synchronous probe requires exclusive receive ownership: it must not be called concurrently with the game's receiver. A later native transport adapter needs a shared receive dispatcher, or an explicit native probing phase, rather than consuming gameplay packets through this diagnostic API.

Build and run the local fixture:

```powershell
.\multiplayer\Build-Rooms.ps1
.\build\multiplayer\InternetCheck.exe selftest
```

For a deliberately selected STUN service, the diagnostic accepts an explicit IP and port:

```text
InternetCheck.exe stun <server-IP> <port>
```

No public provider is selected or contacted automatically. The diagnostic owns its own temporary socket; its result is **not** the native game's endpoint. It neither publishes that result as a room nor changes the game's native connection status.

The local fixture checks IPv4/IPv6 decoding, all truncation lengths of both sample packets, transaction mismatch, unknown required attributes, an independently generated CRC fixture and tampering, retransmission from the same socket, rejection of a wrong-source packet, rejection of a wrong transaction, timeout and receive-timeout restoration. It also exercises 1,000 arbitrary bounded attribute streams. These are protocol and socket checks, not cross-PC race tests.

## Native direct IPv4 discovery

The installed native **Find by Name** page now also contains **JOIN BY IPV4 ADDRESS**. Edit the Search field with a four-octet IPv4 address, confirm, then choose that action. Confirmation only edits the field; directory search requires its explicit button. No directory URL is needed for the direct action. The button is enabled only when the targeted native discovery hook is installed.

This uses the game's existing directed discovery callback, not a fabricated room or a global rewrite of outgoing packets. Only the currently requested native browser Finder uses the explicit destination, UDP 6529. Other discovery paths retain their original behavior. Results must have the requested observed IPv4 sender, and the original native Join callback is used only for a unique matching room. Multiple matches remain a manual browser choice. Leaving the browser, dispatching a join, invalid input or reaching the 900-UI-tick bound cancels the attempt and restores default discovery.

The compiled x86 fixture exercises the actual generated wrapper and native helper in owned memory, with stubs for original game callbacks: address parsing and encoding, active versus default discovery, foreign-Finder fallback, cancellation and expiry. A clean installed game session without Frida verified the visible enabled action, native keyboard confirmation, and rejection of empty/invalid input. These checks do **not** establish that a remote host responded, that a native peer joined or that an Internet race ran.

![Installed native IPv4 action rejecting invalid input](images/native-direct-ipv4.jpg)

Discovery alone is insufficient through many routers. Static native analysis identifies three separate endpoints: Announcer UDP 6529, the ephemeral lobby port stored at Room+0x78, and the race port at Room+0x7A published after native server start. Those real endpoints need reachability and correct advertisement; no automatic router mapping or native socket hole punching is shipped.

## Native work still required

1. Validate the new directed discovery against a real remote native host, including response selection and original eligibility checks.
2. Establish the actual game sockets across the selected route, including ports negotiated after discovery. Redirecting multicast queries alone does not establish this.
3. Check peer reachability on the exact sockets used for gameplay before declaring a room join successful. UDP endpoint observations cannot describe the separate experimental TCP room listener.
4. During handover, pause at an authoritative tick and transfer the map/event, cars and tuning, positions and velocities, laps/checkpoints, race clock, results, bot state and applicable random state.
5. Start the successor's native server on its own PC, establish its reachable route, import the checkpoint and obtain acknowledgements from the surviving players before resuming under a new authority epoch.
6. Handle a failed successor without resuming two authorities; handle abrupt host loss only once usable checkpoints are replicated in advance.

The current native server rejects more than 12 participants and contains fixed-size structures. The standalone 60-racer solo patch does not lift this network limit. Native Internet joining remains unverified; race checkpoint import, synchronized resumption and race-host migration remain unimplemented. No Internet-ready room status is enabled by the UDP helper or directed discovery.

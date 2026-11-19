# Validation notes

## Observed checks

| Check | Observation |
| --- | --- |
| Native and managed build | MSVC x86 grid image and Framework C# trainer compile successfully |
| English UI | Game language switched to English and retained after restart; trainer categories, controls and statuses translated |
| Saved settings migration | Earlier French labels are normalized on load for enabled tools and favorite destinations |
| Career class C | Class D Audi R8 e-tron started the Iceland Infection event |
| Required-car series | Class D Dodge Dart GT started the Renault Clio-restricted Temple event |
| Career class B | Dodge Dart GT started the Great Wall event |
| Custom race HUD | Recent starts displayed 5 laps and 40 racers |
| Career toggle | Disabling restored Clio-only/class B selection restrictions in the open garage; enabling restored alternative garage selection |

These observations are local evidence, not exhaustive coverage. Some checks preceded the English-only string changes. Translating the interface did not change native patch offsets or emitted hook code.

## Native multiplayer and social checks

A clean game/trainer build was exercised without Frida attached. All social identities and the HTTP directory used in these checks were owned local test fixtures.

| Check | Observation |
| --- | --- |
| Native pages | Added main, Room Options, Find by Name and Friends pages opened |
| Native keyboard | Lowercase entry produced literal `a`; Cancel restored the previous value |
| Incoming friend request | **Accept Selected** created a real HTTP friendship; service readback showed one friendship and zero pending requests |
| Friend presence | The friend row changed from Online to Offline after its presence TTL expired |
| Invitation inbox | An invitation sent by the owned local peer appeared in the native list |
| Invitation acceptance | Resolved the reference and explicitly reported that the native race adapter is pending and the player was not joined |
| Invitation dismissal | Removed the selected invitation from the real inbox |
| Name search | Native query `A` reached the HTTP directory and displayed zero matches |
| LAN join helper | **JOIN LAN BY NAME** opened the original `SelectRoom` browser; no match led to the bounded 900-tick timeout, and Back returned without a crash |
| Saved room name | Earlier instrumentation observed the actual `LocalLobbyHost` argument `Asphalt 8 room` with capacity eight |

These observations establish UI callbacks and local social-service interaction. The LAN helper is linked and invoked before the local filter, but no external room was available on the single test PC. Successful native joining, synchronized human racing, Internet reachability, 60-human grids and native race-host migration remain unverified. Default / 1–60 settings remain requested values; the native `ClassicLaps` hook has not been verified in a game race. The final rebuilt launcher kept asynchronous friend rows above the action buttons. Grid and laps changed from Default to 1, then Reset restored both to Default; saved-profile readback matched both states.

The latest recorded automated checks passed 19 directory/social Python tests, six native-resource tests, the room subprocess selftest, two successive lobby migrations and the invitation selftest. The managed native-bridge fixture additionally exercises real local HTTP operations with simulated shared memory, including DPAPI credential persistence, process changes and disposal. Lobby migration tests do not transfer the native race simulation.

The installed build also passed an isolated x86 fixture with 1,281 assertions, including 585 image relocations, native UI early-return guards, and execution of generated lap code for Default, 1, 60, invalid ranges and private/Classic guards. The fixture owns all executed memory and does not open the game. Final native action buttons no longer retain automatic selection; policy highlights follow requested ON/OFF values. The temporary local service was disconnected and stopped after testing.

## Isolated two-instance LAN and AI-policy checks

Two independent UWP package identities were prepared with separate Host/Client folders and app-container data. Resources were copied into each package, approximately 3.2 GB per instance. An initial resource junction setup was unreadable inside the app container and caused a startup access violation at `0x105B90E`; actual copies resolved the failure. Optional copied save seeds did not establish imported progress: the observed games started with fresh profiles, zero currency and one Dodge. Source game files and source user data were read-only inputs. Game assets remain outside this repository.

| Check | Observation |
| --- | --- |
| Explicit-PID harness | Signature, target-thread suspension and x86 context checks passed on both isolated processes; native Create/Join navigation worked without a crash |
| Native identities | Host and Client had different native player IDs |
| Native room creation | Host created a real eight-slot room and bound UDP 6529 |
| Multicast discovery | Client send to `224.0.0.1:6529` failed with error 10065 from Frida call-context `lastError` |
| Diagnostic correction | Earlier explicit `WSAGetLastError` readback of zero was invalid and was discarded |
| Temporary unicast diagnostic | Client recorded 21 successful sends to `127.0.0.1:6529`; Host observed no received discovery packet, and no native join occurred |
| Fill with AI bridge | Instrumented isolated-game ON added six AI and OFF removed them; manual original-control use updated the requested bit and returned success |
| Clean installed AI check | Without Frida, saved ON populated six AI; original Remove AI removed them, persisted Policy 4 and showed OFF in Room Options; restoring ON persisted Policy 20 and populated a fresh room |
| Latest compiled payload | Isolated native fixture passed 1,321 assertions and 605 relocations; nine social tests passed |

The unicast target was a temporary diagnostic, not a shipped fix. Microsoft's [Sockets and network isolation documentation](https://learn.microsoft.com/en-us/windows/uwp/networking/sockets) makes app-container policy a plausible explanation for same-machine communication failure; packet loss due to that policy was not directly established. No loopback exemption, firewall rule or other OS networking setting was changed. Manual exemption commands were declined, and both isolated test games were closed afterward.

The Fill with AI bridge calls the original callback at preferred image VA `0xE99D10` (RVA `0xA99D10`). The clean installed-build check used launcher SHA-256 `8be5f826869e5a43554ca45297f84ba1b55712bfeecc4d451903a62d21e8576e`, with no Frida session attached. The saved Grid 20, Laps 3 and room name ASPHALT8 were retained; the initial AI ON preference was restored. The HUD remained at 14,651 credits, 100 tokens, 2,000 fusion coins and 24 stars throughout these lobby checks. Launch remained disabled with one human. These observations do not establish successful native joining, peer reachability, synchronized racing, enforcement of all policies, 60-human grids or native host migration. Preparation and harness usage are documented in [the native LAN test guide](../tools/native-lan-test/README.md).

See [multiplayer status and test commands](MULTIPLAYER.md) for the implementation boundaries and reproducible checks.

## In-Game Mutators

The compiled native entry was exercised in a fixture for both player and bot targets. For the six non-airborne controls, the fixture checked 0×, 0.01×, 1×, 5× and 100× settings, plus disabled pass-through. These checks cover the calculation paths, including their numeric bounds; they do not substitute for observing every effect across game events.

Live measurements used the Audi R8 e-tron in an eight-car **Sector 8** race. The three hook stages ran for both target groups. The values below are raw native physics samples, not HUD units or vehicle specifications.

| Control | Player observation | Bot observation |
| --- | --- | --- |
| Acceleration | 0.2 → 0.3 at 1.5× | 1 → 1.2 at 1.2× |
| Top speed | 159.559 → 239.338 at 1.5× | Covered by the native fixture |
| Nitro power | 1310.65 → 2621.3 at 2× | 1250 → 1500 at 1.2× |
| Air rotation | 0.0574659 → 0.028733 at 0.5× | 0.25× scaling observed |
| Drift radius | Nonzero native input/output at 1× | Covered by the native fixture |
| Steering response | Nonzero native input/output at 1× | 0.9× scaling observed |
| Braking | Nonzero native input/output at 1× | 1 → 1.5 at 1.5× |

Additional checks:

- Player UI enable and reset updated the live mask and values. Reset readback showed a zero mask and seven 1.00× factors.
- A zero player top-speed setting produced a zero HUD speed with the intended 0.01 native cap.
- Air rotation ran at 100× for 341 application samples and at 0× for 104 samples; 1,536 inspected numeric samples remained finite.
- After disabling the effects, both masks were zero and the cached previous-rotation factors returned to 1.00×.
- No exception or game-process crash was observed during these checks.
- The final air-rotation dispatcher also passed an isolated native fixture for both targets at 0×, 0.01×, 0.25×, 1× and 100× over 30 updates each. Disabling restored the cached baseline. Ground-contact/reset cases at 0×, 0.01× and 100× restored that baseline without amplifying a simulated landing impulse.
- A bot acceleration setting of 1.10× survived a complete game/trainer restart and was reapplied with the player mask still disabled.

This is targeted evidence from one car/track setup and the native fixture. Other cars, tracks, complete-race endurance and combinations with 61 racers or older physics cheats still require separate regression testing. In particular, a nonzero 1× drift sample confirms execution of that path, not an exact turning radius in meters.

## Regression checklist for future changes

- Verify both enabled and disabled behavior, including a fresh process.
- Reopen every category and favorites to confirm state consistency.
- Start races on several layouts and with several owned cars.
- Test each custom race override independently, together and disabled.
- Exercise each player and bot mutator independently at low, neutral and high gains, then disable it and reset its group.
- Recheck mutator persistence after category changes and a fresh game/trainer process, including the separate player/bot values.
- Compare drift turning behavior, steering and braking across several cars, and test air rotation when taking off and landing at 0× and high gains.
- Check interactions between mutators, MAX/PRO overlays, older nitro/speed cheats and expanded 61-car grids.
- Exercise 61-racer starts and full race completion on multiple tracks.
- Compare MAX/PRO values with a saved baseline for cars with different PRO ceilings, including the Dodge; enter a race and restore the original tuning after disabling.
- Verify paid upgrades and purchases change the expected currency exactly once.
- Verify sale refunds match receipts, including insufficient funds and missing receipts.
- Recheck all Moto Blitz season and per-event gates.
- Treat remote-event and missing-catalogue paths as separately unsupported until observed working.

When reporting a crash, specify whether it happened during selection, loading, grid preparation, racing, upgrading or shutdown. Include the last actions and active tool list. Do not claim a whole category is verified from a successful signature check alone.

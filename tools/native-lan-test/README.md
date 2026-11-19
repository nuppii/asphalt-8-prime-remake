# Explicit-PID native LAN test harness

This opt-in x86 tool installs four allowlisted native hooks into one explicitly identified process. It never launches or registers a game, starts the production trainer, enumerates `Asphalt8` processes, acquires the trainer mutex, loads saved tools or issues wallet/gameplay commands. Production behavior is unchanged. Live installation passed on two isolated UWP test identities: signature, thread-suspension and context checks succeeded, followed by native Create/Join navigation without a crash. This did not establish a joined network session.

## Build and source-only checks

```powershell
.\tools\native-lan-test\Build.ps1
.\build\native-lan-test\HookHarness.exe --selftest
```

The selftest opens no target process. It checks SHA-256, mandatory explicit options, the dry-run default and unsupported-image rejection.

## Target inspection and opt-in installation

Use the independently built trainer assembly containing the matching native UI resource. Compute its SHA-256 before calling this tool. The hash is required so a different assembly cannot be silently loaded for reflection.

```powershell
$trainerHash=(Get-FileHash -LiteralPath '<built-trainer.exe>' -Algorithm SHA256).Hash
.\build\native-lan-test\HookHarness.exe --pid <explicit-pid> --module '<exact-package-path>\Asphalt8.exe' --trainer '<built-trainer.exe>' --trainer-sha256 $trainerHash
```

The default verifies the explicit process/module path, target lifetime, supported executable and all four live hook signatures without writing memory. Add `--apply` only for an intended test installation into a fresh, unhooked target.

The executable must be the supported original or exactly the repository's two offline patches. A patched image additionally requires its original backup with the known original SHA-256. The allowlist is **Wallet Editor** (shared native UI tick dispatcher), **MultiplayerEntry**, **MultiplayerTile** and **NativeRoomName**. The wallet command block remains zero; no balance changes or driver-name commands are queued. The harness loads `InjectionCatalog` builders and resources through reflection; it does not invoke the production engine or entry point.

All signatures are preflighted before allocation. Before publishing, the harness suspends the explicit target's threads until membership stabilizes, inspects each x86 instruction pointer and refuses any thread inside a hook site. Signatures are rechecked while paused. A failed publication attempts to restore original sites while threads remain paused. Every successfully suspended thread is resumed in `finally`, including access/context failures; a resume failure is reported as an error. Caves remain allocated until the target exits, including on failure, to avoid freeing possibly executing instructions. Restart the selected target to remove hooks. There is no hot-uninstall or reattach path. Installation prints its selected PID and shared-state address; it does not run a managed social bridge. The successful publication path was exercised on both isolated test instances; failure recovery is not established by that success.

## Two isolated UWP instances on one PC

The production installer registers one fixed development package identity. Registering that same identity from another directory does not establish an independently isolated second app. The separate preparation script creates `Host` and `Client` folders with identities `OfflineLab.Asphalt480LanHost` and `OfflineLab.Asphalt480LanClient`, independent app containers and real resource copies. Allow approximately 3.2 GB per copy, about 6.4 GB total. No game assets are included in this repository; an existing compatible installation and the original x86 VCLibs dependency are required.

```powershell
.\tools\native-lan-test\Prepare-Instances.ps1 `
  -GameDirectory '<existing-compatible-game>' `
  -OutputDirectory '<separate-test-parent>' `
  -Register
```

Omit `-Register` to prepare files only. New loose packages use `Add-AppxPackage -Register` without `-DisableDevelopmentMode`. Preparation reads the source game and any optional `-SeedLocalState` input; changes go to test folders and their separate LocalState. Seeding is an explicit, one-time copy, not a guarantee that the game will accept the old profile under a new identity. The observed test instances started with fresh profiles, zero currency and one Dodge despite copied seed data.

Use actual copies of `Data`, `Assets`, `IGBLib` and `IGPLib`. An initial junction-based resource setup was unreadable inside the new app container and produced a startup access violation at native address `0x105B90E`; real copies resolved that startup failure. The source game and original user data remain read-only inputs.

Verify each process's module path and PID after launching the intended identities. Run one harness process per target. Do not run the ordinary trainer concurrently: its injection engine selects `games[0]`, several commands require exactly one game, and its allocation dictionaries represent one PID. The trainer's global mutex also prevents two normal trainer instances.

Distinct package identities do not prove networking compatibility. In the live test, the identities had different native player IDs. Host created a real eight-slot room and bound UDP 6529. Client discovery to `224.0.0.1:6529` failed with error 10065, captured from Frida's call-context `lastError`. An earlier explicit `WSAGetLastError` probe incorrectly reported zero and is not accepted evidence. A temporary diagnostic unicast target `127.0.0.1:6529` produced 21 successful sends, but Host observed no received discovery packet and Client did not join. The diagnostic target is not a shipped networking fix.

Microsoft documents restrictions on same-machine communication between isolated Windows apps, including Winsock, in [Sockets and network isolation](https://learn.microsoft.com/en-us/windows/uwp/networking/sockets). App-container policy is a plausible explanation for the observed discovery failure, not a confirmed packet-drop diagnosis. No firewall, loopback-exemption or other OS networking setting was changed; the user declined manual exemption commands. Both isolated test games were closed afterward. Internet/NAT support, synchronized native racing, 60 humans and native host migration remain unverified.

For a windowless managed social bridge, the least invasive next step is to compile the existing `NativeLobbyBridge.cs` with a **test-only explicit-PID adapter**, isolated per-instance profile/credential storage and unsupported gameplay/name commands rejected. Its WinForms timer needs an STA message loop. Reusing production `EnsureWalletEditor` or `Toggle` would lose explicit-PID isolation. This extension has not been implemented or launched by this harness.

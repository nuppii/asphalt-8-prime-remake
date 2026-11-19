# Architecture and recovery

## Launcher and overlay

`Trainer.cs` contains the WinForms launcher, tray icon, global Insert hotkey, categorized overlay, favorites and persistent tool state. The launcher activates the registered offline Windows app identity. A named mutex prevents duplicate trainer instances.

The package identity is `YoanRochat.Asphalt8Airborne480Offline`. The launcher targets the application ID `YoanRochat.Asphalt8Airborne480Offline_0pp20fcewvvtj!App`. The patcher preserves the original manifest publisher because it determines the package family suffix.

## Patch engine

`CheatTableForm.cs` contains direct patch definitions, native injection definitions and emitted x86 machine code. The main interface integrates these tools into their functional sections. The legacy direct-patch form is retained in source.

Direct patches verify original or expected replacement bytes before writing. Injection tools allocate remote code, emit the hook and retain restoration data. These addresses and instruction lengths are specific to the supported executable. Never substitute another release without fresh reverse-engineering and validation.

## Race grid

`GridSpawn.c` implements the extended grid preparation logic. `Build-Trainer.ps1` compiles and links it as an x86 native image, then embeds it as `GridSpawn.x86` in the managed executable. `GridSpawnHook.cs` loads and relocates that image into the game process. Lap and racer settings have independent runtime slots and restoration records.

## In-game physics mutators

`MutatorEngine.cs` loads the freestanding x86 image built from `MutatorPhysics.c`, embedded as `MutatorPhysics.x86`. The PE loader checks image bounds and relocations before installing three version-specific hooks for the complete vehicle update, engine outputs and drift assistance. Installation checks the original byte signatures, suspends the game's threads, verifies that no instruction pointer occupies a replaced range, and rechecks signatures before writing. Failed installation rolls back changed hooks before resuming the threads.

The remote state begins with the game module and original-update trampoline addresses, followed by two aligned 32-bit masks and two sets of seven float multipliers. Player and bot classification chooses the appropriate mask and values for each car. Diagnostics record stage calls, applied masks, before/after values and application counts. These runtime records are independent of the saved garage tuning.

Engine-stage gains modify transient acceleration, speed-cap, nitro-force, steering and braking results. The drift stage adjusts assisted yaw inversely with the radius factor. The complete-update wrapper keeps a per-car cache for airborne angular velocity, removes the previous gain before the next update and applies the current gain afterward. This prevents repeated multiplication from compounding across frames. Per-car caches also allow disabling or resetting the effect to restore the unscaled behavior.

The UI stores desired settings separately from successfully applied states. `mutators.txt` uses invariant-culture rows of `target|parameter|enabled|multiplier`, with independent player and bot entries. A new game process restores enabled settings; a closed game clears the displayed applied states. The defaults are disabled with multipliers of 1.00×.

Disabling clears the relevant mask bit. A group reset clears its mask and writes seven 1.00× values. The hooks remain allocated for the life of the game process, avoiding code-memory release while a game thread could still be executing it. No permanent executable or save-data changes are needed for these mutators.

## Local command processing

Wallet, purchase, receipt, sale and driver-name operations are queued for the game's own processing path. The overlay polls for confirmation instead of treating submission as success. The career refresh hook rebuilds the selector on the game's UI thread when the override changes.

## Native multiplayer controls

`NativeMultiplayer.cs` reuses the original Local Wi-Fi state. `NativeLobbyUi.c` creates buttons and keyboard widgets through the game's GIN factories and connects their original click signals. It runs on the game UI thread through the existing wallet dispatcher; a second competing hook is not installed at that update entry. `NativeLobbyHook.cs` embeds and relocates the freestanding x86 image, preserving the calling frame and floating-point state.

`NativeLobbyUi.h` defines the 3,784-byte, versioned shared-memory contract. `NativeLobbyBridge.cs` reads explicit user requests and performs HTTP work in the background. Counts are published after list strings; stale process/profile completions are discarded. Requested settings live in `native-profile.json`. Social credentials are separate, protected with Windows DPAPI for the current user and directory URL. Password entry remains session-only. Clipboard access requires an explicit native button action.

`NativeLanBridge.c` opens the original LAN browser and matches an exact room name against native discovery results. A unique result uses the original Join callback and its original eligibility checks. Duplicate names leave selection to the browser. Shared pointers are retained for the callback, and discovered room pointers do not survive a polling call. A dispatched callback does not establish a connection.

`NativeLobbyPolicy.c` connects the saved AI-fill bit to the original private Wi-Fi host control. It checks screen, lobby and room types, host ownership, ready state and the real enabled button before invoking the original callback with a retained shared pair. Saved settings win on room entry; subsequent use of the original control updates that bit through the existing managed save request. Each edit is dispatched once, avoiding repeated toggles if the native callback rejects it. Other policy bits are untouched. This adapter changes lobby AI fill, not the native race capacity or Launch eligibility.

`tools/native-ui` generates two local BGIN resources from installed assets; the installer creates and copies them. The repository contains the parser and builder, not complete game resources. The optional directory/social service and experimental TCP room-control service remain separate from native race synchronization. See [multiplayer status](MULTIPLAYER.md) for the unsupported boundaries.

## Startup patch

`scripts/patch_offline.py` requires the supported original SHA-256 and expected byte signatures. It replaces the remote compliance-variable lookup block and redirects the unavailable startup age-gating flow to the normal initialization path. It updates a dedicated copy's package identity and retains original package metadata in `_offline_patch_backup` for recovery.

## Backups and recovery

Before experimenting, close the game and copy the offline package's full `LocalState` directory to a location outside the game directory. Its usual location is:

```text
%LOCALAPPDATA%/Packages/YoanRochat.Asphalt8Airborne480Offline_0pp20fcewvvtj/LocalState
```

Keep `_offline_patch_backup/tools/car-receipts.txt` with the associated save if you need later purchase refunds. Avoid mixing receipts from a different save.

To stop applying saved tools, close the game and trainer, preserve copies of `active-tools.txt` and `mutators.txt`, then move those files outside the `tools` folder before restarting. In the running trainer, the mutator group reset buttons provide the same return to disabled, 1.00× defaults for their respective groups. This only stops runtime overlays. To undo persistent transactions, restore your own matching save backup while the game is closed.

To restore the selected game copy's original executable and manifest, close all associated processes and copy `Asphalt8.exe.original` and `AppxManifest.xml.original` from `_offline_patch_backup` back to their original filenames. Package registration may need to be updated for that restored identity. Use the untouched source installation when possible. Keep backups until the restored copy has been verified.

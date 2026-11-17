# Architecture and recovery

## Launcher and overlay

`Trainer.cs` contains the WinForms launcher, tray icon, global Insert hotkey, categorized overlay, favorites and persistent tool state. The launcher activates the registered offline Windows app identity. A named mutex prevents duplicate trainer instances.

The package identity is `YoanRochat.Asphalt8Airborne480Offline`. The launcher targets the application ID `YoanRochat.Asphalt8Airborne480Offline_0pp20fcewvvtj!App`. The patcher preserves the original manifest publisher because it determines the package family suffix.

## Patch engine

`CheatTableForm.cs` contains direct patch definitions, native injection definitions and emitted x86 machine code. The main interface integrates these tools into their functional sections. The legacy direct-patch form is retained in source.

Direct patches verify original or expected replacement bytes before writing. Injection tools allocate remote code, emit the hook and retain restoration data. These addresses and instruction lengths are specific to the supported executable. Never substitute another release without fresh reverse-engineering and validation.

## Race grid

`GridSpawn.c` implements the extended grid preparation logic. `Build-Trainer.ps1` compiles and links it as an x86 native image, then embeds it as `GridSpawn.x86` in the managed executable. `GridSpawnHook.cs` loads and relocates that image into the game process. Lap and racer settings have independent runtime slots and restoration records.

## Local command processing

Wallet, purchase, receipt, sale and driver-name operations are queued for the game's own processing path. The overlay polls for confirmation instead of treating submission as success. The career refresh hook rebuilds the selector on the game's UI thread when the override changes.

## Startup patch

`scripts/patch_offline.py` requires the supported original SHA-256 and expected byte signatures. It replaces the remote compliance-variable lookup block and redirects the unavailable startup age-gating flow to the normal initialization path. It updates a dedicated copy's package identity and retains original package metadata in `_offline_patch_backup` for recovery.

## Backups and recovery

Before experimenting, close the game and copy the offline package's full `LocalState` directory to a location outside the game directory. Its usual location is:

```text
%LOCALAPPDATA%/Packages/YoanRochat.Asphalt8Airborne480Offline_0pp20fcewvvtj/LocalState
```

Keep `_offline_patch_backup/tools/car-receipts.txt` with the associated save if you need later purchase refunds. Avoid mixing receipts from a different save.

To stop applying saved tools, close the game and trainer, preserve a copy of `active-tools.txt`, then move it outside the `tools` folder before restarting. This only stops runtime overlays. To undo persistent transactions, restore your own matching save backup while the game is closed.

To restore the selected game copy's original executable and manifest, close all associated processes and copy `Asphalt8.exe.original` and `AppxManifest.xml.original` from `_offline_patch_backup` back to their original filenames. Package registration may need to be updated for that restored identity. Use the untouched source installation when possible. Keep backups until the restored copy has been verified.

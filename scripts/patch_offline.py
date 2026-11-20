from pathlib import Path
import hashlib
import shutil
import argparse


parser = argparse.ArgumentParser(description="Patch a dedicated local Asphalt 8 build 4.8.0.7 copy for offline startup.")
parser.add_argument("--game-dir", required=True, type=Path, help="Complete compatible game copy to patch")
CLONE = parser.parse_args().game_dir.resolve()
EXE = CLONE / "Asphalt8.exe"
MANIFEST = CLONE / "AppxManifest.xml"
BACKUP = CLONE / "_offline_patch_backup"

ORIGINAL_SHA256 = "6a5d3933f92809297631f1299c9906ec40fbb9d585a8c77115f321bfe733134c"
PATCH_OFFSET = 0xBBAE1A
PATCH_LENGTH = 0x40
EXPECTED_PREFIX = bytes.fromhex("68 5c aa e1 01 8b ce e8")
FORCE_TRUE = bytes.fromhex("c6 83 a5 00 00 00 01")
AGE_GATE_BRANCH_OFFSET = 0x4AA48B
EXPECTED_AGE_GATE_BRANCH = bytes.fromhex("75 5e")
SKIP_AGE_GATE_BRANCH = bytes.fromhex("eb 5e")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


if not EXE.is_file() or not MANIFEST.is_file():
    raise SystemExit(f"Incomplete game copy: {CLONE}")

BACKUP.mkdir(exist_ok=True)
exe_backup = BACKUP / "Asphalt8.exe.original"
manifest_backup = BACKUP / "AppxManifest.xml.original"

if not exe_backup.exists():
    current_hash = sha256(EXE)
    if current_hash != ORIGINAL_SHA256:
        raise SystemExit(f"Unexpected original executable: {current_hash}")
    shutil.copy2(EXE, exe_backup)

if not manifest_backup.exists():
    shutil.copy2(MANIFEST, manifest_backup)

if sha256(exe_backup) != ORIGINAL_SHA256:
    raise SystemExit("Original backup hash mismatch; refusing to patch")

binary = bytearray(exe_backup.read_bytes())
if binary[PATCH_OFFSET:PATCH_OFFSET + len(EXPECTED_PREFIX)] != EXPECTED_PREFIX:
    raise SystemExit("Compliance routine signature mismatch")

# The original 64-byte block queries the remote variable
# "disable_compliancy_lib" and stores it at [config + 0xA5]. Replace only that
# block with: mov byte ptr [ebx+0xA5], 1; nops.
binary[PATCH_OFFSET:PATCH_OFFSET + PATCH_LENGTH] = (
    FORCE_TRUE + bytes([0x90]) * (PATCH_LENGTH - len(FORCE_TRUE))
)

# Startup normally creates GS_AgeGating when the local identity field at
# +0xAFC is unset (-1). Turn the conditional branch into an unconditional jump
# to the normal post-gate initialization path.
if binary[AGE_GATE_BRANCH_OFFSET:AGE_GATE_BRANCH_OFFSET + 2] != EXPECTED_AGE_GATE_BRANCH:
    raise SystemExit("Age-gating branch signature mismatch")
binary[AGE_GATE_BRANCH_OFFSET:AGE_GATE_BRANCH_OFFSET + 2] = SKIP_AGE_GATE_BRANCH
EXE.write_bytes(binary)

manifest = manifest_backup.read_text(encoding="utf-8")
manifest = manifest.replace(
    'Name="GAMELOFTSA.Asphalt8Airborne"',
    'Name="YoanRochat.Asphalt8Airborne480Offline"',
    1,
)
manifest = manifest.replace("ms-resource:AppName", "Asphalt 8: Airborne 4.8")
manifest = manifest.replace("ms-resource:ShortName", "Asphalt 8")
manifest = manifest.replace("Asphalt 8: Airborne 4.8 Offline", "Asphalt 8: Airborne 4.8")
# Windows requires a new registration revision to refresh the visible title.
# The package family and the supported Asphalt8.exe build stay the same.
manifest = manifest.replace('Version="4.8.0.7"', 'Version="4.8.0.8"', 1)
MANIFEST.write_text(manifest, encoding="utf-8", newline="\n")

# Loose development registration must not consume the original package's
# now-stale signature/catalog metadata. Keep it inside the clone for recovery.
for relative in ("AppxSignature.p7x", "AppxBlockMap.xml", "AppxMetadata"):
    source = CLONE / relative
    destination = BACKUP / relative
    if source.exists() and not destination.exists():
        shutil.move(str(source), str(destination))

print(f"original_sha256={ORIGINAL_SHA256}")
print(f"patched_sha256={sha256(EXE)}")
print(f"patch_offset=0x{PATCH_OFFSET:x}")
print(f"age_gate_branch_offset=0x{AGE_GATE_BRANCH_OFFSET:x}")
print(f"clone={CLONE}")

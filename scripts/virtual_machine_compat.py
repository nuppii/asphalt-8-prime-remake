"""Support hardware queries in the legacy game on Hyper-V guests."""

from hashlib import sha256
import os
import struct
import sys
import winreg


TOOLKIT_SHA256 = "dbc4fc0edb952d805378d638baf62ecf1a057f36ed86f1dcbd69e54cadab30ef"
BIOS_KEY = r"HARDWARE\DESCRIPTION\System\BIOS"


def is_hyper_v_guest():
    # Windows Sandbox can hide the host BIOS keys from its disposable account.
    if os.environ.get("USERNAME", "").casefold() == "wdagutilityaccount":
        return True
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, BIOS_KEY) as bios:
        company = winreg.QueryValueEx(bios, "SystemManufacturer")[0]
        product = winreg.QueryValueEx(bios, "SystemProductName")[0]
    return company == "Microsoft Corporation" and product == "Virtual Machine"


def prepare_for_virtual_machine(layout):
    try:
        guest = is_hyper_v_guest()
    except FileNotFoundError:
        # Some sandbox images omit the BIOS registry key entirely.
        guest = True
    except OSError:
        guest = False
    if not guest:
        return

    toolkit = layout / "WCPToolkit.dll"
    binary = bytearray(toolkit.read_bytes())
    if sha256(binary).hexdigest() != TOOLKIT_SHA256:
        raise ValueError("The game toolkit has an unsupported build for Hyper-V compatibility.")

    # WinRT inventory in Hyper-V can be unavailable. Return the actual OS
    # version and use the title's normal no-accelerometer behavior there.
    major, minor = sys.getwindowsversion().platform_version[:2]
    version = f"{major}.{minor}".encode("ascii")
    if len(version) > 4:
        raise ValueError("The guest Windows version does not fit the legacy compatibility path.")
    # MSVC x86 std::string result: object pointer, inline buffer, size, and capacity.
    version_result = bytearray(b"\x8b\x44\x24\x04")
    for index, character in enumerate(version + b"\0"):
        version_result.extend((0xC6, 0x40, index, character))
    version_result.extend(b"\xc7\x40\x10" + struct.pack("<I", len(version)))
    version_result.extend(b"\xc7\x40\x14\x0f\x00\x00\x00\xc2\x04\x00")
    binary[0x69610:0x69610 + len(version_result)] = version_result
    # Clear the two base-relocation types for overwritten original instructions.
    struct.pack_into("<HH", binary, 0x100F56, 0x0216, 0x0225)
    binary[0x686B0] = 0xC3
    binary[0x5C2D0:0x5C2D3] = b"\x31\xc0\xc3"
    toolkit.write_bytes(binary)

"""Write an AppX using the Windows App Package API already on the PC."""

from contextlib import contextmanager
import ctypes as ct
from pathlib import Path
from uuid import UUID


POINTER = ct.c_void_p
HRESULT = ct.c_long
WIDE = ct.c_wchar_p
DWORD = ct.c_ulong
FACTORY_CLASS = UUID("5842a140-ff9f-4166-8f5c-62f5b7b0c781").bytes_le
FACTORY_INTERFACE = UUID("beb94909-e451-438b-b5a7-d79e767b75d8").bytes_le
SHA256_URI = "http://www.w3.org/2001/04/xmlenc#sha256"
CONTENT_TYPES = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
                 ".xml": "application/xml", ".json": "application/json", ".txt": "text/plain"}
PACKAGE_METADATA = {"appxmanifest.xml", "appxblockmap.xml", "appxsignature.p7x", "[content_types].xml",
                    "appxmetadata", "microsoft.system.package.metadata", "_offline_patch_backup",
                    "asphalt 8 prime remake.exe", "offline-build.sha256"}


class PackageSettings(ct.Structure):
    _fields_ = [("force_zip32", ct.c_int), ("hash_uri", POINTER)]


def check(result, operation):
    if result < 0:
        raise OSError(f"{operation} failed (0x{result & 0xffffffff:08X}).")


def method(instance, index, result, *arguments):
    vtable = ct.cast(instance, ct.POINTER(ct.POINTER(POINTER))).contents
    return ct.WINFUNCTYPE(result, POINTER, *arguments)(vtable[index])


def invoke(instance, index, operation, types, *values):
    check(method(instance, index, HRESULT, *types)(instance, *values), operation)


@contextmanager
def com_output():
    pointer = POINTER()
    try:
        yield pointer
    finally:
        if pointer:
            method(pointer, 2, DWORD)(pointer)


@contextmanager
def file_stream(path, writable=False):
    stream = ct.WinDLL("shlwapi").SHCreateStreamOnFileEx
    stream.argtypes = (WIDE, DWORD, DWORD, ct.c_int, POINTER, ct.POINTER(POINTER))
    stream.restype = HRESULT
    with com_output() as result:
        mode = 0x1011 if writable else 0x20
        check(stream(str(path), mode, 0x80, writable, None, ct.byref(result)), f"Open {Path(path).name}")
        yield result


def write_appx(layout, manifest, output):
    ole = ct.WinDLL("ole32")
    ole.CoInitializeEx.argtypes = (POINTER, DWORD)
    ole.CoInitializeEx.restype = HRESULT
    ole.CoCreateInstance.argtypes = (POINTER, POINTER, DWORD, POINTER, ct.POINTER(POINTER))
    ole.CoCreateInstance.restype = HRESULT
    create_uri = ct.WinDLL("urlmon").CreateUri
    create_uri.argtypes = (WIDE, DWORD, ct.c_size_t, ct.POINTER(POINTER))
    create_uri.restype = HRESULT

    check(ole.CoInitializeEx(None, 0), "Initialize AppX packaging")
    try:
        with com_output() as factory, com_output() as uri, file_stream(output, writable=True) as destination:
            check(ole.CoCreateInstance(ct.create_string_buffer(FACTORY_CLASS), None, 1,
                                       ct.create_string_buffer(FACTORY_INTERFACE), ct.byref(factory)),
                  "Create AppX package factory")
            check(create_uri(SHA256_URI, 0, 0, ct.byref(uri)), "Create block map hash URI")
            settings = PackageSettings(False, uri)
            with com_output() as writer:
                invoke(factory, 3, "Create AppX package writer",
                       (POINTER, ct.POINTER(PackageSettings), ct.POINTER(POINTER)),
                       destination, ct.byref(settings), ct.byref(writer))
                for path in layout.rglob("*"):
                    relative = path.relative_to(layout)
                    if not path.is_file() or relative.parts[0].lower() in PACKAGE_METADATA:
                        continue
                    content_type = CONTENT_TYPES.get(path.suffix.lower(), "application/octet-stream")
                    with file_stream(path) as source:
                        invoke(writer, 3, f"Package {relative}", (WIDE, WIDE, ct.c_int, POINTER),
                               str(relative), content_type, 0, source)
                with file_stream(manifest) as manifest_stream:
                    invoke(writer, 4, "Finalize AppX package", (POINTER,), manifest_stream)
    finally:
        ole.CoUninitialize()

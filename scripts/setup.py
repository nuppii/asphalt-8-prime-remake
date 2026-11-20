"""Offline, self-contained Windows setup for a user-supplied 4.8.0.7 package."""
import argparse
import os
from pathlib import Path, PureWindowsPath
import shutil
import stat
import subprocess
import sys
import threading
from xml.etree import ElementTree
from zipfile import BadZipFile, ZipFile

from bgin import Document
from build_native_ui import build_keyboard_document, build_panel_document, build_roster_document
from deploy_appx import RUNTIME_FILE, install_clean
from patch_offline import PACKAGE_NAME, PACKAGE_VERSION, PROJECT_NAME, brand_manifest, patch_game, sha256


LAUNCHER = f"{PROJECT_NAME}.exe"
LEGACY_LAUNCHER = "Asphalt 8 Airborne 4.8.exe"
PAYLOAD = Path(getattr(sys, "_MEIPASS", Path(__file__).parent)) / "offline-launcher.exe"
RUNTIME_PAYLOAD = PAYLOAD.with_name(RUNTIME_FILE)
BUILD_LOCK = "offline-build.sha256"
REGISTRATION = r"""
$ErrorActionPreference = 'Stop'
$root = $env:ASPHALT_OFFLINE_SETUP_DESTINATION
if (Get-Process Asphalt8 -ErrorAction SilentlyContinue) {
    throw 'Close Asphalt 8 before registering this installation.'
}
if (!(Get-AppxPackage -Name Microsoft.VCLibs.120.00 | Where-Object {
    $_.Architecture -eq 'X86' -and $_.Version -ge [version]'12.0.21005.1'
})) {
    throw 'The Microsoft.VCLibs.120.00 x86 package at version 12.0.21005.1 or later is required.'
}
$installed = Get-AppxPackage -Name YoanRochat.Asphalt8Airborne480Offline
if ($installed | Where-Object { $_.InstallLocation -ne $root }) {
    throw 'An offline copy is registered in another folder. Select that copy to update it.'
}
if (!$installed -or $installed.Version -ne [version]$env:ASPHALT_OFFLINE_SETUP_VERSION) {
    Add-AppxPackage -Register (Join-Path $root 'AppxManifest.xml')
}
"""


def unpack(source, target):
    """Extract only ordinary relative Windows paths from the supplied package."""
    seen = set()
    with ZipFile(source) as archive:
        for entry in archive.infolist():
            item = PureWindowsPath(entry.filename)
            parts = item.parts
            if (item.anchor or not parts or any(
                part in (".", "..") or part.endswith((" ", ".")) or
                any(ord(c) < 32 or c in '<>:"|?*' for c in part) or
                PureWindowsPath(part).is_reserved() for part in parts
            ) or stat.S_ISLNK(entry.external_attr >> 16)):
                raise ValueError(f"Unsupported package path: {entry.filename!r}")
            path = target.joinpath(*parts)
            key = tuple(part.casefold() for part in parts)
            if key in seen:
                raise ValueError(f"Duplicate package path: {entry.filename!r}")
            seen.add(key)
            if entry.is_dir():
                path.mkdir(parents=True, exist_ok=True)
                continue
            path.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as original, path.open("xb") as destination:
                shutil.copyfileobj(original, destination, 1024 * 1024)


def build_resources(folder):
    gui = folder / "Data" / "gui"

    def read(name):
        return Document.parse((gui / f"{name}.bgin").read_bytes())

    source, controls = read("multiplayer_menu"), read("windows_contents")
    for name, document in (
        ("offline_lobby_ui", build_keyboard_document(source, controls)),
        ("offline_lobby_panel", build_panel_document(source, read("generic_templates"), read("cmp_special_events"))),
        ("offline_lobby_roster", build_roster_document(source, controls)),
    ):
        (gui / f"{name}.bgin").write_bytes(document.encode())


def verify_prepared(folder, versions=(PACKAGE_VERSION,)):
    identity = ElementTree.parse(folder / "AppxManifest.xml").getroot().find("{*}Identity")
    if identity is None or identity.get("Name") != PACKAGE_NAME or identity.get("Version") not in versions:
        raise ValueError("The selected folder is not a prepared 4.8.0.7 installation.")
    if (folder / BUILD_LOCK).read_text(encoding="ascii").strip() != sha256(folder / "Asphalt8.exe"):
        raise ValueError("The prepared game executable does not match its installation checksum.")


def update_branding(folder):
    verify_prepared(folder, ("4.8.0.9", PACKAGE_VERSION))
    manifest = folder / "AppxManifest.xml"
    current = manifest.read_text(encoding="utf-8")
    branded = brand_manifest(current)
    if current != branded or not (folder / LAUNCHER).is_file():
        shutil.copy2(PAYLOAD, folder / LAUNCHER)
    if current != branded:
        manifest.write_text(branded, encoding="utf-8", newline="\n")


def prepare(source, target):
    if not PAYLOAD.is_file():
        raise ValueError("The built launcher is missing from setup.")
    source, target = source.resolve(), target.resolve()
    if source == target or source in target.parents:
        raise ValueError("Choose an installation location outside the source.")
    if target.exists():
        raise ValueError("Choose a new installation folder. An existing folder will not be overwritten.")
    if not source.is_dir() and (not source.is_file() or source.suffix.lower() not in (".appx", ".msix")):
        raise ValueError("Select an original .appx or .msix package, or an extracted original game folder.")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.mkdir()
    try:
        if source.is_dir():
            shutil.copytree(source, target, dirs_exist_ok=True,
                            ignore=shutil.ignore_patterns("_offline_patch_backup", "LocalState"))
        else:
            unpack(source, target)
        for required in ("Asphalt8.winmd", "resources.pri", "Assets", "Data", "IGBLib", "IGPLib"):
            if not (target / required).exists():
                raise ValueError(f"Incomplete game package. Missing {required}.")
        digest = patch_game(target)
        build_resources(target)
        shutil.copy2(PAYLOAD, target / LAUNCHER)
        (target / BUILD_LOCK).write_text(digest + "\n", encoding="ascii")
    except Exception:
        # Only the directory created exclusively by this invocation can be rolled back.
        if target.resolve() == target:
            shutil.rmtree(target, ignore_errors=True)
        raise


def register(target):
    verify_prepared(target)
    if not (target / LAUNCHER).is_file():
        raise ValueError("The Prime Remake launcher is missing. Run setup again to repair it.")
    power_shell = Path(os.environ["SystemRoot"]) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    env = dict(os.environ, ASPHALT_OFFLINE_SETUP_DESTINATION=str(target.resolve()),
               ASPHALT_OFFLINE_SETUP_VERSION=PACKAGE_VERSION)
    result = subprocess.run([str(power_shell), "-NoProfile", "-NonInteractive", "-Command", REGISTRATION],
                            env=env, capture_output=True, text=True, errors="replace",
                            creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        detail = (result.stderr or result.stdout).strip()
        raise ValueError(f"Windows registration failed. The prepared copy remains in {target}.\n\n{detail}")


def show_setup():
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    window = tk.Tk()
    window.title(f"{PROJECT_NAME} Setup")
    window.geometry("605x310")
    window.resizable(False, False)
    source = tk.StringVar()
    runtime = tk.StringVar()
    status = tk.StringVar(value="Choose your Windows 4.8.0.7 package or extracted game files.")
    frame = ttk.Frame(window, padding=22)
    frame.pack(fill="both", expand=True)
    ttk.Label(frame, text=f"{PROJECT_NAME} Setup", font=("Segoe UI", 17)).pack(anchor="w")
    ttk.Label(frame, textvariable=status, wraplength=520).pack(anchor="w", pady=10)
    ttk.Entry(frame, textvariable=source, state="readonly").pack(fill="x")
    choices = ttk.Frame(frame)
    choices.pack(fill="x", pady=10)

    def choose(package):
        selected = (filedialog.askopenfilename(filetypes=[("Windows package", "*.appx *.msix")])
                    if package else filedialog.askdirectory())
        if selected:
            source.set(selected)

    package_button = ttk.Button(choices, text="Choose a package", command=lambda: choose(True))
    folder_button = ttk.Button(choices, text="Choose a game folder", command=lambda: choose(False))
    package_button.pack(side="left")
    folder_button.pack(side="left", padx=8)
    def choose_runtime():
        selected = filedialog.askopenfilename(title="Choose the Microsoft VCLibs 120 x86 package",
                                               filetypes=[("Windows package", "*.appx *.msix")])
        if selected:
            runtime.set(selected)
            status.set(f"Runtime selected: {Path(selected).name}")

    runtime_button = ttk.Button(choices, text="Choose runtime", command=choose_runtime)
    runtime_button.pack(side="left")
    progress = ttk.Progressbar(frame, mode="indeterminate")
    progress.pack(fill="x", pady=5)
    installing = False

    def close():
        if not installing:
            window.destroy()

    window.protocol("WM_DELETE_WINDOW", close)

    def finished(error, target):
        nonlocal installing
        installing = False
        progress.stop()
        for button in (package_button, folder_button, runtime_button, install_button):
            button.configure(state="normal")
        status.set("Installation stopped." if error else f"Installed in {target}.")
        if error:
            messagebox.showerror("Setup", str(error), parent=window)
        elif messagebox.askyesno("Setup", "Installation complete. Launch the game now?", parent=window):
            os.startfile(target / LAUNCHER)
            window.destroy()

    def install():
        if not source.get():
            messagebox.showinfo("Setup", "Choose a game package or folder first.", parent=window)
            return
        selected = Path(source.get())
        if (selected / BUILD_LOCK).is_file():
            start(selected, selected)
            return
        parent = filedialog.askdirectory(title="Choose where to create the offline installation")
        if not parent:
            return
        target = Path(parent) / PROJECT_NAME
        start(selected, target)

    def start(selected, target):
        nonlocal installing
        installing = True
        selected_runtime = Path(runtime.get()) if runtime.get() else None
        for button in (package_button, folder_button, runtime_button, install_button):
            button.configure(state="disabled")
        status.set("Preparing and registering the offline copy. This can take a few minutes.")
        progress.start(10)

        def work():
            try:
                if selected == target:
                    update_branding(target)
                    register(target)
                    legacy = target / LEGACY_LAUNCHER
                    backup = target / "_offline_patch_backup" / LEGACY_LAUNCHER
                    if legacy.is_file() and not backup.exists():
                        try:
                            legacy.rename(backup)
                        except OSError:
                            pass
                else:
                    install_clean(selected, target, prepare, LAUNCHER, find_runtime(selected, selected_runtime))
            except (OSError, ValueError) as error:
                window.after(0, finished, error, target)
            except Exception as error:
                window.after(0, finished, ValueError(f"Setup could not complete: {error}"), target)
            else:
                window.after(0, finished, None, target)

        threading.Thread(target=work, daemon=True).start()

    install_button = ttk.Button(frame, text="Install", command=install)
    install_button.pack(anchor="e")
    window.mainloop()


def find_runtime(source, specified):
    if specified:
        return specified.resolve()
    folders = [source if source.is_dir() else source.parent, source.parent]
    return next((path.resolve() for path in [RUNTIME_PAYLOAD, *(folder / RUNTIME_FILE for folder in folders)]
                 if path.is_file()), None)


if __name__ == "__main__":
    if len(sys.argv) == 1:
        show_setup()
    else:
        parser = argparse.ArgumentParser(description=__doc__)
        modes = parser.add_mutually_exclusive_group(required=True)
        modes.add_argument("--prepare-only", nargs=2, type=Path, metavar=("SOURCE", "DESTINATION"))
        modes.add_argument("--install-clean", nargs=2, type=Path, metavar=("SOURCE", "DESTINATION"))
        parser.add_argument("--runtime", type=Path, help="Supply the signed Microsoft VCLibs 120 x86 package.")
        parser.add_argument("--error-log", type=Path, help="Write a preparation error to this file.")
        args = parser.parse_args()
        try:
            if args.prepare_only:
                prepare(*args.prepare_only)
            else:
                source, target = args.install_clean
                install_clean(source, target, prepare, LAUNCHER, find_runtime(source, args.runtime))
        except (OSError, ValueError, BadZipFile, ElementTree.ParseError) as error:
            if args.error_log:
                args.error_log.write_text(f"{type(error).__name__}: {error}\n", encoding="utf-8")
            if sys.stderr is not None:
                print(error, file=sys.stderr)
            sys.exit(1)

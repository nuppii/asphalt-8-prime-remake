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
from patch_offline import patch_game, sha256


LAUNCHER = "Asphalt 8 Airborne 4.8.exe"
PACKAGE_NAME = "YoanRochat.Asphalt8Airborne480Offline"
PAYLOAD = Path(getattr(sys, "_MEIPASS", Path(__file__).parent)) / "offline-launcher.exe"
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
if (!$installed -or $installed.Version -ne [version]'4.8.0.9') {
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


def verify_prepared(folder):
    identity = ElementTree.parse(folder / "AppxManifest.xml").getroot().find("{*}Identity")
    if identity is None or identity.get("Name") != PACKAGE_NAME or identity.get("Version") != "4.8.0.9":
        raise ValueError("The selected folder is not a prepared 4.8.0.7 installation.")
    if (not (folder / LAUNCHER).is_file() or
        (folder / BUILD_LOCK).read_text(encoding="ascii").strip() != sha256(folder / "Asphalt8.exe")):
        raise ValueError("The prepared game executable does not match its installation checksum.")


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
    power_shell = Path(os.environ["SystemRoot"]) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    env = dict(os.environ, ASPHALT_OFFLINE_SETUP_DESTINATION=str(target.resolve()))
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
    window.title("Asphalt 8 Offline Lab Setup")
    window.geometry("570x270")
    window.resizable(False, False)
    source = tk.StringVar()
    status = tk.StringVar(value="Choose your Windows 4.8.0.7 package or extracted game files.")
    frame = ttk.Frame(window, padding=22)
    frame.pack(fill="both", expand=True)
    ttk.Label(frame, text="Asphalt 8 Offline Lab Setup", font=("Segoe UI", 17)).pack(anchor="w")
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
        for button in (package_button, folder_button, install_button):
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
        target = Path(parent) / "Asphalt 8 Offline Lab"
        start(selected, target)

    def start(selected, target):
        nonlocal installing
        installing = True
        for button in (package_button, folder_button, install_button):
            button.configure(state="disabled")
        status.set("Preparing and registering the offline copy. This can take a few minutes.")
        progress.start(10)

        def work():
            try:
                if selected != target:
                    prepare(selected, target)
                register(target)
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


if __name__ == "__main__":
    if len(sys.argv) == 1:
        show_setup()
    else:
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument("--prepare-only", nargs=2, type=Path, metavar=("SOURCE", "DESTINATION"))
        parser.add_argument("--error-log", type=Path, help="Write a preparation error to this file.")
        args = parser.parse_args()
        if args.prepare_only:
            try:
                prepare(*args.prepare_only)
            except (OSError, ValueError, BadZipFile, ElementTree.ParseError) as error:
                if args.error_log:
                    args.error_log.write_text(f"{type(error).__name__}: {error}\n", encoding="utf-8")
                if sys.stderr is not None:
                    print(error, file=sys.stderr)
                sys.exit(1)

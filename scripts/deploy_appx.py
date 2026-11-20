"""Install a locally generated game package with Windows 11 AppX deployment."""

import base64
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from xml.etree import ElementTree
from zipfile import ZipFile

from appx_writer import write_appx
from patch_offline import PROJECT_NAME, sha256
from virtual_machine_compat import prepare_for_virtual_machine


APPX_NAME = "PrimeRemake.Asphalt8"
APPX_PUBLISHER = "CN=Asphalt 8 Prime Remake, OID.2.25.311729368913984317654407730594956997722=1"
RUNTIME_NAME = "Microsoft.VCLibs.120.00"
RUNTIME_FILE = "Microsoft.VCLibs.120.00-x86.appx"
RUNTIME_PUBLISHER = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
INSTALL_SCRIPT = r"""
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $packageHash) {
        throw 'The AppX changed after Setup prepared it.'
    }
    if (!(Get-AppxPackage -Name Microsoft.VCLibs.120.00 | Where-Object {
        $_.Architecture -eq 'X86' -and $_.Version -ge [version]'12.0.21005.1'
    })) {
        if (!$runtime) { throw 'The Microsoft VCLibs 120 x86 package is required. Select it in Setup and retry.' }
        $signature = Get-AuthenticodeSignature -LiteralPath $runtime
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -ne $runtimePublisher) {
            throw 'The runtime package does not have a valid Microsoft signature.'
        }
        Add-AppxPackage -Path $runtime
    }
    Add-AppxPackage -Path $package -AllowUnsigned
    $installed = Get-AppxPackage -Name PrimeRemake.Asphalt8
    if (!$installed) { throw 'Windows did not register the installed Prime Remake package.' }
    [IO.File]::WriteAllText($result, $installed.PackageFamilyName + '!App')
    exit 0
} catch {
    [IO.File]::WriteAllText($result, $_.Exception.Message)
    exit 1
}
"""


def package_layout(layout, package):
    manifest = (layout / "AppxManifest.xml").read_text(encoding="utf-8")
    tree = ElementTree.fromstring(manifest)
    namespace = {"app": tree.tag.split("}")[0].lstrip("{")}
    identity = tree.find("app:Identity", namespace)
    original_publisher = identity.attrib["Publisher"]
    previous_identity = f'Name="{identity.attrib["Name"]}" Publisher="{original_publisher}"'
    if manifest.count(previous_identity) != 1:
        raise ValueError("The game manifest does not have the expected AppX identity.")
    replacement = f'Name="{APPX_NAME}" Publisher="{APPX_PUBLISHER}"'
    manifest = manifest.replace(previous_identity, replacement, 1)
    previous_display_name = tree.find("app:Properties/app:PublisherDisplayName", namespace).text
    manifest = manifest.replace(f"<PublisherDisplayName>{previous_display_name}</PublisherDisplayName>",
                                f"<PublisherDisplayName>{PROJECT_NAME}</PublisherDisplayName>", 1)
    draft = package.parent / "package-manifest.xml"
    draft.write_text(manifest, encoding="utf-8")
    write_appx(layout, draft, package)


def validate_runtime(path):
    with ZipFile(path) as archive:
        manifest = ElementTree.fromstring(archive.read("AppxManifest.xml"))
    identity = manifest.find("{*}Identity")
    if (identity is None or identity.get("Name") != RUNTIME_NAME or
        identity.get("ProcessorArchitecture", "").lower() != "x86" or
        identity.get("Publisher") != RUNTIME_PUBLISHER or
        tuple(int(value) for value in identity.get("Version", "0").split(".")) < (12, 0, 21005, 1)):
        raise ValueError("Choose the Microsoft.VCLibs.120.00 x86 framework package, version 12.0.21005.1 or later.")


def install_package(package, runtime):
    if sys.getwindowsversion().build < 22000:
        raise ValueError("The packaged installer requires Windows 11.")
    if runtime:
        validate_runtime(runtime)
    powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    result = package.parent / "deployment-result.txt"
    values = {"package": str(package), "packageHash": sha256(package), "runtime": str(runtime) if runtime else "",
              "result": str(result), "runtimePublisher": RUNTIME_PUBLISHER}
    assignments = [f"${key} = '" + value.replace("'", "''") + "'" for key, value in values.items()]
    encoded = base64.b64encode(("\n".join(assignments) + "\n" + INSTALL_SCRIPT).encode("utf-16-le")).decode("ascii")
    outer = ("$ErrorActionPreference='Stop'; "
             "$p=Start-Process -FilePath powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -Wait "
             f"-ArgumentList '-NoProfile -NonInteractive -EncodedCommand {encoded}'; exit $p.ExitCode")
    process = subprocess.run([str(powershell), "-NoProfile", "-NonInteractive", "-Command", outer],
                             capture_output=True, text=True, errors="replace", creationflags=subprocess.CREATE_NO_WINDOW)
    message = result.read_text(encoding="utf-8").strip() if result.is_file() else ""
    if process.returncode or not re.fullmatch(r"PrimeRemake\.Asphalt8_[a-z0-9]{13}!App", message):
        raise ValueError(message or "Windows declined the installation or administrator approval.")
    return message


def install_clean(source, target, prepare, launcher, runtime):
    source, target = source.resolve(), target.resolve()
    if source == target or source in target.parents or target.exists():
        raise ValueError("Choose a new launcher folder outside the source.")
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".prime-install-", dir=target.parent) as temp:
        work = Path(temp)
        layout = work / "layout"
        prepare(source, layout)
        prepare_for_virtual_machine(layout)
        package = work / "Asphalt8PrimeRemake.appx"
        package_layout(layout, package)
        app_id = install_package(package, runtime)
        target.mkdir()
        try:
            (target / "app-id.txt").write_text(app_id + "\n", encoding="ascii")
            (target / launcher).write_bytes((layout / launcher).read_bytes())
        except OSError:
            raise ValueError(f"The game is installed. Open {PROJECT_NAME} from the Windows Start menu.")

param(
    [Parameter(Mandatory=$true)][string]$MsvcBin,
    [string]$RuntimeAppx,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $repo 'build\setup'
$output = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repo 'build\release' }
New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
$launcher = Join-Path $stage 'offline-launcher.exe'
& (Join-Path $repo 'trainer\Build-Trainer.ps1') -MsvcBin $MsvcBin -Output $launcher -ExperimentalBotRace

$runtimeStage = Join-Path $stage 'Microsoft.VCLibs.120.00-x86.appx'
if ($RuntimeAppx) {
    $runtime = (Resolve-Path -LiteralPath $RuntimeAppx).Path
    if ($runtime -ne $runtimeStage) { Copy-Item -LiteralPath $runtime -Destination $runtimeStage -Force }
} else {
    $expected = 'F1302E899F015818B1B807F7A1DC44C221C184E3F34A19FE81B5874D4AB974BD'
    if (!(Test-Path -LiteralPath $runtimeStage) -or (Get-FileHash -LiteralPath $runtimeStage -Algorithm SHA256).Hash -ne $expected) {
        $archive = Join-Path $stage 'VCLibs_Redist_Packages.zip'
        $download = 'https://download.microsoft.com/download/5/F/0/5F0F8404-9329-44A9-8176-ED6F7F746F25/VCLibs_Redist_Packages.zip'
        Invoke-WebRequest -Uri $download -UseBasicParsing -OutFile $archive
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $entry = $zip.GetEntry('VCLibs_Redist_Packages/Microsoft.VCLibs.120.00_12.0.21005.1_x86__8wekyb3d8bbwe.appx')
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $runtimeStage, $true)
        } finally {
            $zip.Dispose()
        }
        if ((Get-FileHash -LiteralPath $runtimeStage -Algorithm SHA256).Hash -ne $expected) {
            throw 'The Microsoft framework package did not match the published setup build input.'
        }
    }
}
$signature = Get-AuthenticodeSignature -LiteralPath $runtimeStage
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -ne 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US') {
    throw 'Expected a signed Microsoft framework package.'
}

$arguments = @(
    '-m', 'PyInstaller', '--noconfirm', '--onefile', '--windowed',
    '--name', 'Asphalt8PrimeRemake-Setup',
    '--distpath', $output, '--workpath', $stage, '--specpath', $stage,
    '--paths', (Join-Path $repo 'tools\native-ui'),
    '--add-data', "$launcher;.",
    '--add-data', "$runtimeStage;.",
    (Join-Path $PSScriptRoot 'setup.py')
)
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Setup packaging failed.' }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'Asphalt8PrimeRemake-Setup.exe')

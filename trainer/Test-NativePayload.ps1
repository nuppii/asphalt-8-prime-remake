[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$TrainerPath)
$ErrorActionPreference = 'Stop'
$trainer = (Resolve-Path -LiteralPath $TrainerPath).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('asphalt-native-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    $fixture = Join-Path $stage 'NativePayloadCheck.exe'
    & $compiler /nologo /target:exe /platform:x86 /optimize+ "/out:$fixture" (Join-Path $PSScriptRoot 'NativePayloadCheck.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated native fixture compilation failed.' }
    Write-Output ('Trainer SHA256: ' + (Get-FileHash -LiteralPath $trainer -Algorithm SHA256).Hash)
    & $fixture $trainer
    if ($LASTEXITCODE -ne 0) { throw "Isolated native fixture failed ($LASTEXITCODE)." }
}
finally {
    # This directory is created above with a fresh GUID, never provided by callers.
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}

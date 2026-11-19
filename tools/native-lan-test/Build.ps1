param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\build\native-lan-test'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x86 /optimize+ "/out:$OutputDirectory\HookHarness.exe" (Join-Path $PSScriptRoot 'HookHarness.cs')
if($LASTEXITCODE -ne 0){throw 'Explicit-PID test harness compilation failed.'}

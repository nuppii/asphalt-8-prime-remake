param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\build\multiplayer'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$sources = @('RoomProtocol.cs','RoomServer.cs','RoomClient.cs','LanRoomDiscovery.cs','PublicRoomDirectory.cs','RoomInvites.cs','SocialClient.cs','StunBinding.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:library /optimize+ /reference:System.Runtime.Serialization.dll "/out:$OutputDirectory\OfflineLab.Rooms.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Room library compilation failed.' }
& $compiler /nologo /target:exe /optimize+ /reference:System.Runtime.Serialization.dll "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\RoomCli.exe" (Join-Path $PSScriptRoot 'RoomCli.cs')
if ($LASTEXITCODE -ne 0) { throw 'Room CLI compilation failed.' }
& $compiler /nologo /target:exe /optimize+ /reference:System.Runtime.Serialization.dll "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\DiscoveryCheck.exe" (Join-Path $PSScriptRoot 'DiscoveryCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Discovery check compilation failed.' }
& $compiler /nologo /target:exe /optimize+ /reference:System.Runtime.Serialization.dll "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\InviteCheck.exe" (Join-Path $PSScriptRoot 'InviteCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Invitation check compilation failed.' }
& $compiler /nologo /target:exe /optimize+ /reference:System.Runtime.Serialization.dll "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\SocialCheck.exe" (Join-Path $PSScriptRoot 'SocialCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Social check compilation failed.' }
& $compiler /nologo /target:exe /optimize+ "/reference:$OutputDirectory\OfflineLab.Rooms.dll" "/out:$OutputDirectory\InternetCheck.exe" (Join-Path $PSScriptRoot 'InternetCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Internet connectivity check compilation failed.' }

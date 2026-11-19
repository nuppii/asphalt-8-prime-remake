param(
    [Parameter(Mandatory = $true)][string]$MsvcBin,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('AsphaltGrid-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $object = Join-Path $stage 'GridSpawn.obj'
    $image = Join-Path $stage 'GridSpawn.bin'
    & (Join-Path $MsvcBin 'cl.exe') /nologo /c /O2 /GS- /arch:IA32 "/Fo$object" (Join-Path $PSScriptRoot 'GridSpawn.c')
    if ($LASTEXITCODE -ne 0) { throw 'Native grid compilation failed.' }
    & (Join-Path $MsvcBin 'link.exe') /NOLOGO /NODEFAULTLIB /MACHINE:X86 /SUBSYSTEM:CONSOLE /ENTRY:PrepareGrid /FIXED:NO "/OUT:$image" $object
    if ($LASTEXITCODE -ne 0) { throw 'Native grid linking failed.' }
    $mutatorObject = Join-Path $stage 'MutatorPhysics.obj'
    $mutatorImage = Join-Path $stage 'MutatorPhysics.bin'
    & (Join-Path $MsvcBin 'cl.exe') /nologo /c /O2 /GS- /arch:IA32 "/Fo$mutatorObject" (Join-Path $PSScriptRoot 'MutatorPhysics.c')
    if ($LASTEXITCODE -ne 0) { throw 'Native mutator compilation failed.' }
    & (Join-Path $MsvcBin 'link.exe') /NOLOGO /NODEFAULTLIB /MACHINE:X86 /SUBSYSTEM:CONSOLE /ENTRY:ApplyMutators /FIXED:NO "/OUT:$mutatorImage" $mutatorObject
    if ($LASTEXITCODE -ne 0) { throw 'Native mutator linking failed.' }
    $lobbyObject = Join-Path $stage 'NativeLobbyUi.obj'
    $lobbyImage = Join-Path $stage 'NativeLobbyUi.bin'
    & (Join-Path $MsvcBin 'cl.exe') /nologo /c /O2 /GS- /arch:IA32 "/Fo$lobbyObject" (Join-Path $PSScriptRoot 'NativeLobbyUi.c')
    if ($LASTEXITCODE -ne 0) { throw 'Native lobby compilation failed.' }
    $lanObject = Join-Path $stage 'NativeLanBridge.obj'
    & (Join-Path $MsvcBin 'cl.exe') /nologo /c /O2 /GS- /arch:IA32 "/Fo$lanObject" (Join-Path $PSScriptRoot 'NativeLanBridge.c')
    if ($LASTEXITCODE -ne 0) { throw 'Native LAN adapter compilation failed.' }
    $policyObject = Join-Path $stage 'NativeLobbyPolicy.obj'
    & (Join-Path $MsvcBin 'cl.exe') /nologo /c /O2 /GS- /arch:IA32 "/Fo$policyObject" (Join-Path $PSScriptRoot 'NativeLobbyPolicy.c')
    if ($LASTEXITCODE -ne 0) { throw 'Native lobby policy compilation failed.' }
    & (Join-Path $MsvcBin 'link.exe') /NOLOGO /NODEFAULTLIB /MACHINE:X86 /SUBSYSTEM:CONSOLE /ENTRY:TickUi /FIXED:NO "/OUT:$lobbyImage" $lobbyObject $lanObject $policyObject
    if ($LASTEXITCODE -ne 0) { throw 'Native lobby linking failed.' }
    $sources = @('Trainer.cs', 'CheatTableForm.cs', 'GridSpawnHook.cs', 'MutatorEngine.cs', 'NativeMultiplayer.cs', 'NativeLobbyHook.cs', 'NativeLobbyBridge.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $sources += @('RoomProtocol.cs','RoomServer.cs','RoomClient.cs','LanRoomDiscovery.cs','PublicRoomDirectory.cs','RoomInvites.cs','SocialClient.cs') | ForEach-Object { Join-Path (Join-Path $PSScriptRoot '..\multiplayer') $_ }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /platform:x86 /optimize+ "/out:$Output" "/resource:$image,GridSpawn.x86" "/resource:$mutatorImage,MutatorPhysics.x86" "/resource:$lobbyImage,NativeLobbyUi.x86" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll /reference:System.Runtime.Serialization.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
}
finally {
    foreach ($name in 'GridSpawn.obj', 'GridSpawn.bin', 'MutatorPhysics.obj', 'MutatorPhysics.bin', 'NativeLobbyUi.obj', 'NativeLanBridge.obj', 'NativeLobbyPolicy.obj', 'NativeLobbyUi.bin', 'NativeLobbyUi.lib', 'NativeLobbyUi.exp') {
        $file = Join-Path $stage $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $stage
}

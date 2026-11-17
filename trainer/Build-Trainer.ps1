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
    $sources = @('Trainer.cs', 'CheatTableForm.cs', 'GridSpawnHook.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /platform:x86 /optimize+ "/out:$Output" "/resource:$image,GridSpawn.x86" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
}
finally {
    foreach ($name in 'GridSpawn.obj', 'GridSpawn.bin') {
        $file = Join-Path $stage $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $stage
}

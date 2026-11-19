[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$SeedLocalState,
    [switch]$Register
)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $GameDirectory).Path
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if($destination.TrimEnd('\') -eq $source.TrimEnd('\') -or $source.StartsWith($destination.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $destination.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test output must be separate from the source installation.'
}
foreach($name in 'Asphalt8.exe','AppxManifest.xml','Data','Assets') {
    if(!(Test-Path -LiteralPath (Join-Path $source $name))) { throw "Missing source game item: $name" }
}
$manifestSource=[IO.File]::ReadAllText((Join-Path $source 'AppxManifest.xml'))
[xml]$sourceXml=$manifestSource
if($sourceXml.Package.Identity.Version -ne '4.8.0.7') { throw 'Only the supported Windows 4.8.0.7 package is accepted.' }
$results=@()
foreach($role in 'Host','Client') {
    $folder=Join-Path $destination $role
    $marker=Join-Path $folder 'offline-lab-test-instance.json'
    if(Test-Path -LiteralPath $folder) {
        if(!(Test-Path -LiteralPath $marker)) { throw "Refusing an existing unmarked folder: $folder" }
        $previous=Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
        if($previous.Source -ne $source -or $previous.Role -ne $role) { throw 'The existing test instance belongs to another source or role.' }
    } else { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    $identity='OfflineLab.Asphalt480Lan'+$role
    if(Get-AppxPackage -Name $identity | Where-Object { $_.InstallLocation -ne $folder }) { throw "Test identity already registered elsewhere: $identity" }
    $existing=Get-Process Asphalt8 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $folder 'Asphalt8.exe') }
    if($existing) { throw "Close the $role test game before preparing its files." }
    # Independent executable/metadata copies; no launcher and no user save copy.
    Get-ChildItem -LiteralPath $source -File -Force | Where-Object { $_.Name -ne 'AppxManifest.xml' -and $_.Name -ne 'Asphalt 8 Airborne 4.8 Offline.exe' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $folder $_.Name) -Force
    }
    # Real copies inherit this package's access rights. Junctions into another
    # package can resolve but remain unreadable inside its app container.
    foreach($name in 'Data','Assets','IGBLib','IGPLib') {
        $original=Join-Path $source $name
        if(!(Test-Path -LiteralPath $original)) { continue }
        $link=Join-Path $folder $name
        if(Test-Path -LiteralPath $link) {
            $item=Get-Item -LiteralPath $link -Force
            if($item.LinkType) {
                if($item.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$item.Target) -ne [IO.Path]::GetFullPath($original)) { throw "Unexpected resource link: $link" }
                Remove-Item -LiteralPath $link -Force
            }
        }
        & robocopy $original $link /E /COPY:DAT /DCOPY:DAT /MT:16 /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
        if($LASTEXITCODE -ge 8) { throw "Game resource copy failed: $name" }
    }
    $original=Join-Path $source '_offline_patch_backup\Asphalt8.exe.original'
    if(Test-Path -LiteralPath $original) {
        $backup=Join-Path $folder '_offline_patch_backup'
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Copy-Item -LiteralPath $original -Destination (Join-Path $backup 'Asphalt8.exe.original') -Force
    }
    [xml]$xml=$manifestSource
    $xml.Package.Identity.Name=$identity
    $xml.Package.Properties.DisplayName='Asphalt 8 LAN Test '+$role
    $namespaces=New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $namespaces.AddNamespace('a','http://schemas.microsoft.com/appx/2010/manifest')
    $namespaces.AddNamespace('m2','http://schemas.microsoft.com/appx/2013/manifest')
    $visual=$xml.SelectSingleNode('//m2:VisualElements',$namespaces)
    $visual.SetAttribute('DisplayName','Asphalt 8 LAN Test '+$role)
    $tile=$xml.SelectSingleNode('//m2:DefaultTile',$namespaces)
    $tile.SetAttribute('ShortName','A8 LAN '+$role)
    # Do not take over the production game's Xbox protocol association.
    foreach($node in @($xml.SelectNodes('//a:Application/a:Extensions/a:Extension[@Category="windows.protocol"]',$namespaces))) {
        [void]$node.ParentNode.RemoveChild($node)
    }
    $xml.Save((Join-Path $folder 'AppxManifest.xml'))
    $record=[ordered]@{Version=1;Role=$role;Source=$source;Identity=$identity;Folder=$folder;IndependentLocalState=$true;IndependentResources=$true}
    [IO.File]::WriteAllText($marker,($record|ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
    # This is a NEW loose development package, not an existing staged package.
    # DisableDevelopmentMode incorrectly requires installed package metadata.
    if($Register) { Add-AppxPackage -Path (Join-Path $folder 'AppxManifest.xml') -Register -ErrorAction Stop }
    $package=Get-AppxPackage -Name $identity
    if($SeedLocalState) {
        if(!$package) { throw 'Register the test package before seeding its independent LocalState.' }
        $seed=(Resolve-Path -LiteralPath $SeedLocalState).Path
        $local=Join-Path $env:LOCALAPPDATA ('Packages\'+$package.PackageFamilyName+'\LocalState')
        if($seed.TrimEnd('\') -eq $local.TrimEnd('\')) { throw 'A test save cannot seed itself.' }
        $seedMarker=Join-Path $folder 'seeded-save.json'
        if(Test-Path -LiteralPath $seedMarker) { throw 'This test save has already been seeded; refusing to overwrite test progress.' }
        & robocopy $seed $local /E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
        if($LASTEXITCODE -ge 8) { throw 'Save copy failed.' }
        [IO.File]::WriteAllText($seedMarker,(@{Source=$seed;Target=$local;Utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json))
    }
    $results += [pscustomobject]@{Role=$role;Folder=$folder;Identity=$identity;AppId=if($package){$package.PackageFamilyName+'!App'}else{$null}}
}
$results | Format-List

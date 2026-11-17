[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$folder=Join-Path $root 'vendor/Harmony-2.3.3'
$package=Join-Path $folder 'Lib.Harmony.2.3.3.nupkg'
$expected='87B63DDB92F04FCB89C30B7EBAE473C948DD65E80569EB94AEF08B163BC3BF63'
if(!(Test-Path -LiteralPath $package)){
 Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/lib.harmony/2.3.3/lib.harmony.2.3.3.nupkg' -OutFile $package
}
if((Get-FileHash $package).Hash -ne $expected){throw 'Pinned upstream package hash mismatch; no patch/build performed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{
 $entry=$zip.GetEntry('lib/net452/0Harmony.dll');if(!$entry){throw 'Pinned Harmony asset missing'}
 $input=$entry.Open();$output=[IO.File]::Create((Join-Path $folder '0Harmony.dll'))
 try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
}finally{$zip.Dispose()}
& (Join-Path $PSScriptRoot 'build-harmony-fieldtype-fix.ps1') -CecilPath (Join-Path $GameRoot 'Client/Empyrion_Data/Managed/Trivial.Mono.Cecil.dll')
if((Get-FileHash (Join-Path $root 'vendor/Harmony-2.3.3-fieldtypefix/0Harmony.dll')).Hash -ne '45032A3366CFDE979E92CC07489BB5D7DE73EA3A99822179664B1040DF55997A'){throw 'Patched dependency differs from audited runtime input'}
'Pinned dependency restored and reproduced locally. No game writes.'

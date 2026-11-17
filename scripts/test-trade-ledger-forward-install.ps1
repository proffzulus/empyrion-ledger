param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop';$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage=Join-Path $root 'artifacts\release\stage\TradeLedger'
$fixture=Join-Path $root ('artifacts\release\fixtures\forward-install-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory (Join-Path $fixture 'runtime') -Force
$m=Get-Content (Join-Path $stage 'build-manifest.json') -Raw|ConvertFrom-Json
foreach($name in $m.files.PSObject.Properties.Name){[IO.File]::Copy((Join-Path $stage $name),(Join-Path $fixture $name))}
# Deliberately require the unknown-image route while keeping real payloads intact.
# This is an isolated manifest fixture, never the distributable certificate.
$m.gameHashes.'Assembly-CSharp.dll'='0000000000000000000000000000000000000000000000000000000000000000';$m.target='future-label-fixture'
[IO.File]::WriteAllText((Join-Path $fixture 'build-manifest.json'),($m|ConvertTo-Json -Depth 12))
$installed=Join-Path $GameRoot 'Content\Mods\TradeLedgerProbeB';if(Test-Path (Join-Path $GameRoot 'Content\Mods\TradeLedger\TradeLedger.dll')){$installed=Join-Path $GameRoot 'Content\Mods\TradeLedger'}
$before=@{};foreach($file in Get-ChildItem $installed -File){$before[$file.FullName]=(Get-FileHash $file.FullName).Hash}
$output=@(& (Join-Path $PSScriptRoot 'deploy-trade-ledger.ps1') -GameRoot $GameRoot -Stage $fixture)
if(@($output|Where-Object {$_ -like '*journal=ready*' -and $_ -like '*inventory=ready*' -and $_ -like '*trade=ready*'}).Count -ne 1 -or $output -notcontains 'PREVIEW ONLY'){throw 'Forward installer did not validate all current capabilities'}
'PASS changed-label/image installer route executes static shipped audit and reaches preview'
$m.gameHashes.'ModApi.dll'='0000000000000000000000000000000000000000000000000000000000000000'
[IO.File]::WriteAllText((Join-Path $fixture 'build-manifest.json'),($m|ConvertTo-Json -Depth 12))
$blocked=$false;try{& (Join-Path $PSScriptRoot 'deploy-trade-ledger.ps1') -GameRoot $GameRoot -Stage $fixture|Out-Null}catch{$blocked=$_.Exception.Message -like '*Game reference differs*'}
if(!$blocked){throw 'Non-native API dependency integrity was weakened'}
'PASS changed API dependency rejected before package writes'
foreach($p in $before.Keys){if((Get-FileHash $p).Hash -ne $before[$p]){throw 'Read-only preview changed installed bytes'}}
'PASS existing installed top-level payload/settings unchanged by forward previews'
[ordered]@{checks=3;fixture=$fixture;mode='PREVIEW ONLY';nativeCapabilities=$output;gameWrites=$false;actualUnity='not run'}|ConvertTo-Json -Depth 6

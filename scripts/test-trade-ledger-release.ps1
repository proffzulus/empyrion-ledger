$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $root ('artifacts\release\fixtures\'+[guid]::NewGuid().ToString('N'))
$stage=Join-Path $fixture 'package';$source=Join-Path $root 'artifacts\release\stage\TradeLedger'
$null=New-Item -ItemType Directory -Path $stage -Force
$files=@('TradeLedger.dll','runtime\TradeLedger.Runtime.dll','runtime\0Harmony.dll','TradeLedger_Info.yaml','TradeLedger.settings','Harmony-LICENSE.txt','THIRD-PARTY-LICENSES.txt','README.md','RELEASE-NOTICES.md')
$null=New-Item -ItemType Directory -Path (Join-Path $stage 'runtime') -Force
foreach($n in $files){[IO.File]::Copy((Join-Path $source $n),(Join-Path $stage $n))}
$manifest=Get-Content (Join-Path $source 'build-manifest.json') -Raw|ConvertFrom-Json
# Synthetic certificate refers only to synthetic game dependency files below. No
# real package certificate is changed, and no real game/save path is an output.
$manifest.nativeCertified=$true;$manifest.target='fixture v0';$manifest.gameHashes=[pscustomobject]@{};$manifest.ui.referenceHashes=[pscustomobject]@{}
$manifest.sqlite.nativeRelative='Client\fixture-sqlite.txt'
$fakeBytes=[Text.Encoding]::UTF8.GetBytes('synthetic dependency, not a game binary')
$sha=[Security.Cryptography.SHA256]::Create();try{$manifest.sqlite.nativeHash=[BitConverter]::ToString($sha.ComputeHash($fakeBytes)).Replace('-','')}finally{$sha.Dispose()}
[IO.File]::WriteAllText((Join-Path $stage 'build-manifest.json'),($manifest|ConvertTo-Json -Depth 12))
$script=Join-Path $PSScriptRoot 'deploy-trade-ledger.ps1';$passed=New-Object 'System.Collections.Generic.List[string]'
function Ok([bool]$v,[string]$n){if(!$v){throw "FAIL $n"};$passed.Add($n);Write-Output "PASS $n"}
function Game([string]$name){$g=Join-Path $fixture $name;$null=New-Item -ItemType Directory -Path (Join-Path $g 'Content\Mods'),(Join-Path $g 'Client') -Force;[IO.File]::WriteAllText((Join-Path $g 'BuildNumber.txt'),'fixture v0');[IO.File]::WriteAllBytes((Join-Path $g 'Client\fixture-sqlite.txt'),$fakeBytes);return $g}
function Install([string]$g){& $script -GameRoot $g -Stage $stage -Install -Confirm:$false *> (Join-Path $g 'deployment-output.txt')}
function Block([string]$g,[string]$pattern){$blocked=$false;try{Install $g}catch{$blocked=$_.Exception.Message -like $pattern};return $blocked}
$g=Game 'new'; & $script -GameRoot $g -Stage $stage *> (Join-Path $g 'preview.txt');$target=Join-Path $g 'Content\Mods\TradeLedger';Ok (!(Test-Path $target)) 'preview makes no package writes'
Install $g;Ok ((Get-ChildItem $target -File).Count -eq 8) 'clean ordinary-folder install exact top-level file set'
Ok ([IO.File]::ReadAllText((Join-Path $target 'TradeLedger.settings')) -match 'RuntimeObservation=on') 'new default observation ON'
$settings=[Text.Encoding]::UTF8.GetBytes("# user-owned choice`nRuntimeObservation=off`nDiagnostics=Debug`n");[IO.File]::WriteAllBytes((Join-Path $target 'TradeLedger.settings'),$settings)
$null=New-Item -ItemType Directory -Path (Join-Path $target 'logs') -Force;[IO.File]::WriteAllText((Join-Path $target 'logs\original-evidence.log'),'preserve')
Install $g;Ok ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $target 'TradeLedger.settings'))) -ceq [Convert]::ToBase64String($settings)) 'update preserves custom settings byte-for-byte including OFF'
Ok ([IO.File]::ReadAllText((Join-Path $target 'logs\original-evidence.log')) -eq 'preserve') 'update leaves existing logs untouched'
$before=(Get-FileHash (Join-Path $target 'TradeLedger.dll')).Hash
$lock=[IO.File]::Open((Join-Path $target 'runtime\0Harmony.dll'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try{$blocked=Block $g '*Exclusive-file preflight blocked before writes*'}finally{$lock.Dispose()};Ok ($blocked -and (Get-FileHash (Join-Path $target 'TradeLedger.dll')).Hash -eq $before) 'locked dependency blocks before first write'
[IO.File]::WriteAllText((Join-Path $target 'unknown-user.txt'),'keep');Ok (Block $g '*Unknown existing package*') 'unknown file blocks update';Ok ([IO.File]::ReadAllText((Join-Path $target 'unknown-user.txt')) -eq 'keep') 'unknown evidence preserved'
# A tiny owned versioned assembly replaces the private historical package input.
$g2=Game 'legacy';$legacy=Join-Path $g2 'Content\Mods\TradeLedgerProbeB';$null=New-Item -ItemType Directory -Path (Join-Path $legacy 'runtime') -Force
Add-Type -TypeDefinition 'using System.Reflection;[assembly:AssemblyVersion("0.3.12.0")] public sealed class OwnedLegacyPackageFixture {}' -OutputAssembly (Join-Path $legacy 'TradeLedgerProbeB.dll') -OutputType Library
$oldMap=@{'runtime\TradeLedgerProbeB.Runtime.dll'='runtime\TradeLedger.Runtime.dll';'runtime\0Harmony.dll'='runtime\0Harmony.dll';'TradeLedgerProbeB_Info.yaml'='TradeLedger_Info.yaml';'TradeLedgerProbeB.settings'='TradeLedger.settings';'Harmony-LICENSE.txt'='Harmony-LICENSE.txt';'THIRD-PARTY-LICENSES.txt'='THIRD-PARTY-LICENSES.txt'}
foreach($n in $oldMap.Keys){[IO.File]::Copy((Join-Path $source $oldMap[$n]),(Join-Path $legacy $n))}
$oldFiles=[ordered]@{};foreach($n in @('TradeLedgerProbeB.dll')+@($oldMap.Keys)){$oldFiles[$n]=(Get-FileHash (Join-Path $legacy $n)).Hash}
[IO.File]::WriteAllText((Join-Path $legacy 'build-manifest.json'),(@{version='0.3.12';files=$oldFiles}|ConvertTo-Json -Depth 5))
[IO.File]::WriteAllBytes((Join-Path $legacy 'TradeLedgerProbeB.settings'),$settings)
$null=New-Item -ItemType Directory -Path (Join-Path $legacy 'logs') -Force;[IO.File]::WriteAllText((Join-Path $legacy 'logs\historic.log'),'unchanged')
# Sentinel save-local identity/data is never an installer input, only an independent assertion.
$data=Join-Path $g2 'Saves\Games\Personal\Mods\TradeLedger';$null=New-Item -ItemType Directory -Path $data -Force;[IO.File]::WriteAllText((Join-Path $data 'TradeLedger.sqlite'),'sentinel-not-sqlite')
Install $g2;$new=Join-Path $g2 'Content\Mods\TradeLedger'
Ok ((Test-Path (Join-Path $new 'TradeLedger.dll')) -and !(Test-Path (Join-Path $legacy 'TradeLedgerProbeB.dll'))) 'legacy upgrade leaves exactly one loader'
Ok ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $new 'TradeLedger.settings'))) -ceq [Convert]::ToBase64String($settings)) 'legacy settings migrate byte-identically'
Ok ([IO.File]::ReadAllText((Join-Path $legacy 'logs\historic.log')) -eq 'unchanged') 'legacy evidence sentinel is retained in place'
Ok ([IO.File]::ReadAllText((Join-Path $data 'TradeLedger.sqlite')) -eq 'sentinel-not-sqlite') 'save-local state neither moved nor opened'
Install $g2;Ok (!(Test-Path (Join-Path $legacy 'TradeLedgerProbeB.dll'))) 'repeated upgrade does not recreate legacy loader'
$g3=Game 'bad-current';Install $g3;$t3=Join-Path $g3 'Content\Mods\TradeLedger';[IO.File]::AppendAllText((Join-Path $t3 'README.md'),'tampered');Ok (Block $g3 '*Existing package hash mismatch*') 'changed owned payload refuses update'
$g4=Game 'changed-build';[IO.File]::WriteAllText((Join-Path $g4 'BuildNumber.txt'),'other build');Install $g4;Ok (Test-Path (Join-Path $g4 'Content\Mods\TradeLedger\TradeLedger.dll')) 'changed build label alone does not refuse compatible package'
$cert=$manifest.nativeCertified;$manifest.nativeCertified=$false;[IO.File]::WriteAllText((Join-Path $stage 'build-manifest.json'),($manifest|ConvertTo-Json -Depth 12));Ok (Block $g2 '*not a native-certified*') 'preparation-only package cannot install'
$record=[ordered]@{checks=$passed.Count;passed=$passed;fixture=$fixture;scope='synthetic packages/game refs; no real game or save writes';actualUnity='not run'}
$record|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'artifacts\release\verification\deployment-fixtures.json') -Encoding UTF8

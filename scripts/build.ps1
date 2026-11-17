[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$MSBuildPath)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$managed=Join-Path $GameRoot 'Client/Empyrion_Data/Managed'
$source=Join-Path $root 'src/TradeLedgerProbeB';$output=Join-Path $root 'artifacts/poc-b';$build=Join-Path $output 'build';$stage=Join-Path $root 'artifacts/release/stage/TradeLedger';$verify=Join-Path $output 'verification'
foreach($folder in @($build,$stage,$verify,(Join-Path $stage 'runtime'),(Join-Path $root 'artifacts/release/verification'))){New-Item -ItemType Directory -Path $folder -Force|Out-Null}
if(!(Test-Path -LiteralPath $MSBuildPath)){throw 'Supply an existing MSBuild executable; no tools are installed by this script'}
$contract=Get-Content (Join-Path $source 'contract-5232.json') -Raw|ConvertFrom-Json
$profile=Get-Content (Join-Path $source 'NativeContracts5232.json') -Raw|ConvertFrom-Json
foreach($item in $contract.hashes.PSObject.Properties){if($item.Name -ne 'Assembly-CSharp.dll' -and (Get-FileHash (Join-Path $managed $item.Name)).Hash -ne $item.Value){throw ('Game dependency changed: '+$item.Name)}}
$refs=@{'UnityEngine.TextRenderingModule.dll'='BC789553A0BDC72A1248324D0AC402EB1F412DED427BEA6DCE6073F4EF27A7B4';'UnityEngine.IMGUIModule.dll'='83C3C8182B2F822B253B7F28022004FE1CF9F48392AF4EA8EEF4130167DF4E12';'UnityEngine.InputLegacyModule.dll'='6D40162C06BFDBD965E616745705C2E450013A7E74CA116DC5E0AE26F8B4C7A9'}
foreach($n in $refs.Keys){if((Get-FileHash (Join-Path $managed $n)).Hash -ne $refs[$n]){throw ('Game UI dependency changed: '+$n)}}
$cecil=Join-Path $managed 'Trivial.Mono.Cecil.dll'
$null=[Reflection.Assembly]::LoadFrom($cecil)
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $source 'NativeInstallAudit.cs.txt'))) -ReferencedAssemblies $cecil,'System.Core','System.Web.Extensions'
$audit=@([LedgerInstallContractAudit]::Inspect((Join-Path $managed 'Assembly-CSharp.dll'),([IO.File]::ReadAllText((Join-Path $source 'NativeContracts5232.json')))))
$audit|Tee-Object -FilePath (Join-Path $verify 'feature-contracts.txt')
if($audit -notcontains 'runtime-core=ready'){throw 'Required passive runtime-core contract unavailable; no certified package'}
$actual=(Get-FileHash (Join-Path $managed 'Assembly-CSharp.dll')).Hash
if($actual -eq $profile.Hash){foreach($v in Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'verify-poc-b-*.ps1'|Sort-Object Name){& $v.FullName -GameRoot $GameRoot}}
else{'Unknown image: exact feature contracts above decide capability availability; known-image token-only detail verifiers are not falsely reused.'}
$vendor=Join-Path $root 'vendor/Harmony-2.3.3-fieldtypefix';$harmony=Join-Path $vendor '0Harmony.dll'
if(!(Test-Path $harmony)){throw 'Run restore-dependencies.ps1 first'}
if((Get-FileHash $harmony).Hash -ne '45032A3366CFDE979E92CC07489BB5D7DE73EA3A99822179664B1040DF55997A'){throw 'Audited Harmony input differs'}
$contractFile=Join-Path $output 'BuildContract.g.cs';[IO.File]::Copy((Join-Path $source 'BuildContract5232.cs.txt'),$contractFile,$true)
$inputs=@(Get-ChildItem (Join-Path $root 'src') -Recurse -File|Sort-Object FullName|ForEach-Object {(Get-FileHash $_.FullName).Hash})
$inputs+=Get-ChildItem $PSScriptRoot -File|Sort-Object Name|ForEach-Object {(Get-FileHash $_.FullName).Hash}
$sha=[Security.Cryptography.SHA256]::Create();try{$fingerprint=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($inputs -join '')))).Replace('-','')}finally{$sha.Dispose()}
$constants=Join-Path $output 'ProbeBuild.g.cs'
function Constants([string]$adapter){$text='namespace TradeLedgerProbeB { internal static class ProbeBuild { internal const string Fingerprint="'+$fingerprint+'"; internal const string ModApiHash="'+$contract.hashes.'ModApi.dll'+'"; internal const string MifHash="'+$contract.hashes.'Mif.dll'+'"; internal const string RuntimeHash="'+$contract.hashes.'Assembly-CSharp.dll'+'"; internal const string AdapterHash="'+$adapter+'"; } }';[IO.File]::WriteAllText($constants,$text,(New-Object Text.UTF8Encoding($false)))}
function Compile([string]$project,[string[]]$extra){& $MSBuildPath (Join-Path $root $project) /t:Rebuild /nologo /verbosity:minimal "/p:GameManaged=$managed" "/p:ProbeBuildFile=$constants" "/p:ContractFile=$contractFile" "/p:PathMap=$root=/_/" /p:LedgerPublicRelease=True @extra;if($LASTEXITCODE -ne 0){throw ('Compilation failed: '+$project)}}
$loader=Join-Path $build 'TradeLedger.dll';$adapter=Join-Path $build 'TradeLedger.Runtime.dll'
Constants 'deterministic-seed';Compile 'src/TradeLedgerProbeB/TradeLedgerProbeB.csproj' @()
Compile 'src/TradeLedgerProbeB/TradeLedgerProbeB.Runtime.csproj' @("/p:ProbeDll=$loader","/p:HarmonyDll=$harmony")
& (Join-Path $PSScriptRoot 'prepare-probe-fixture.ps1') -AdapterPath $adapter -CecilPath $cecil
Constants (Get-FileHash $adapter).Hash;Compile 'src/TradeLedgerProbeB/TradeLedgerProbeB.csproj' @()
$map=@{'TradeLedger.dll'=$loader;'runtime/TradeLedger.Runtime.dll'=$adapter;'runtime/0Harmony.dll'=$harmony;'TradeLedger_Info.yaml'=(Join-Path $source 'TradeLedger_Info.yaml');'TradeLedger.settings'=(Join-Path $source 'TradeLedger.settings');'Harmony-LICENSE.txt'=(Join-Path $vendor 'LICENSE');'THIRD-PARTY-LICENSES.txt'=(Join-Path $vendor 'THIRD-PARTY-LICENSES.txt')};$hashes=[ordered]@{}
foreach($n in $map.Keys){[IO.File]::Copy($map[$n],(Join-Path $stage $n),$true);$hashes[$n.Replace('/','\')]=(Get-FileHash (Join-Path $stage $n)).Hash}
$native='Client/Empyrion_Data/Plugins/x86_64/sqlite3.dll';$nativeHash='17A3DB82B949EBC604D613F8EBB8371A874100F4E293D841AE16AEA2C616B644'
if((Get-FileHash (Join-Path $GameRoot $native)).Hash -ne $nativeHash){throw 'Native SQLite dependency differs'}
$manifest=[ordered]@{version='1.0.0';target=$contract.build;fingerprint=$fingerprint;compilerVersion=(Get-Item $MSBuildPath).VersionInfo.FileVersion;publicRelease=$true;nativeCertified=$true;packageName='TradeLedger';gameHashes=$contract.hashes;files=$hashes;ui=@{referenceHashes=$refs};sqlite=@{provider='game-shipped Mono.Data.Sqlite 4.0.0.0';nativeRelative=$native.Replace('/','\');nativeHash=$nativeHash};nativeCapabilities=$audit;actualGameImage=$actual}
[IO.File]::WriteAllText((Join-Path $stage 'build-manifest.json'),($manifest|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding($false)))
'PUBLIC SOURCE BUILD VERIFIED. Repository artifacts only; not installed, uploaded or live-tested.'

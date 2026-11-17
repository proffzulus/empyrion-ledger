[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param([Parameter(Mandatory=$true)][string]$GameRoot,[string]$Stage=(Join-Path $PSScriptRoot 'TradeLedger'),[switch]$Install,[string]$EvidencePath)
$ErrorActionPreference='Stop'
$game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
$stageRoot=[IO.Path]::GetFullPath($Stage).TrimEnd('\')
$target=Join-Path $game 'Content\Mods\TradeLedger'
$legacy=Join-Path $game 'Content\Mods\TradeLedgerProbeB'
$allowed=@('TradeLedger.dll','runtime\TradeLedger.Runtime.dll','runtime\0Harmony.dll','TradeLedger_Info.yaml','TradeLedger.settings','Harmony-LICENSE.txt','THIRD-PARTY-LICENSES.txt','README.md','RELEASE-NOTICES.md')
$oldAllowed=@('TradeLedgerProbeB.dll','runtime\TradeLedgerProbeB.Runtime.dll','runtime\0Harmony.dll','TradeLedgerProbeB_Info.yaml','TradeLedgerProbeB.settings','Harmony-LICENSE.txt','THIRD-PARTY-LICENSES.txt')
function No-Link([string]$path){$p=[IO.Path]::GetFullPath($path);while($p){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Reparse path rejected: $p"};$p=[IO.Path]::GetDirectoryName($p)}}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Digest([byte[]]$bytes){$sha=[Security.Cryptography.SHA256]::Create();try{[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}}
function Names([string]$root,[bool]$installed){
 $names=@();foreach($entry in Get-ChildItem -LiteralPath $root -Force){
  No-Link $entry.FullName
  if($entry.PSIsContainer){
   if($installed -and $entry.Name -in @('logs','legacy-recovery')){continue} # Owned evidence/data: never enumerate or write inside.
   if($entry.Name -ne 'runtime'){throw "Unknown package directory: $($entry.FullName)"}
   foreach($file in Get-ChildItem -LiteralPath $entry.FullName -Force){No-Link $file.FullName;if($file.PSIsContainer){throw 'Unknown nested package directory'};$names+='runtime\'+$file.Name}
  }else{$names+=$entry.Name}
 };return $names
}
foreach($p in @($game,$stageRoot,$target,$legacy)){No-Link $p}
if($stageRoot -eq $target -or $stageRoot.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Stage must be outside the game installation'}
$manifest=Get-Content -LiteralPath (Join-Path $stageRoot 'build-manifest.json') -Raw|ConvertFrom-Json
if($manifest.publicRelease -isnot [bool] -or $manifest.nativeCertified -isnot [bool] -or !$manifest.publicRelease -or !$manifest.nativeCertified -or $manifest.packageName -ne 'TradeLedger'){throw 'Candidate is not a native-certified public Trade Ledger release; installation prohibited'}
if(@(Compare-Object $allowed @($manifest.files.PSObject.Properties.Name)).Count -or @(Compare-Object ($allowed+@('build-manifest.json')) @(Names $stageRoot $false)).Count){throw 'Unexpected release file set'}
foreach($name in $allowed){if((Hash (Join-Path $stageRoot $name)) -ne $manifest.files.$name){throw "Stage hash mismatch: $name"}}
if([IO.File]::ReadAllText((Join-Path $stageRoot 'TradeLedger_Info.yaml')) -notmatch '(?m)^ModTargets:\s*Client\s*$'){throw 'Release must target Client'}
foreach($name in @('TradeLedger.dll','runtime\TradeLedger.Runtime.dll')){$assembly=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $stageRoot $name));if($assembly.Version.ToString() -ne ($manifest.version+'.0') -or $assembly.Name -ne [IO.Path]::GetFileNameWithoutExtension($name)){throw 'Release assembly identity/version mismatch'}}
$observedBuild=([IO.File]::ReadAllText((Join-Path $game 'BuildNumber.txt')) -replace '\s+',' ').Trim()
$nativeImagePath=Join-Path $game 'Client\Empyrion_Data\Managed\Assembly-CSharp.dll'
$nativeImageHash=$(if(Test-Path -LiteralPath $nativeImagePath){Hash $nativeImagePath}else{$null})
$nativeChanged=$false;$nativeAudit=@('known certified image; build label is informational')
foreach($p in $manifest.gameHashes.PSObject.Properties){if((Hash (Join-Path $game ('Client\Empyrion_Data\Managed\'+$p.Name))) -ne $p.Value){if($p.Name -eq 'Assembly-CSharp.dll'){$nativeChanged=$true}else{throw "Game reference differs: $($p.Name)"}}}
if((Hash (Join-Path $game $manifest.sqlite.nativeRelative)) -ne $manifest.sqlite.nativeHash){throw 'Game SQLite dependency differs'}
foreach($p in $manifest.ui.referenceHashes.PSObject.Properties){if((Hash (Join-Path $game ('Client\Empyrion_Data\Managed\'+$p.Name))) -ne $p.Value){throw "Game UI reference differs: $($p.Name)"}}
if($nativeChanged){
 # A changed image is not a veto. Use the shipped contract validator in an isolated
 # process: metadata/IL inspection only, no game methods, hooks or instance reads.
 # Unchanged private/API/Unity/SQLite dependencies above remain required.
 $args64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@{managed=(Join-Path $game 'Client\Empyrion_Data\Managed');loader=(Join-Path $stageRoot 'TradeLedger.dll')}|ConvertTo-Json -Compress)))
 $code=@'
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
try{
 $p=ConvertFrom-Json ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__ARGS__')))
 $managed=$p.managed
 $cecil=Join-Path $managed 'Trivial.Mono.Cecil.dll'
 if((Get-FileHash -LiteralPath $cecil).Hash -ne '4730CCD23C3BFFBE06D21529FF05DDD7751DBFC1FD5682324FBE8F2444C3E392'){throw 'Static contract audit tool differs; no native code executed'}
 $null=[Reflection.Assembly]::LoadFrom($cecil)
 $loader=[Reflection.Assembly]::LoadFrom($p.loader)
 $auditStream=$loader.GetManifestResourceStream('TradeLedger.NativeInstallAudit.cs');$profileStream=$loader.GetManifestResourceStream('TradeLedgerProbeB.NativeContracts.json')
 if(!$auditStream -or !$profileStream){throw 'Shipped native audit/profile resource missing'}
 $source=New-Object IO.StreamReader($auditStream)
 $profile=New-Object IO.StreamReader($profileStream)
 try{Add-Type -TypeDefinition $source.ReadToEnd() -ReferencedAssemblies $cecil,'System.Core','System.Web.Extensions'
  $result=[LedgerInstallContractAudit]::Inspect((Join-Path $managed 'Assembly-CSharp.dll'),$profile.ReadToEnd())
 }finally{$source.Dispose();$profile.Dispose()}
 foreach($line in $result){[Console]::WriteLine($line)}
 if($result -notcontains 'journal=ready'){throw 'Journal capability unavailable'}
}catch{[Console]::Error.WriteLine($_.Exception.ToString());exit 1}
'@
 $code=$code.Replace('__ARGS__',$args64)
 $shell=Join-Path $PSHOME 'powershell.exe';if(!(Test-Path -LiteralPath $shell)){$shell=Join-Path $PSHOME 'pwsh.exe'}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=$shell;$start.Arguments='-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $audit=New-Object Diagnostics.Process;$audit.StartInfo=$start
 try{
  $null=$audit.Start();$stdout=$audit.StandardOutput.ReadToEndAsync();$stderr=$audit.StandardError.ReadToEndAsync()
  if(!$audit.WaitForExit(30000)){throw 'Static capability audit exceeded30s; no writes; no process terminated'}
  $nativeAudit=@($stdout.Result -split '\r?\n'|Where-Object {$_})
  if($audit.ExitCode -ne 0 -or $nativeAudit -notcontains 'journal=ready'){throw ('Native feature contracts incompatible or unresolved; no writes: '+($nativeAudit -join '; ')+'; '+$stderr.Result)}
 }finally{$audit.Dispose()}
 Write-Output ('Changed native image validated; optional unavailable capabilities degrade independently: '+($nativeAudit -join '; '))
}
$preserved=$null;$existing=@{};$legacyActive=$false
function Validate-Installed([string]$root,[string[]]$files,[string]$settings,[string]$loader){
 $m=Get-Content -LiteralPath (Join-Path $root 'build-manifest.json') -Raw|ConvertFrom-Json
 if(@(Compare-Object $files @($m.files.PSObject.Properties.Name)).Count -or @(Compare-Object ($files+@('build-manifest.json')) @(Names $root $true)).Count){throw "Unknown existing package: $root"}
 if($m.deploymentState -and $m.deploymentState -ne 'verified'){throw "Incomplete previous deployment: $root; inspect before retry"}
 foreach($n in $files){if($n -ne $settings -and (Hash (Join-Path $root $n)) -ne $m.files.$n){throw "Existing package hash mismatch: $root\$n"}}
 $assembly=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $root $loader));if($assembly.Version.ToString() -ne ($m.version+'.0') -or $assembly.Name -ne [IO.Path]::GetFileNameWithoutExtension($loader)){throw 'Existing package identity/version mismatch'}
 $s=[IO.File]::ReadAllBytes((Join-Path $root $settings));$text=[Text.Encoding]::UTF8.GetString($s)
 if($s.Length -gt 1024 -or @([regex]::Matches($text,'(?m)^RuntimeObservation=(?:on|off)\r?$')).Count -ne 1 -or @([regex]::Matches($text,'(?m)^Diagnostics=(?:Off|Warn|Info|Debug)\r?$')).Count -ne 1){throw 'Existing settings invalid; preserved without mutation'}
 return ,$s
}
if(Test-Path -LiteralPath $target){$preserved=Validate-Installed $target $allowed 'TradeLedger.settings' 'TradeLedger.dll';foreach($n in $allowed+@('build-manifest.json')){$existing[(Join-Path $target $n)]=$true}}
if(Test-Path -LiteralPath $legacy){
 $legacyNames=@(Names $legacy $true)
 if($legacyNames.Count){
  if($preserved){throw 'Both release and legacy payloads exist; refuse ambiguous upgrade'}
  $preserved=Validate-Installed $legacy $oldAllowed 'TradeLedgerProbeB.settings' 'TradeLedgerProbeB.dll';$legacyActive=$true
  foreach($n in $oldAllowed+@('build-manifest.json')){$existing[(Join-Path $legacy $n)]=$true}
 }
}
$a=Join-Path $game 'Content\Mods\TradeLedgerProbe\TradeLedgerProbe_Info.yaml'
if((Test-Path -LiteralPath $a) -and [IO.File]::ReadAllText($a) -notmatch '(?m)^\s*ModTargets:\s*None\s*(#.*)?$'){throw 'Older A loader is not disabled; not changed by installer'}
Write-Output "Validated release $($manifest.version); destination=$target; legacy transition=$legacyActive; preserve settings=$($null -ne $preserved). No backups, save/cache/journal/log writes."
if(!$Install){Write-Output 'PREVIEW ONLY';return}
if($EvidencePath -and [IO.Path]::GetFullPath($EvidencePath).StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence output must be outside the game installation'}
if(!$PSCmdlet.ShouldProcess($target,'Install checked Trade Ledger package and retire only validated legacy payload files')){return}
$beforeHashes=@{};foreach($p in $existing.Keys){$beforeHashes[$p]=Hash $p}
# Fresh actual game/client/dedicated check, never inferred from owner history.
$processes=@(Get-Process -Name Empyrion,EmpyrionClient,EmpyrionDedicated -ErrorAction SilentlyContinue)
if($processes.Count){throw ('Game running; no writes: '+(($processes|ForEach-Object {$_.Name+' PID='+$_.Id}) -join ', '))}
if($nativeImageHash -and (Hash $nativeImagePath) -ne $nativeImageHash){throw 'Game image changed during validation; no writes; verify again'}
$handles=@{};$started=$false;$settingsBytes=$(if($null -ne $preserved){$preserved}else{[IO.File]::ReadAllBytes((Join-Path $stageRoot 'TradeLedger.settings'))})
try{
 foreach($p in $existing.Keys){try{$handles[$p]=[IO.File]::Open($p,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw "Exclusive-file preflight blocked before writes: $p; $($_.Exception.Message)"}}
 # Revalidate all locked existing payloads against the just-validated bytes. A changed
 # package must not become permission to overwrite it between preflight and mutation.
 foreach($p in $handles.Keys){$h=$handles[$p];$h.Position=0;$sha=[Security.Cryptography.SHA256]::Create();try{$lockedHash=[BitConverter]::ToString($sha.ComputeHash($h)).Replace('-','')}finally{$sha.Dispose()};$h.Position=0
  if($lockedHash -ne $beforeHashes[$p]){throw "Package changed during exclusive preflight: $p"}
 }
 function Put([string]$name,[byte[]]$bytes){$p=Join-Path $target $name;$h=$handles[$p];if($null -eq $h){$h=[IO.File]::Open($p,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$handles[$p]=$h};$h.SetLength(0);$h.Position=0;$h.Write($bytes,0,$bytes.Length);$h.Flush()}
 $started=$true;$null=New-Item -ItemType Directory -Path (Join-Path $target 'runtime') -Force
 $inactive=[Text.Encoding]::UTF8.GetBytes(([IO.File]::ReadAllText((Join-Path $stageRoot 'TradeLedger_Info.yaml')) -replace '(?m)^ModTargets:\s*Client\s*$','ModTargets: None'))
 Put 'TradeLedger_Info.yaml' $inactive
 foreach($n in $allowed){if($n -eq 'TradeLedger_Info.yaml'){continue};Put $n $(if($n -eq 'TradeLedger.settings'){$settingsBytes}else{[IO.File]::ReadAllBytes((Join-Path $stageRoot $n))})}
 $manifest.files.'TradeLedger.settings'=Digest $settingsBytes
 # Verify replacement bytes while still inactive and exclusively held, before
 # retiring the old loader. Do not treat a successful Write as verification.
 foreach($n in $allowed){$h=$handles[(Join-Path $target $n)];$h.Position=0;$sha=[Security.Cryptography.SHA256]::Create();try{$actual=[BitConverter]::ToString($sha.ComputeHash($h)).Replace('-','')}finally{$sha.Dispose()};$h.Position=0;$expected=$(if($n -eq 'TradeLedger_Info.yaml'){Digest $inactive}else{$manifest.files.$n});if($actual -ne $expected){throw "Inactive replacement verification mismatch: $n"}}
 $manifest|Add-Member -NotePropertyName deploymentState -NotePropertyValue 'preparing' -Force
 Put 'build-manifest.json' ((New-Object Text.UTF8Encoding($false)).GetBytes(($manifest|ConvertTo-Json -Depth 12)))
 # Retire the legacy loader before enabling the replacement. Never infer ownership
 # from folder/name alone: all of these exact bytes passed the installed manifest.
 if($legacyActive){foreach($n in @('TradeLedgerProbeB.dll')+@($oldAllowed|Where-Object {$_ -ne 'TradeLedgerProbeB.dll'})+@('build-manifest.json')){$p=Join-Path $legacy $n;$handles[$p].Dispose();$handles.Remove($p);[IO.File]::Delete($p)}}
 Put 'TradeLedger_Info.yaml' ([IO.File]::ReadAllBytes((Join-Path $stageRoot 'TradeLedger_Info.yaml')))
 $manifest|Add-Member -NotePropertyName deploymentState -NotePropertyValue 'verified' -Force
 $manifest|Add-Member -NotePropertyName installedRuntimeOptIn -NotePropertyValue ([Text.Encoding]::UTF8.GetString($settingsBytes) -match '(?m)^RuntimeObservation=on\r?$') -Force
 Put 'build-manifest.json' ((New-Object Text.UTF8Encoding($false)).GetBytes(($manifest|ConvertTo-Json -Depth 12)))
}catch{if($started){throw "Deployment outcome PARTIAL/UNCERTAIN; inspect actual package before retry: $($_.Exception.Message)"};throw}finally{foreach($h in $handles.Values){$h.Dispose()}}
foreach($n in $allowed){if((Hash (Join-Path $target $n)) -ne $manifest.files.$n){throw "Installed verification mismatch: $n"}}
if(@(Compare-Object ($allowed+@('build-manifest.json')) @(Names $target $true)).Count){throw 'Installed complete-file-set verification failed'}
if($legacyActive -and (Test-Path -LiteralPath (Join-Path $legacy 'TradeLedgerProbeB.dll'))){throw 'Legacy loader still present'}
if((Hash (Join-Path $target 'TradeLedger.settings')) -ne (Digest $settingsBytes)){throw 'Settings preservation mismatch'}
$evidence=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');status='INSTALLED VERIFIED';version=$manifest.version;target=$target;observedBuild=$observedBuild;nativeChanged=$nativeChanged;nativeCapabilities=$nativeAudit;processCheck='actual game/client/dedicated absent immediately before exclusive preflight';legacyRetired=$legacyActive;settingsPreserved=($null -ne $preserved);settingsHash=(Digest $settingsBytes);writeBoundary='exact owned mod payload only; no save/cache/journal/log/recovery writes';files=[ordered]@{}}
foreach($n in $allowed+@('build-manifest.json')){$evidence.files[$n]=Hash (Join-Path $target $n)}
if($EvidencePath){[IO.File]::WriteAllText([IO.Path]::GetFullPath($EvidencePath),($evidence|ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))}
$evidence|ConvertTo-Json -Depth 10

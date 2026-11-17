param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$cecil=Join-Path $managed 'Trivial.Mono.Cecil.dll'
$null=[Reflection.Assembly]::LoadFrom($cecil)
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\NativeInstallAudit.cs.txt'))) -ReferencedAssemblies $cecil,'System.Core','System.Web.Extensions'
$profile=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\NativeContracts5232.json'))
$watch=[Diagnostics.Stopwatch]::StartNew()
$results=@([LedgerInstallContractAudit]::Inspect((Join-Path $managed 'Assembly-CSharp.dll'),$profile))
$watch.Stop()
foreach($line in $results){Write-Output $line}
if($results.Count -ne 17 -or @($results|Where-Object {$_ -notmatch '=ready$'}).Count){throw 'Real current-image contract fallback failed; no native code was invoked'}
[ordered]@{checks=17;capabilities=$results;unknownImageMs=$watch.Elapsed.TotalMilliseconds;scope='Cecil static IL/operand/local/EH inspection; hash/token fast path never used; no native game code, hooks or instances';actualUnity='not tested'}|ConvertTo-Json -Depth 6

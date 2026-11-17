param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop';$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$profile=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\NativeContracts5232.json') -Raw|ConvertFrom-Json
$path=Join-Path $managed 'Assembly-CSharp.dll';if((Get-FileHash $path).Hash -ne $profile.Hash){throw 'Current-runtime source audit image mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'));. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try{
 if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Current-runtime MVID mismatch'}
 # Interface fields and literal type fences must point at real current native types.
 foreach($file in Get-ChildItem (Join-Path $root 'src\TradeLedgerProbeB') -Filter '*.cs'){
  $s=[IO.File]::ReadAllText($file.FullName)
  foreach($m in [regex]::Matches($s,'"(Assembly-CSharp\.[\w`+]+)"')){if($m.Groups[1].Value -ne 'Assembly-CSharp.dll' -and !$a.MainModule.GetType($m.Groups[1].Value.Replace('+','/'))){throw "Unknown current native type literal: $($file.Name): $($m.Groups[1].Value)"}}
 }
 $families=@('LedgerTradeRuntime.cs','LedgerHandInRuntime.cs','LedgerProductionRuntime.cs')
 $ids=@(@(0x0600cd22,0x0600cd23,0x060058c6,0x0600cd24,0x06006067,0x060022b7,0x060022c1),@(0x06006aaa,0x0600ba92,0x06003380),@(0x06006c72,0x06004ef3))
 for($i=0;$i -lt $families.Count;$i++){
  $source=[IO.File]::ReadAllText((Join-Path $root ('src\TradeLedgerProbeB\'+$families[$i])))
  $body=[regex]::Match($source,'string\[\] signatures=\{(.*?)\};',[Text.RegularExpressions.RegexOptions]::Singleline).Groups[1].Value
  $signatures=@([regex]::Matches($body,'"([^"]+)"')|ForEach-Object {$_.Groups[1].Value})
  if($signatures.Count -ne $ids[$i].Count){throw 'Current runtime signature count mismatch'}
  for($j=0;$j -lt $signatures.Count;$j++){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$ids[$i][$j]);if($m.FullName.Replace('/','+') -cne $signatures[$j]){throw "Runtime hook signature disagrees with current image: $($families[$i]) $j"}}
 }
 $source=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerProductionLifecycleNative.cs'))
 $block=[regex]::Match($source,'return type==(.+?);').Value;$names=@([regex]::Matches($block,'"([^"]+)"')|ForEach-Object {$_.Groups[1].Value})
 $owners=@(foreach($id in @(0x06000f79,0x060010cc,0x060010ce,0x060010d0,0x060011fc)){[LedgerCurrentEvidence]::Lookup($a.MainModule,$id).DeclaringType.FullName})
 if(@(Compare-Object $owners $names).Count -or $names.Count -ne 5){throw 'Supported lifecycle families differ from audited producer block factories'}
 # A new native mode check is native code, not a new observer invocation. Validate it
 # as part of the current bag/toolbar context method; observer still uses fields only.
 $sync=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600af0b)
 $guard=@($sync.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Boolean World::DisconnectPane()'})
 if($guard.Count -ne 1 -or $guard[0].Next.OpCode.Name -notin @('brtrue','brtrue.s')){throw 'Reviewed native mode guard changed'}
 'PASS current5232 runtime source: all native type literals, 12 exact outcome/lifecycle signatures, five producer factories and added native mode guard; no game invocation'
}finally{$a.Dispose()}

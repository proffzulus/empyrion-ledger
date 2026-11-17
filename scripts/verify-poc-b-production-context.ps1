param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Production context target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString('D') -ne $contract.moduleMvid){throw 'Production context MVID mismatch'}
 # API Name is copied from the native world, whereas SolarSystemName comes from a different descriptor field.
 $bridge=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600B87C)
 $il=$bridge.Body.Instructions
 function Pair($first,$second) {
  for($i=0;$i -lt $il.Count-1;$i++){
   if($il[$i].Operand -is [Trivial.Mono.Cecil.MemberReference] -and $il[$i+1].Operand -is [Trivial.Mono.Cecil.MemberReference] -and
      [LedgerCurrentEvidence]::Canonical($il[$i].Operand.MetadataToken.ToInt32()) -eq $first -and [LedgerCurrentEvidence]::Canonical($il[$i+1].Operand.MetadataToken.ToInt32()) -eq $second){return $true}
  };return $false
 }
 if($bridge.Name -ne 'SetWorldData' -or !(Pair 0x04004F78 0x0400A823) -or !(Pair 0x04005D6E 0x0600B876)){throw 'API playfield/system role relationship changed'}
 foreach($row in @(@(0x0600B871,0x0400A823,'get_Name'),@(0x06006186,0x04005D71,'get_PlayfieldName'))){
  $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$row[0]);$b=$m.Body.Instructions
  if($m.Name -ne $row[2] -or $m.ReturnType.FullName -ne 'System.String' -or $b.Count -ne 3 -or $b[1].OpCode.Name -ne 'ldfld' -or [LedgerCurrentEvidence]::Canonical($b[1].Operand.MetadataToken.ToInt32()) -ne $row[1]){throw 'Playfield name getter contract changed'}
 }
 if([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600B876).Name -ne 'set_SolarSystemName'){throw 'System field assignment lost API semantic anchor'}
 $worldField=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x04004F4A)
 if($worldField.FieldType.FullName -ne [LedgerCurrentEvidence]::Lookup($a.MainModule,0x04005D71).DeclaringType.FullName -or $worldField.DeclaringType.FullName -ne [LedgerCurrentEvidence]::Lookup($a.MainModule,0x04004F78).DeclaringType.FullName){throw 'World/descriptor ownership relationship changed'}
 foreach($name in @('LedgerProductionNative.cs','LedgerDiscoveredNative.cs','LedgerNativeGeography.cs','LedgerNativeMarkerBackend.cs')){
  $source=[IO.File]::ReadAllText((Join-Path $root ('src\TradeLedgerProbeB\'+$name)))
  if($source -notmatch '0x(?:0400)?5d71' -or $source -match '0x(?:0400)?5d6e'){throw ('Incorrect playfield-name binding in '+$name)}
 }
 $source=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerProductionNative.cs'))
 if(!$source.Contains('LedgerProduction.ScopeRejection(c,app,player)') -or !$source.Contains('V<string>(0x4f78,w)!=c.Playfield') -or !$source.Contains('V<string>(0x5d71,pf)!=c.Playfield')){throw 'Physical save/player/world playfield gates missing'}
 'PASS production context: API Name <- world4F78; descriptor PlayfieldName5D71; SolarSystemName <-5D6E; four consumers preserve exact playfield identity (static IL only)'
} finally {$a.Dispose()}

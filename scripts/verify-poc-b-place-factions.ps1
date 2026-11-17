param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$gm=Join-Path $GameRoot 'Client\Empyrion_Data\Managed';$dll=Join-Path $gm 'Assembly-CSharp.dll'
$c=Get-Content (Join-Path $root 'src/TradeLedgerProbeB/contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash $dll).Hash -ne $c.hashes.'Assembly-CSharp.dll'){throw 'Faction native build mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $gm 'Trivial.Mono.Cecil.dll'));. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try{
 if($a.MainModule.Mvid.ToString() -ne $c.moduleMvid){throw 'Faction native MVID mismatch'}
 function Operand([int]$m,[int]$offset,[int]$expected){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$m).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or $i.Operand -isnot [Trivial.Mono.Cecil.MemberReference] -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $expected){throw ('Faction operand {0:X8}+{1:X4}' -f $m,$offset)}}
 Operand 0x06005bae 0x04d5 0x0400701c
 Operand 0x06005bae 0x0506 0x04005609
 Operand 0x06005bc2 0x0016 0x0400561d
 Operand 0x06005bd7 0x0015 0x04005609
 Operand 0x06000d4b 0x0009 0x040012f8
 Operand 0x06000d1f 0x006c 0x04001ba5
 Operand 0x06000d1f 0x0000 0x040012d8
 Operand 0x06000d35 0x0000 0x040012d7
 Operand 0x06000d2d 0x0049 0x040012d9
 Operand 0x06005bca 0x0001 0x06005bb3
 Operand 0x0600cb77 0x00f5 0x06000d2d
 # These are the independently checked five standing levels; never equate missing with Neutral.
 $levels=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06000d28).DeclaringType.NestedTypes|Where-Object Name -eq 'Level'
 $expected=@{Hostile=0;Unfriendly=1;Neutral=2;Friendly=3;Honored=4;NotSet=5}
 foreach($name in $expected.Keys){$field=$levels.Fields|Where-Object Name -eq $name;if(!$field -or $field.Constant -ne $expected[$name]){throw 'Relation level changed'}}
 $boundaries=@([LedgerCurrentEvidence]::Lookup($a.MainModule,0x06000d28).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldc.i4'}|ForEach-Object Operand)
 if(($boundaries -join ',') -ne '6000,12000,18000,24000'){throw 'Reputation thresholds changed'}
 $key=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06000d44).Body.Instructions
 if(@($key|Where-Object {$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -in @(40,32)}).Count -ne 2){throw 'Faction pair key packing changed'}
 $source=[IO.File]::ReadAllText((Join-Path $root 'src/TradeLedgerProbeB/LedgerNativePlaceFactions.cs'))
 foreach($forbidden in @('GetEntities','GetAllEntities','GetStructures','LedgerJson','Enqueue','0x06005bad','0x06000d2d','0x06000d1f')){if($source.Contains($forbidden)){throw ('Forbidden faction lookup path '+$forbidden)}}
 foreach($guard in @('MatchesCurrentSave','LedgerPlaceBrowser.Current','Current(data,app,c)','values.Clear()','race<matrix.Count','column<rowValues.Count','ReferenceEquals(F(0x12d9,converter),rep)')){if(!$source.Contains($guard)){throw ('Faction fence missing '+$guard)}}
 $tokens=@(0x06005bae,0x06005bb3,0x06005bca,0x06005bc2,0x06005bd7,0x06000d44,0x06000d4b,0x06000d28,0x06000d2d,0x06000d1f,0x06000d35,0x06000d7c,0x06000d7d,0x06005bb6,0x0600cb77)
 $out=Join-Path $root 'artifacts/poc-b/verification/places-factions';$null=New-Item -ItemType Directory -Force $out
 [ordered]@{sha256=(Get-FileHash $dll).Hash;mvid=$a.MainModule.Mvid.ToString();proof='Static IL: config id<100 ->group/id0, exact native faction colour distinct from reputation; player-pair explicit entry OR actual initialized race/faction matrix; native reset clears current standings; no native invocation';methods=@(foreach($token in $tokens){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);[ordered]@{token=('{0:X8}'-f$token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object ToString)}})}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $out 'native-contract.json') -Encoding UTF8
 $u=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $gm 'UnityEngine.CoreModule.dll'))
 try{
  $m=$u.MainModule.GetType('UnityEngine.Color32').Methods|Where-Object {$_.Name -eq 'op_Implicit' -and $_.ReturnType.FullName -eq 'UnityEngine.Color32'}
  $round=@($m.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Single UnityEngine.Mathf::Round(System.Single)'})
  if($round.Count -ne 4){throw 'Native faction colour rounding changed'}
  [ordered]@{path=(Join-Path $gm 'UnityEngine.CoreModule.dll');sha256=(Get-FileHash (Join-Path $gm 'UnityEngine.CoreModule.dll')).Hash;proof='Native Color32 Clamp01, float32 *255, Mathf.Round then byte; not truncation';il=@($m.Body.Instructions|ForEach-Object ToString)}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'native-colour.json') -Encoding UTF8
 }finally{$u.Dispose()}
 'PASS faction identity/colour, player-pair reputation keys/levels and initialized matrix; static only'
}finally{$a.Dispose()}

param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Producer interaction target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString('D') -ne $contract.moduleMvid){throw 'Producer interaction MVID mismatch'}
 function Operand([int]$method,[int]$offset,[int]$token){
  $i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset
  if(!$i -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $token){throw ('Interaction semantic binding changed: {0:X8}+{1:X4}' -f $method,$offset)}
 }
 # Actual Bag controller: Props getter, same WindowStub transform and local-world actor.
 $bag=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400AD20).DeclaringType
 if($bag.FullName -ne 'Assembly-CSharp.FormSiteConnectionToken' -or [LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400AD25).DeclaringType -ne $bag -or [LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400AD27).DeclaringType -ne $bag){throw 'Bag controller ownership changed'}
 Operand 0x0600BE75 0x0001 0x0400AD20
 Operand 0x0600BE77 0x0007 0x0400AD27
 Operand 0x0600BE79 0x0007 0x040064E6
 Operand 0x0600BE79 0x000C 0x06006C05
 Operand 0x0600BE79 0x0016 0x0400AD25
 # Producer window root/model and published output callback chain.
 Operand 0x0600C475 0x0001 0x0400B4DC
 Operand 0x0600C47A 0x0007 0x0400B4DD
 Operand 0x0600C47D 0x0008 0x0400B4DE
 Operand 0x0600BBCC 0x001F 0x06005849
 Operand 0x06005849 0x0006 0x060033ED
 # Existing verifier also checks owner construction, fuel arithmetic/label formatting,
 # native terrain-position conversion, capacity/full state and read-only helper bodies.
 $reader=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerProductionNative.cs'))
 foreach($literal in @('Assembly-CSharp.FormSiteConnectionToken','!ui.RootReady(bagRoot)','!ui.Same(F(0xad27,bag),ui.Transform(bagRoot))','!Config(F(0xc186,bagEntry),F(0xad20,bag),bagRoot)','V<int>(0x1b31,owner)!=localPlayer','!ui.Same(ui.Root(stub),root)||V<bool>(0xc197,stub)','LedgerProduction.ReadOwner(F,model)','!LedgerProduction.LocalOwner(owner,player)','!Published(published,stacks)','WindowView("ItemGenerator",out producerRoot)','new LedgerProductionUnityAccess(module)')){
  if(!$reader.Contains($literal)){throw ('Missing real interaction gate: '+$literal)}
 }
 if($reader.Contains('Assembly-CSharp.DeviceInvokerStoreSite')){throw 'Old Bag controller literal still present'}
 $out=Join-Path $root 'artifacts\poc-b\verification\device-ui-5229'
 $null=New-Item -ItemType Directory -Force $out
 # Audit every direct native field/method referenced in this reader, including downstream bindings.
 $tokens=[Collections.Generic.HashSet[int]]::new()
 foreach($m in [regex]::Matches($reader,'\b(F|V<[^>]+>|Invoke|M)\(0x([0-9a-f]{4})')){
  $prefix=if($m.Groups[1].Value -in @('Invoke','M')){0x06000000}else{0x04000000}
  $null=$tokens.Add($prefix+[Convert]::ToInt32($m.Groups[2].Value,16))
 }
 $rows=@(foreach($token in ($tokens|Sort-Object)){$member=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);[ordered]@{token=('{0:X8}' -f $token);signature=$member.FullName;owner=$member.DeclaringType.FullName}})
 $proof=[ordered]@{hash=$contract.hashes.'Assembly-CSharp.dll';mvid=$contract.moduleMvid;bagController=$bag.FullName;fieldsAndCalls=$rows;methods=@(foreach($token in @(0x0600BE75,0x0600BE77,0x0600BE79,0x0600C475,0x0600C47A,0x0600C47D)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);[ordered]@{token=('{0:X8}' -f $token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}});limits='Static current1.20 contract evidence; no native game method invocation or Unity timing acceptance'}
 $proof|ConvertTo-Json -Depth 7|Set-Content (Join-Path $out 'native-interaction-contract.json') -Encoding UTF8
 'PASS producer interaction: real5229 Bag registration/transform/local actor, producer publication and downstream binding audit; no guard bypass or game invocation'
} finally {$a.Dispose()}

param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed';$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Discovered object target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString() -ne $contract.moduleMvid){throw 'Discovered object MVID mismatch'}
 function Operand([int]$method,[int]$offset,[int]$expected){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or $i.Operand -isnot [Trivial.Mono.Cecil.MemberReference] -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $expected){throw ('Object contract mismatch {0:X8}+{1:X4}' -f $method,$offset)}}
 # Local event24: arg1 is the POI GROUP (not display name), arg2 local actor,
 # arg3 precisely the native persisted record. Event is after personal bookkeeping.
 Operand 0x060053fe 0x01a6 0x060051c5
 Operand 0x060053fe 0x01ae 0x060053b0
 $event=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060053fe).Body.Instructions|Where-Object Offset -eq 0x024a
 if($event.OpCode.Name -ne 'ldc.i4.s' -or [int]$event.Operand -ne 24){throw 'POI event changed'}
 $fence=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060053fe).Body.Instructions|Where-Object Offset -eq 0x01b3
 if($fence.OpCode.Name -ne 'bne.un' -or $fence.Operand.Offset -ne 0x02a1){throw 'POI local event fence changed'}
 Operand 0x060053fe 0x025e 0x04001e9a
 Operand 0x060053fe 0x0264 0x04004f43
 Operand 0x060053fe 0x026f 0x04005085
 Operand 0x060053fe 0x0281 0x06006be1
 Operand 0x060051c8 0x0001 0x04004dcb
 Operand 0x060051c8 0x0012 0x060079cd
 Operand 0x060079cd 0x0024 0x060079c4
 Operand 0x0600377b 0x0001 0x0400334a
 Operand 0x0600377b 0x000c 0x0a00180e
 # Map hover publishes the exact selected marker, then derives its bound body and
 # label synchronously. Do not call that formatter (it can mutate native caches).
 Operand 0x0600c523 0x0324 0x0600c525
 Operand 0x0600c523 0x0329 0x0400b634
 Operand 0x0600c523 0x0357 0x0400b634
 Operand 0x0600c523 0x035c 0x06003741
 Operand 0x0600c523 0x03e0 0x0400b634
 Operand 0x0600c523 0x03e5 0x06003754
 Operand 0x0600c523 0x03f1 0x0400b5bc
 Operand 0x0600c523 0x03fd 0x0a0016d7
 # Exact direct saved ID read/write and concrete prefab/header lineage.
 Operand 0x0600202d 0x007a 0x04001e81
 Operand 0x0600202e 0x0081 0x04001e81
 Operand 0x0600af38 0x009e 0x04001e8c
 Operand 0x0600af38 0x00fd 0x04001e99
 Operand 0x0600af38 0x010a 0x04001e9a
 Operand 0x06001f65 0x0070 0x04001e99
 Operand 0x06001f65 0x0075 0x04001cee
 # Native names and actual faction fields, no formatter/cache getter calls.
 Operand 0x06001d29 0x0001 0x04001c15
 Operand 0x06001bc0 0x0001 0x04001ba6
 # Shared orbital parent: Space=2 switch branch assigns sector name, copied to PF5D4C.
 $switch=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005f67).Body.Instructions|Where-Object Offset -eq 0x04d4
 if($switch.OpCode.Name -ne 'switch' -or $switch.Operand[1].Offset -ne 0x04f2){throw 'Native orbital parent switch changed'}
 Operand 0x06005f67 0x04f5 0x04005985
 Operand 0x06005f67 0x070d 0x04005985
 Operand 0x06005f67 0x0712 0x04005d72
 if((Get-FileHash -LiteralPath (Join-Path $managed 'ModApi.dll')).Hash -ne $contract.hashes.'ModApi.dll'){throw 'Object public enum assembly hash changed'}
 $api=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'ModApi.dll'))
 try{$space=$api.MainModule.GetType('PlayfieldType').Fields|Where-Object Name -eq 'Space';if($space.Constant -ne 2){throw 'Native space classification changed'}}finally{$api.Dispose()}
 # Read helpers are inspected transitively through custom Contains. The ctor only
 # writes a NEW boxed value's FactionData, never an existing discovery collection.
 foreach($token in @(0x060051c8,0x060079cd,0x060079c4,0x0600377b,0x0600377d,0x060053af,0x060053b0)){
  foreach($i in [LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token).Body.Instructions){if($i.OpCode.Name -match '^(stfld|stsfld|stelem|newobj)'){throw 'Object read helper now writes/allocates native state'}}
 }
 $expected=@{0x04001e81='System.Int32';0x04001e7e='UnityEngine.Vector3';0x04001e8c='System.String';0x04001e99='System.String';0x04001e9a='System.String';0x04001ea6='Assembly-CSharp.DirectoryDictionary';0x04001cee='System.String';0x04001cef='System.String';0x04003332='Assembly-CSharp.ReferenceHelper';0x04003331='System.Boolean';0x0400332c='System.Int32';0x0400332d='Assembly-CSharp.VectorHelper';0x04001b41='UnityEngine.Vector3';0x04001ba6='Assembly-CSharp.DirectoryDictionary';0x04001305='FactionData';0x04005d72='System.String';0x0400b634='Assembly-CSharp.DirectorySet';0x0400b5bc='UILabel'}
 foreach($t in $expected.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t).FieldType.FullName -ne $expected[$t]){throw ('Object field type changed {0:X8}' -f $t)}}
 $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\LedgerDiscoveredNative.cs'))
 if($source -match 'ToArray\(|LedgerJson\.|GetEntities|GetAllEntities|Invoke\(0x(5191|5192|3721|1c5a|7d0d)' -or $source -match 'F\(0x(4f18|506c)'){throw 'Forbidden object scan/mutation/serialization path'}
 if(!$source.Contains('Member(w,entityId,2)') -or !$source.Contains('ReferenceEquals(Registered(w,2,entityId),marker)') -or !$source.Contains('!ReferenceEquals(F(0x3332,marker),entity)') -or !$source.Contains('!Entity(entity).Equals(s)')){throw 'Object final revalidation missing'}
 if(!$source.Contains('queued<2') -or !$source.Contains('Math.Min(8,size)') -or !$source.Contains('if(!LedgerDiscoveredObjects.Valid(o)||!submit(o))return false;')){throw 'Object budget/admission cache contract changed'}
 $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\discovered-objects';$null=New-Item -ItemType Directory -Path $out -Force
 $tokens=@(0x060053fe,0x060051c8,0x060079cd,0x060079c4,0x0600377b,0x0600377d,0x06000d56,0x0600202d,0x0600202e,0x0600af38,0x06001f65,0x06005f67,0x06001d29,0x06001bc0,0x0600c523)
 $record=[ordered]@{assemblyHash=(Get-FileHash -LiteralPath $path).Hash;mvid=$a.MainModule.Mvid.ToString();proof='local event24 OR personally-discovered native marker binding OR already-visible Map hover; no world entity enumeration';limits='static exact-build proof, no live Unity callback/save-load timing acceptance';methods=@(foreach($token in $tokens){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);[ordered]@{token=('{0:X8}' -f $token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object ToString)}})}
 [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $out 'native-contract.json')),($record|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
 'Discovered object native contracts passed (static Cecil only).'
}finally{$a.Dispose()}

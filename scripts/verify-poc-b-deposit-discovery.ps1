param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed';$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Discovery native hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'));. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Discovery MVID mismatch'}
 function Operand([int]$method,[int]$offset,[int]$expected){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $expected){throw ('Discovery contract mismatch {0:X8}+{1:X4}' -f $method,$offset)}}
 Operand 0x060053a0 0x0045 0x060037b0
 Operand 0x060053a0 0x0050 0x0400506f
 Operand 0x060053fe 0x000b 0x060053b0
 Operand 0x060053fe 0x0039 0x04004f34
 Operand 0x060053fe 0x0040 0x0600377d
 Operand 0x060053fe 0x004e 0x0600374a
 Operand 0x060053fe 0x00b1 0x06003754
 Operand 0x060053fe 0x01fd 0x04004f43
 Operand 0x060053fe 0x0219 0x06006be1
 Operand 0x0600374a 0x000b 0x04003331
 Operand 0x060037b8 0x0018 0x060037b1
 Operand 0x060037b1 0x0129 0x0400336a
 Operand 0x060037b0 0x00c6 0x060037b1
 $emit=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060053fe).Body.Instructions
 foreach($pair in @(@(0x01df,23),@(0x004d,1))){$i=$emit|Where-Object Offset -eq $pair[0];if([int]$i.Operand -ne $pair[1] -and $i.OpCode.Name -ne ('ldc.i4.'+$pair[1])){throw 'Discovery event/discovered flag constant changed'}}
 $local=$emit|Where-Object Offset -eq 0x01b3;if($local.OpCode.Name -ne 'bne.un' -or $local.Operand.Offset -ne 0x02a1){throw 'Discovery local-player event fence changed'}
 $override=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060037b8);if(!$override.IsVirtual -or $override.IsNewSlot){throw 'Discovery native virtual formatter dispatch changed'}
 $bridge=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06006be1).Body.Instructions|Where-Object Offset -eq 0x00f5
 if([LedgerCurrentEvidence]::Canonical($bridge.Operand.Resolve().MetadataToken.ToInt32()) -ne 0x0600b7ea){throw 'Discovery event bridge changed'}
 $sync=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600b7ea).Body.Instructions|Where-Object Offset -eq 0x0014
 if($sync.Operand.FullName -notmatch 'GameEventDelegate::Invoke'){throw 'Discovery event is no longer synchronously delivered'}
 foreach($token in @(0x0600377d,0x060053af,0x060053b0,0x060037ba)){foreach($i in [LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token).Body.Instructions){if($i.OpCode.Name -in @('stfld','stsfld','stelem.ref')){throw 'Discovery read helper writes state'}}}
 # Native cache generation is precise internally then rounded to10; 0 bucket is literal <5%.
 foreach($offset in @(0x0014,0x0019)){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060037b1).Body.Instructions|Where-Object Offset -eq $offset;if([int]$i.Operand -ne 10){throw 'Native percentage rounding changed'}}
 $f=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400506f);if($f.FieldType.FullName -ne 'Assembly-CSharp.TreeNodeManager' -or $f.IsStatic){throw 'Direct resource marker field changed'}
 $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\LedgerProductionNative.cs'))
 if($source -match 'Invoke\(0x(377e|3785|3721|54ed)' -or $source -match '0x(504c|504d|5055)'){throw 'Observer invokes native refresh or reads precise depletion counters'}
 $proof=@(foreach($token in @(0x060053a0,0x060053fe,0x0600374a,0x0600377d,0x060037b0,0x060037b1,0x060037b8,0x060053af,0x060053b0,0x06006be1,0x0600b7ea)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);[ordered]@{token=('{0:X8}' -f $token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object ToString)}})
 $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\scanner-discovery';$null=New-Item -ItemType Directory -Path $out -Force
 $record=[ordered]@{assemblyHash=(Get-FileHash -LiteralPath $path).Hash;mvid=$a.MainModule.Mvid.ToString();boundary='Local event23 after native discovered flag and cache refresh; synchronous API bridge. Exact point lookup, no resource enumeration, no later cache replay.';limitation='Absent/unregistered logical marker or off-API-thread event remains identity-only/unrecorded; no unproven asynchronous cache admission.';methods=$proof}
 [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $out 'native-contract.json')),($record|ConvertTo-Json -Depth 7),(New-Object Text.UTF8Encoding($false)))
 'Offscreen discovery native contracts passed (Cecil only; no game execution).'
} finally {$a.Dispose()}

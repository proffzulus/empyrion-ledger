param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Distance target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString() -ne $contract.moduleMvid){throw 'Distance MVID mismatch'}
 function At($method,$offset){$i=@([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$method).Body.Instructions|Where-Object Offset -eq $offset);if($i.Count -ne 1){throw 'Distance instruction missing'};return $i[0]}
 function Ref($method,$offset,$token){$i=At $method $offset;if(!$i.Operand -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $token){throw ('Distance operand mismatch '+$method+' / '+$offset)}}
 function Constant($method,$offset,$value){$i=At $method $offset;if($i.OpCode.Name -ne 'ldc.r4' -or [Math]::Abs([double]$i.Operand-$value) -gt 0.00001){throw 'Distance conversion changed'}}
 # Exact map origins: pane2 uses stellar coords; other pane uses sector coords.
 Ref 0x060070a1 0x0018 0x04005d70
 Ref 0x060070a1 0x002e 0x04005d6f
 Ref 0x060070a1 0x00b2 0x060070a5
 Ref 0x060070a5 0x0002 0x06005f03
 $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005f03)
 if($m.ReturnType.FullName -ne 'System.Single' -or $m.Parameters.Count -ne 2 -or @($m.Parameters|Where-Object {$_.ParameterType.FullName -ne 'Vector3i'}).Count){throw 'Distance signature mismatch'}
 $subtract=(At 0x06005f03 0x0002).Operand.Resolve()
 if(@($subtract.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'sub'}).Count -ne 3){throw 'Vector3i subtraction changed'}
 if((At 0x06005f03 0x0012).Operand.FullName -ne 'System.Single UnityEngine.Vector3::get_magnitude()'){throw 'Distance norm changed'}
 Constant 0x06005f03 0x0017 0.1
 Constant 0x060070a1 0x0145 10000
 if((At 0x060070a1 0x014a).OpCode.Name -ne 'div'){throw 'Stellar map conversion changed'}
 Ref 0x06005eeb 0x0001 0x0400593d
 Ref 0x06005eeb 0x0007 0x06005f03
 Constant 0x060037ac 0x0031 150000
 Constant 0x060037ac 0x0037 1000
 # Static resource decoding, never invoke an obfuscated game string getter.
 $bytes=($a.MainModule.Resources|Where-Object Name -eq 'Assembly-CSharp$').GetResourceData()
 if($bytes[3] -ne 253){throw 'Distance unit resource format'}
 $des=[Security.Cryptography.DES]::Create()
 try{$des.IV=$bytes[4..11];$des.Key=$bytes[12..19];$tr=$des.CreateDecryptor();try{$table=$tr.TransformFinalBlock($bytes,20,$bytes.Length-20)}finally{$tr.Dispose()}}finally{$des.Dispose()}
 foreach($pair in @(@(454816,'utsLY'),@(689858,'utsAU'),@(455131,'{0:F1}{1}'))){
  $pos=[int]$pair[0];$v=[int]$table[$pos];$pos++
  if(($v-band 128)-eq 0){$len=$v}elseif(($v-band 64)-eq 0){$len=(($v-band 127)-shl 8)-bor [int]$table[$pos];$pos++}else{throw 'Unexpected distance unit string length'}
  if([Text.Encoding]::Unicode.GetString($table,$pos,$len) -ne $pair[1]){throw 'Native distance unit mismatch'}
 }
 Write-Output 'PASS distance exact hash/MVID, PF/system/sector frame selection, Euclidean subtraction/norm, 0.1 AU and /10000 ly, native unit keys/F1 and 150000*1000 AU scale (static IL/resource reads only)'
} finally {$a.Dispose()}

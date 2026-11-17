[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CecilPath)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$original=Join-Path $workspace 'vendor\Harmony-2.3.3\0Harmony.dll'
$folder=Join-Path $workspace 'vendor\Harmony-2.3.3-fieldtypefix'
$output=Join-Path $folder '0Harmony.dll'
if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne '4C06BB6D9D75785F9B64FD551FC75D54AF9D5A5458A43CFB5BF52A5A22B24E6E') { throw 'Wrong original Harmony: refusing library modification' }
$null=New-Item -ItemType Directory -Path $folder -Force
$null=[Reflection.Assembly]::LoadFrom($CecilPath)
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($original)
try {
 if($a.Name.HasPublicKey) { throw 'Unexpected signed library; do not remove signing or validation' }
 $type=$a.MainModule.Types | Where-Object {$_.FullName -eq 'MonoMod.Utils.Extensions'}
 $m=@($type.Methods | Where-Object {$_.Name -eq 'Is' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'Mono.Cecil.MemberReference' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Reflection.MemberInfo'})
 if($m.Count -ne 1) { throw 'Ambiguous exact field matcher' }
 $m=$m[0]
 # The pinned method checks FieldReference/FieldInfo kinds and then continues to PropertyReference.
 # Insert a FieldType comparison only in that already-name-and-declaring-type-matched field branch.
 $propertyChecks=@($m.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'Mono.Cecil.PropertyReference'})
 if($propertyChecks.Count -ne 1) { throw 'Pinned matcher shape changed' }
 $anchor=$propertyChecks[0].Previous
 if($anchor.OpCode.Name -ne 'ldarg.0') { throw 'Pinned field matcher anchor changed' }
 $branch=$anchor.Previous.Previous.Previous
 if($branch.OpCode.Name -ne 'beq.s' -or $branch.Operand -ne $anchor) { throw 'Pinned field-kind branch changed' }
 $fieldRef=($m.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'Mono.Cecil.FieldReference'} | Select-Object -Last 1).Operand
 $fieldInfo=($m.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'System.Reflection.FieldInfo'} | Select-Object -Last 1).Operand
 $getCecilType=New-Object Trivial.Mono.Cecil.MethodReference('get_FieldType',$a.MainModule.GetType('Mono.Cecil.TypeReference'),$fieldRef)
 $getCecilType.HasThis=$true
 $getRuntimeType=$a.MainModule.ImportReference([Reflection.FieldInfo].GetProperty('FieldType').GetGetMethod())
 $cecilType=$a.MainModule.GetType('Mono.Cecil.TypeReference')
 $getDeclaringType=New-Object Trivial.Mono.Cecil.MethodReference('get_DeclaringType',$cecilType,$a.MainModule.GetType('Mono.Cecil.MemberReference'))
 $getDeclaringType.HasThis=$true
 $hasGenerics=New-Object Trivial.Mono.Cecil.MethodReference('get_HasGenericParameters',$a.MainModule.TypeSystem.Boolean,$cecilType)
 $hasGenerics.HasThis=$true
 $isGenericInstance=New-Object Trivial.Mono.Cecil.MethodReference('get_IsGenericInstance',$a.MainModule.TypeSystem.Boolean,$cecilType)
 $isGenericInstance.HasThis=$true
 $il=$m.Body.GetILProcessor()
 $first=$il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldarg_0)
 $pop=$il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Pop)
 $genericPop=$il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Pop)
 $insert=@(
  $first,
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Isinst,$fieldRef),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brfalse_S,$pop),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$getDeclaringType),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$hasGenerics),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brtrue_S,$genericPop),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$getDeclaringType),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$isGenericInstance),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brtrue_S,$genericPop),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$getCecilType),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldarg_1),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Castclass,$fieldInfo),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt,$getRuntimeType),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Call,$m),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ret),
  $genericPop,
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldc_I4_1),
  $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ret),
  $pop
 )
 foreach($i in $insert) { $il.InsertBefore($anchor,$i) }
 $branch.Operand=$first
 # Stable identity for the local derivative, no random MVID or current PE timestamp.
 $a.MainModule.Mvid=[Guid]'389b72a1-d725-4c72-8937-fb72465f0061'
 $a.Write($output)
} finally { $a.Dispose() }
$bytes=[IO.File]::ReadAllBytes($output)
$pe=[BitConverter]::ToInt32($bytes,0x3c)
[Array]::Clear($bytes,$pe+8,4)
[IO.File]::WriteAllBytes($output,$bytes)
foreach($name in @('LICENSE','THIRD-PARTY-LICENSES.txt','MonoMod.Core-LICENSE.txt')) { [IO.File]::Copy((Join-Path $workspace ('vendor\Harmony-2.3.3\'+$name)),(Join-Path $folder $name),$true) }
$notice=@'

LOCAL MODIFICATION — Trade Ledger POC B, 2026-10-16:
Derived from pinned Lib.Harmony 2.3.3 lib/net452/0Harmony.dll (MIT notices above).
In merged MonoMod.Utils.Extensions.Is(MemberReference, MemberInfo), add field-type
comparison for nongeneric declaring types after declaring-type/name/kind checks.
Generic declaring types retain upstream behavior; generic collisions are not fixed.
This prevents
same-name, different-type fields being resolved to the first field during IL copy.
No game DLL modified. This dependency correction itself installs no runtime
Harmony patch; Trade Ledger separately uses Harmony for its guarded observers.
Reproduce with scripts/build-harmony-fieldtype-fix.ps1. This is a locally modified
library, NOT an unmodified publisher binary. Compatibility must be verified
for the supported game contracts; this notice makes no compatibility guarantee.
'@
[IO.File]::AppendAllText((Join-Path $folder 'THIRD-PARTY-LICENSES.txt'),$notice,(New-Object Text.UTF8Encoding($false)))
$provenance=[ordered]@{basePackage='Lib.Harmony 2.3.3 lib/net452';baseSha256=(Get-FileHash -LiteralPath $original).Hash;localSha256=(Get-FileHash -LiteralPath $output).Hash;change='MonoMod.Utils.Extensions.Is nongeneric field-type equality after declaring type/name/kind; generic matching remains upstream';recipe='scripts/build-harmony-fieldtype-fix.ps1';runtimeGameValidation='NOT RUN';license='MIT with all original notices and local modification notice';signing='Original has no public key; no signature enforcement removed'}
$provenance | ConvertTo-Json | Set-Content (Join-Path $folder 'provenance.json') -Encoding UTF8
Write-Output ('Local field-type repair SHA256='+$provenance.localSha256)

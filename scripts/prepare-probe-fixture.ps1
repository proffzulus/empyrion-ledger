[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AdapterPath,[Parameter(Mandatory=$true)][string]$CecilPath)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$path=[IO.Path]::GetFullPath($AdapterPath)
if(!$path.StartsWith((Join-Path $workspace 'artifacts\poc-b\build\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture output must stay in own workspace build directory'}
$null=[Reflection.Assembly]::LoadFrom($CecilPath)
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
$temp=$path+'.fixture-prepared'
try {
 $type=$a.MainModule.Types | Where-Object {$_.FullName -eq 'TradeLedgerProbeB.Runtime.StaticCollisionFixture'}
 if(!$type -or $type.Fields.Count -ne 2){throw 'Owned fixture shape changed'}
 foreach($name in @('integerValue','textValue')) {
  $field=@($type.Fields | Where-Object {$_.Name -ceq $name})
  if($field.Count -ne 1 -or !$field[0].IsPrivate){throw 'Owned fixture field contract changed'}
  $field[0].Name='containerLength'
 }
 $a.Write($temp)
} finally {$a.Dispose()}
$bytes=[IO.File]::ReadAllBytes($temp)
$pe=[BitConverter]::ToInt32($bytes,0x3c)
[Array]::Clear($bytes,$pe+8,4)
[IO.File]::WriteAllBytes($path,$bytes)
# Preserve the intermediate for inspection; never modify any game assembly.
Write-Output 'Prepared owned static collision fixture; only its private field names changed.'

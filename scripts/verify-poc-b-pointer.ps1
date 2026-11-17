[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'NGUIAssembly.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '891051A984A8C8D3FA8C113B0DE76E399E3E2143AF40CEB866D3F74C9F3AD61D'){throw 'Pointer NGUI hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    foreach($spec in @(@(0x04000408,'useMouse',$false),@(0x04000409,'useTouch',$false),@(0x04000421,'showTooltips',$true))) {
        $f=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$spec[0]);if($f.Name -ne $spec[1] -or $f.IsStatic -ne $spec[2] -or $f.FieldType.FullName -ne 'System.Boolean' -or $f.DeclaringType.Name -ne 'UICamera'){throw 'Pointer field mismatch'}
    }
    $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600068D);$il=@($m.Body.Instructions)
    foreach($offset in 0x12,0x22,0x65){$i=$il|Where-Object Offset -eq $offset;if($i.OpCode.Name -ne 'ldfld' -or $i.Operand.DeclaringType.Name -ne 'UICamera'){throw 'Pointer event gate mismatch'}}
    if(($il|Where-Object Offset -eq 0x6A).Operand.Offset -ne 0x158){throw 'Scroll/tooltip block is no longer guarded by useMouse'}
    foreach($token in 0x0600066D,0x06000697){$v=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);if(!$v.IsStatic -or $v.Parameters.Count -ne 1 -or $v.Parameters[0].ParameterType.FullName -ne 'UnityEngine.GameObject'){throw 'Pointer cleanup signature mismatch'}}
    $setter=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600066D);$names=@($setter.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
    if('OnHover' -notin $names -or 'OnClick' -in $names -or 'OnPress' -in $names){throw 'Pointer clear notification contract changed'}
    Write-Output 'PASS pointer current-profile: useMouse/useTouch gate mouse press/hover; useMouse gates wheel+tooltip; native null-hover/tooltip cleanup, no patch or gameplay action. Live cockpit confirmation pending.'
} finally {$a.Dispose()}

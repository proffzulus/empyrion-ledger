param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Geography target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    $expected=@{0x04005d6f='Vector3i';0x04005d70='Vector3i';0x04005d71='System.String';0x04005db3='PlayfieldType';0x04005d77='SectorMapObjectType';0x04005dab='System.Boolean'}
    foreach($token in $expected.Keys){$f=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);if($f.DeclaringType.FullName -ne 'Assembly-CSharp.AddinScopeConnectionToken' -or $f.FieldType.FullName -ne $expected[$token]){throw 'Geography field mismatch'}}
    # Sector-loader copies a sector YAML record's Coordinates to each playfield descriptor, not its per-playfield offset.
    $ins=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005f67).Body.Instructions;$proved=$false
    for($i=2;$i -lt $ins.Count;$i++){
        if($ins[$i].OpCode.Name -eq 'stfld' -and [LedgerCurrentEvidence]::Canonical($ins[$i].Operand.MetadataToken.ToInt32()) -eq 0x04005d70 -and
            $ins[$i-2].Operand -is [Trivial.Mono.Cecil.MethodReference] -and [LedgerCurrentEvidence]::Canonical($ins[$i-2].Operand.MetadataToken.ToInt32()) -eq 0x060069b9){$proved=$true}
    }
    if(!$proved){throw 'Shared sector Coordinates -> PF field evidence changed'}
    $kind=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06006188)
    if($kind.ReturnType.FullName -ne 'PlayfieldType' -or [LedgerCurrentEvidence]::Canonical($kind.Body.Instructions[1].Operand.MetadataToken.ToInt32()) -ne 0x04005db3){throw 'Playfield type getter changed'}
    $moon=$a.MainModule.GetType('SectorMapObjectType').Fields|Where-Object{$_.Name -eq 'Moon'}
    if($moon.Constant -ne 7){throw 'Native map moon classification changed'}
    Write-Output 'PASS geography field types, typed body getter and shared sector YAML Coordinates -> PF descriptor (static IL, no game methods)'
} finally {$a.Dispose()}

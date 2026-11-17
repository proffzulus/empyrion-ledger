param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Trader timer target hash mismatch'}
Add-Type -Path (Join-Path $managed 'Trivial.Mono.Cecil.dll')
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Trader timer MVID mismatch'}
    foreach($entry in @(@(0x0400533a,'System.UInt16'),@(0x0400533f,'System.UInt64'))){
        $field=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$entry[0])
        if($field.IsStatic -or $field.DeclaringType.FullName -ne 'Assembly-CSharp.ActionEditor' -or $field.FieldType.FullName -ne $entry[1]){throw 'Trader interval/deadline field mismatch'}
    }
    function Member([int]$method,[int]$offset,[int]$token){
        $i=@([LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset)
        if($i.Count -ne 1 -or [LedgerCurrentEvidence]::Canonical($i[0].Operand.MetadataToken.ToInt32()) -ne $token){throw ('Trader timer IL {0:X8}+{1:X4}' -f $method,$offset)}
    }
    Member 0x060058b1 0x0032 0x040070a8
    Member 0x060058b1 0x0038 0x0400533f
    Member 0x060058b1 0x0046 0x060058b2
    Member 0x06006c93 0x0016 0x060058b1
    Member 0x060058b2 0x0006 0x040070a8
    Member 0x060058b2 0x000e 0x0400533a
    Member 0x060058b2 0x0019 0x0400533f
    Member 0x060058ae 0x0042 0x0400533f
    Member 0x060058af 0x007e 0x0400533f
    Member 0x0600b7b8 0x0005 0x040070a8
    $refresh=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060058b2).Body.Instructions
    foreach($entry in @(@(0x000b,20),@(0x0014,60))){$i=$refresh|Where-Object Offset -eq $entry[0];if([int]$i.Operand -ne $entry[1]){throw 'Trader time unit changed'}}
    $comparison=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060058b1).Body.Instructions|Where-Object Offset -eq 0x003d
    if($comparison.OpCode.Name -notin @('ble.un','ble.un.s')){throw 'Strict native eligibility boundary changed'}
    foreach($token in @(0x060058b3,0x060058b4)){
        foreach($i in [LedgerCurrentEvidence]::Lookup($a.MainModule,$token).Body.Instructions){
            if($i.OpCode.Name -eq 'stfld' -and [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -in @(0x0400533a,0x0400533f)){throw 'Trades now reset timer'}
        }
    }
    $rows=@(foreach($token in @(0x060058b1,0x060058b2,0x060058ae,0x060058af,0x060058b3,0x060058b4,0x06006c93,0x0600b7b8,0x06006ab2)){
        $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);[ordered]@{token=('{0:X8}' -f $token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}
    })
    $folder=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\offer-freshness';$null=New-Item -ItemType Directory -Force $folder
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $folder 'native-contract.json')),([ordered]@{hash=(Get-FileHash $path).Hash;MVID=$a.MainModule.Mvid.ToString();evidence=$rows;scope='Static Cecil only; no game method executed. Actual current UI model interval/deadline, no prefab guess. Strict > native boundary; UI recheck at >= is conservative.'}|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($false)))
    Write-Output 'Offer freshness native contracts passed: exact instance fields, lazy open, GameTicks/20, persisted deadline, no trade timer reset. No game execution.'
}finally{$a.Dispose()}

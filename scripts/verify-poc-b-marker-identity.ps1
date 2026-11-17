param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$contract=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
foreach($name in 'Assembly-CSharp.dll','NGUIAssembly.dll'){
    if((Get-FileHash -LiteralPath (Join-Path $managed $name)).Hash -ne $contract.hashes.$name){throw "Marker identity hash mismatch: $name"}
}
Add-Type -Path (Join-Path $managed 'Trivial.Mono.Cecil.dll')
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$game=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
$ngui=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'NGUIAssembly.dll'))
try{
    if($game.MainModule.Mvid.ToString() -ne $contract.moduleMvid){throw 'Marker identity game MVID mismatch'}
    $select=[LedgerCurrentEvidence]::Lookup($game.MainModule,0x06006004)
    if($select.FullName -ne 'System.Void SQLiteDBGlobalData::NavigatePlugin(Assembly-CSharp.DirectoryDictionary,SQLiteDBGlobalData/WindowType)'){throw 'Private bookmark SELECT signature changed'}
    $enum=$select.DeclaringType.NestedTypes|Where-Object Name -eq SQLCommands
    if(!@($enum.Fields|Where-Object {$_.Name -eq 'GetBookmarksForFaction' -and $_.Constant -eq 30}).Count){throw 'Bookmark read command identity changed'}
    if(!@($select.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 30}).Count){throw 'Native query command changed'}
    $calls=@($select.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    if($calls -contains 'ProcessAspect' -or $calls -contains 'DeployDrive' -or $calls -notcontains 'ExitStub'){throw 'Bookmark SELECT no longer uses read callback path'}
    $callback=[LedgerCurrentEvidence]::Lookup($game.MainModule,0x06006121)
    if($callback.FullName -ne 'System.Void SQLiteDBGlobalData/GroupSiteConnectionToken::OrderVector(System.Data.IDataReader)'){throw 'Bookmark read completion changed'}
    $read=$callback.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ClearEmulator'}|Select-Object -First 1
    if(!$read){throw 'Bookmark result decoder changed'}
    $decode=$read.Operand.Resolve()
    $ins=$decode.Body.Instructions;$direct=$false
    for($i=0;$i -lt $ins.Count-1;$i++){
        if($ins[$i].Operand -is [Trivial.Mono.Cecil.MethodReference] -and $ins[$i].Operand.Name -eq 'GetString' -and $ins[$i+1].OpCode.Name -eq 'stfld' -and [LedgerCurrentEvidence]::Canonical($ins[$i+1].Operand.MetadataToken.ToInt32()) -eq 0x04005a16){$direct=$true}
    }
    if(!$direct){throw 'Persisted bookmark label no longer reads directly from GetString'}
    $write=[LedgerCurrentEvidence]::Lookup($game.MainModule,0x06006008).Body.Instructions;$direct=$false
    for($i=0;$i -lt $write.Count-1;$i++){
        if($write[$i].Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($write[$i].Operand.MetadataToken.ToInt32()) -eq 0x04005a16 -and $write[$i+1].OpCode.Name -eq 'stelem.ref'){$direct=$true}
    }
    if(!$direct){throw 'Native bookmark update no longer submits raw label to persistence'}
    $parse=[LedgerCurrentEvidence]::Lookup($ngui.MainModule,0x06000349)
    if($parse.DeclaringType.FullName -ne 'NGUIText' -or $parse.Name -ne 'ParseSymbol'){throw 'NGUI parser identity changed'}
    $ops=@($parse.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    if($ops -notcontains 'ParseColor24' -or $ops -notcontains 'EncodeColor24' -or $ops -notcontains 'RemoveAt'){throw 'NGUI RGB push/pop consumption changed'}
    Write-Output ('PASS marker identity: native read-only GetBookmarksForFaction30/5FBC ->60D9 ->'+('{0:X8}' -f [LedgerCurrentEvidence]::Canonical($decode.MetadataToken.ToInt32()))+' raw GetString label;5FC0 raw SQL update; exact NGUI RGB push/pop parser. No game methods invoked.')
}finally{$game.Dispose();$ngui.Dispose()}

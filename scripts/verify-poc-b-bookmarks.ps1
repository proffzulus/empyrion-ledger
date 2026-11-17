param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Bookmark target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Bookmark MVID mismatch'}
    $expected=@{0x04005a16='System.String';0x04005a12='System.String';0x04005a18='UnityEngine.Vector3';0x04005a13='BookmarkType';0x04005a14='Assembly-CSharp.DirectoryDictionary';0x04005a15='System.Int32';0x04005a19='System.Int32';0x04005a1a='System.Boolean';0x04005a1b='System.Boolean';0x04005a1c='System.Boolean';0x04005a1d='System.Boolean';0x04005a1e='System.Boolean'}
    foreach($token in $expected.Keys){$f=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);if($f.DeclaringType.FullName -ne 'Assembly-CSharp.ActivatorManager' -or $f.FieldType.FullName -ne $expected[$token]){throw 'Bookmark field type mismatch'}}
    $add=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001b1b)
    if($add.FullName -ne 'System.Void Assembly-CSharp.FunctionEditor::ToggleDrive(Assembly-CSharp.ActivatorManager,SQLiteDBGlobalData/TreeNodeToken,System.Boolean)'){throw 'Native add signature mismatch'}
    # Actual editor writes each native option directly from the named UIToggle's value.
    $instructions=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600c531).Body.Instructions
    foreach($pair in @(@(0x0400b622,0x04005a1b),@(0x0400b623,0x04005a1c),@(0x0400b624,0x04005a1d))){
        $proved=$false
        for($i=0;$i -lt $instructions.Count-2;$i++){
            if($instructions[$i].Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($instructions[$i].Operand.MetadataToken.ToInt32()) -eq $pair[0] -and
               $instructions[$i+1].Operand.Name -eq 'get_value' -and $instructions[$i+2].OpCode.Name -eq 'stfld' -and [LedgerCurrentEvidence]::Canonical($instructions[$i+2].Operand.MetadataToken.ToInt32()) -eq $pair[1]){$proved=$true}
        }
        if(!$proved){throw 'Native option assignment evidence mismatch'}
    }
    # The selected playfield lookup is a cache-only TryGetValue path; no generation/action helper.
    $lookup=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005efe)
    $calls=@($lookup.Body.Instructions|Where-Object{$_.OpCode.Name -in @('call','callvirt')}|ForEach-Object{$_.Operand.Name})
    if(@($calls|Where-Object{$_ -notin @('SelectStream','TryGetValue')}).Count){throw 'Playfield lookup no longer cache-only'}
    $callback=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001b48)
    if(!@($callback.Body.Instructions|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x06001b23}).Count){throw 'Native callback no longer installs bookmark in local collection'}
    $remove=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001b22)
    if($remove.FullName -ne 'System.Void Assembly-CSharp.FunctionEditor::CopyComponent(System.Int32)'){throw 'Native removal signature mismatch'}
    $calls=@($remove.Body.Instructions|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference]}|ForEach-Object{[LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32())})
    if($calls -notcontains 0x0600600a -or $calls -notcontains 0x06001b24){throw 'Native removal no longer submits DB delete and local collection removal'}
    $delete=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600600a)
    if(!@($delete.Body.Instructions|Where-Object{$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 54}).Count){throw 'Native RemoveBookmark SQL command mismatch'}
    $enum=$delete.DeclaringType.NestedTypes|Where-Object Name -eq SQLCommands
    if(!@($enum.Fields|Where-Object{$_.Name -eq 'RemoveBookmark' -and $_.Constant -eq 54}).Count){throw 'Native delete command identity mismatch'}
    # The ID-only removal and collection helper have no playfield filter, unlike listing ProcessDisk(string).
    foreach($token in 0x06001b22,0x0600600a){
        if(@([LedgerCurrentEvidence]::Lookup($a.MainModule,$token).Body.Instructions|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x04005d71}).Count){throw 'Unexpected current-PF dependency in ID removal'}
    }
    $update=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001b20)
    if($update.FullName -ne 'System.Void Assembly-CSharp.FunctionEditor::DeletePage(Assembly-CSharp.ActivatorManager)'){throw 'Native bookmark update signature changed'}
    $updateCalls=@($update.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference]}|ForEach-Object {[LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32())})
    if($updateCalls -notcontains 0x06001b25 -or $updateCalls -notcontains 0x06006008){throw 'Native same-ID local/DB update path changed'}
    $updateSql=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06006008)
    if(!@($updateSql.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x04005a10}).Count){throw 'Native bookmark update no longer addresses original ID'}
    $load=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001b23)
    foreach($v in 2,3){if(!@($load.Body.Instructions|Where-Object {$_.OpCode.Name -eq ('ldc.i4.'+$v)}).Count){throw 'Ordinary map bookmark type retention changed'}}
    Write-Output 'PASS native same-ID local/DB update1B08->1B0D/5FC0; ordinary map types2/3 retained separately from navigation flags; static evidence only'
    $structure=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x04004ab1).FieldType.Resolve()
    $chain=@();while($structure){$chain+=$structure.FullName;if(!$structure.BaseType){break};$structure=$structure.BaseType.Resolve()}
    $pos=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x04001b41)
    if($chain -notcontains 'Assembly-CSharp.ReferenceHelper' -or $pos.FullName -ne 'UnityEngine.Vector3 Assembly-CSharp.ReferenceHelper::firstMessage'){throw 'Direct owning structure native position path mismatch'}
    Write-Output 'PASS native ID removal (DB RemoveBookmark54 plus local collection, no PF restriction) and direct owning POI position type chain'
    Write-Output 'PASS bookmark exact build/signatures, native toggle -> option fields, cache-only playfield lookup and native callback path (static IL only)'
} finally {$a.Dispose()}

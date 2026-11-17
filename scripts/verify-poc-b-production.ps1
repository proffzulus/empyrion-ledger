param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Production game hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Production MVID mismatch'}
    $spec=@{
        0x0600bbcc='System.Void Eleon.Ngui.ItemGridItemGeneratorOutput::Update()'
        0x06005849='Assembly-CSharp.PaneFactory[] Assembly-CSharp.DirectoryDesigner::SelectPane()'
        0x060033ed='Assembly-CSharp.PaneFactory[] Assembly-CSharp.PaneFactory::JoinDrive(Assembly-CSharp.PaneFactory[])'
        0x0600584b='System.Int32 Assembly-CSharp.DirectoryDesigner::TogglePage()'
        0x06005706='UnityEngine.Vector3 Assembly-CSharp.NodeHelper::DetachImage(World)'
        0x0600c483='System.String Assembly-CSharp.ResourceSetConnectionToken::UncheckMenuItem(System.Int32)'
        0x06005522='System.Single Assembly-CSharp.AddinResolver::FindDockingPane()'
        0x0600cde1='System.Void Assembly-CSharp.RegistryTreeConnectionToken::ToggleDrive(System.String,Eleon.Ngui.WindowStub)'
        0x06007f42='UnityEngine.Vector3 Vector3i::SortMemory()'
    }
    foreach($token in $spec.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token).FullName -ne $spec[$token]){throw ('Production method contract {0:X8}' -f $token)}}
    $fields=@{
        0x0400a9f6='Assembly-CSharp.DirectoryDesigner';0x0400a9f4='Assembly-CSharp.DirectoryDesigner';0x0400a9f7='System.Boolean';0x0400a9f8='System.Boolean';0x0400a9cd='Assembly-CSharp.PaneFactory[]';0x040052fe='Assembly-CSharp.PaneFactory[]'
        0x0400b4de='Assembly-CSharp.DirectoryDesigner';0x040052bb='Assembly-CSharp.PaneFactory[]';0x040052f6='Assembly-CSharp.DirectoryDictionary';0x04001305='FactionData'
        0x040052f9='System.UInt32';0x040052fd='System.Int32';0x04005300='System.UInt64';0x04005304='System.Int32';0x0400531c='Assembly-CSharp.AddinResolver'
        0x0400505f='System.Int32';0x04005060='Vector3i';0x04005065='System.Int32';0x04005066='System.Int32';0x0400506e='System.Boolean'
        0x0400b634='Assembly-CSharp.DirectorySet';0x04003368='Assembly-CSharp.AddinResolver';0x0400336a='System.String';0x0400b5bc='UILabel'
        0x0400b4dc='Assembly-CSharp.FileTreeConnectionToken';0x0400c186='Assembly-CSharp.FileTreeConnectionToken';0x0400c135='System.String';0x0400c136='System.String';0x0400c197='System.Boolean';0x0400c198='System.String';0x0400c199='System.String'
        0x0400b4ea='System.Single'
    }
    foreach($token in $fields.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token).FieldType.FullName -ne $fields[$token]){throw ('Production field contract {0:X8}' -f $token)}}
    if(![LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f7).IsStatic -or [LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f6).IsStatic -or [LedgerCurrentEvidence]::Lookup($a.MainModule,0x04001305).IsStatic){throw 'Production instance ownership/static trap contract changed'}
    function RequireOperand([int]$method,[int]$offset,[int]$operand){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $operand){throw ('Production IL contract {0:X8}+{1:X4}' -f $method,$offset)}}
    RequireOperand 0x0600bbcc 0x001f 0x06005849
    RequireOperand 0x06005849 0x0006 0x060033ed
    RequireOperand 0x06001076 0x0053 0x04001b31
    RequireOperand 0x06001076 0x0058 0x06000d56
    RequireOperand 0x06001076 0x005d 0x040052f6
    RequireOperand 0x06000d56 0x0001 0x04001305
    RequireOperand 0x06005522 0x0018 0x04005066
    RequireOperand 0x06005522 0x001e 0x04005065
    RequireOperand 0x0600c474 0x0024 0x0400b4dc
    RequireOperand 0x0600cde1 0x020b 0x0400c186
    RequireOperand 0x0600cde1 0x0289 0x0400c198
    RequireOperand 0x0600cde1 0x028e 0x0400c135
    RequireOperand 0x0600cde1 0x029b 0x0400c199
    RequireOperand 0x0600cde1 0x02a0 0x0400c136
    RequireOperand 0x0600cde1 0x0059 0x0400c197
    RequireOperand 0x060037b0 0x0003 0x04005060
    RequireOperand 0x060037b0 0x0008 0x06007f42
    RequireOperand 0x060037b0 0x0018 0x0600373c
    RequireOperand 0x0600c482 0x006f 0x0600584a
    RequireOperand 0x0600c482 0x0075 0x0600584b
    RequireOperand 0x0600c482 0x007c 0x0400b4ea
    RequireOperand 0x0600c482 0x00a4 0x0400b4ea
    # Shared output capacity and stopped/full guard used by service status; inspection only.
    RequireOperand 0x06005851 0x0016 0x040052bb
    RequireOperand 0x06005851 0x002c 0x040052bb
    RequireOperand 0x06005851 0x004e 0x040052fd
    RequireOperand 0x06005851 0x005d 0x040052fd
    RequireOperand 0x06005850 0x0004 0x0600585a
    RequireOperand 0x06005850 0x0058 0x0600585a
    $full=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005851).Body.Instructions|Where-Object Offset -eq 0x0066
    $fullRead=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600585a).Body.Instructions|Where-Object Offset -eq 0x0003
    if([LedgerCurrentEvidence]::Canonical($full.Operand.MetadataToken.ToInt32()) -ne [LedgerCurrentEvidence]::Canonical($fullRead.Operand.MetadataToken.ToInt32())){throw 'Service full-state guard changed'}
    $services=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\production-services';$null=New-Item -ItemType Directory -Path $services -Force
    $serviceProof=@(foreach($t in @(0x06005850,0x06005851,0x0600585a)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);[pscustomobject]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}})
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $services 'native-storage-proof.json')),($serviceProof|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
    $polish=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\production-polish';$null=New-Item -ItemType Directory -Path $polish -Force
    $proof=@(foreach($t in @(0x060037b0,0x06007f42,0x0600373c,0x0600584a,0x0600c482,0x0600c483,0x0600584f,0x06005850)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);[pscustomobject]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}})
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $polish 'native-proof.json')),($proof|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
    # Helpers called by the observer are read-only: reject direct stores and audited simulation calls.
    foreach($t in @(0x06005705,0x06005706,0x06004d54,0x060053d1,0x060011d5,0x0600584b,0x060037ba,0x0600c483)){
        $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t)
        foreach($i in $m.Body.Instructions){if($i.OpCode.Name -in @('stfld','stsfld','stelem.ref')){throw ('Unexpected write in read helper {0:X8}' -f $t)}}
    }
    $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\production-experiment\native-contract.json';New-Item -ItemType Directory -Path (Split-Path $out) -Force|Out-Null
    $rows=@(foreach($t in @($spec.Keys)+@(0x06005857,0x06005868,0x06005870,0x0600584f,0x06005850,0x06005851,0x0600c482,0x060037b1,0x060053fe)){
        $m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);[pscustomobject]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}
    })
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($out),($rows|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
    $repair=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\production-repair';$null=New-Item -ItemType Directory -Path $repair -Force
    $factoryRows=@(foreach($token in @(0x06000f79,0x060010cc,0x060010ce,0x060010d0,0x060011fc)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);[pscustomobject]@{token=('{0:X8}' -f $token);signature=$m.FullName;model=@($m.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'newobj' -and $_.Operand.DeclaringType.FullName -in @('Assembly-CSharp.DirectoryDesigner','Assembly-CSharp.ConditionTable','Assembly-CSharp.ConfigList')}|ForEach-Object {$_.Operand.DeclaringType.FullName});il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}})
    $shape=[ordered]@{assemblyHash=(Get-FileHash -LiteralPath $path).Hash;oldOwnerField=[ordered]@{token='040052F7';static=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f7).IsStatic;type=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f7).FieldType.FullName};owner=[ordered]@{token='040052F6';static=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f6).IsStatic;type=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x040052f6).FieldType.FullName;tuple='04001305';tupleType=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x04001305).FieldType.FullName;placement='06001076 IL0050=Player(1),0053=entity1B2A,0058=PathCollection.ctor,005D=instance52DD'};factories=$factoryRows}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $repair 'native-shape.json')),($shape|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($false)))
    $registration=[ordered]@{assemblyHash=(Get-FileHash -LiteralPath $path).Hash;method='0600CDE1';controllerBackedFlag='0400C197=false';controllerProps='0400B4DC';registeredProps='0400C186';overrides=@([ordered]@{target='0400C135';source='0400C198';loadOffset='0289';storeOffset='028E'},[ordered]@{target='0400C136';source='0400C199';loadOffset='029B';storeOffset='02A0'});unchangedFields=11;il=@([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600cde1).Body.Instructions|ForEach-Object {$_.ToString()})}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $repair 'native-registration.json')),($registration|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($false)))
    Write-Output 'Production native static contracts passed: guarded output postfix, published deep-copy provenance, same-device read helpers; no game execution.'
} finally {$a.Dispose()}

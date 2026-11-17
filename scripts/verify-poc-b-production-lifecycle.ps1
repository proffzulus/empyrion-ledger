param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Lifecycle target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Lifecycle MVID mismatch'}
    $spec=@{
        0x06006c72='System.Void Assembly-CSharp.MenuSiteConnectionToken::ToggleFile(World,System.Int32,Vector3i,Assembly-CSharp.ControlTable,System.Int32)'
        0x06004ef3='Assembly-CSharp.ControlTable Assembly-CSharp.ActionLayout::ReduceIcon(Vector3i,System.Boolean,Assembly-CSharp.ControlTable,System.Boolean,System.SByte,System.Boolean,System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Int32)'
        0x06004eec='Assembly-CSharp.ControlTable Assembly-CSharp.ActionLayout::CopyComponent(Vector3i)'
        0x06004ebf='Assembly-CSharp.PaneDictionary Assembly-CSharp.ActionLayout::BuildTemplate(Vector3i)'
        0x06004eb4='System.Int32 Assembly-CSharp.ActionLayout::get_ClusterIdx()'
        0x060053d1='Assembly-CSharp.ControlTable World::CopyComponent(System.Int32,Vector3i)'
    }
    foreach($token in $spec.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token).FullName -ne $spec[$token]){throw ('Lifecycle signature {0:X8}' -f $token)}}
    foreach($pair in @(@(0x04004ac0,'World'),@(0x04001480,'Assembly-CSharp.RegistryOptions[]'),@(0x040052c6,'System.Nullable`1<System.UInt64>'))){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$pair[0]).FieldType.FullName -ne $pair[1]){throw 'Lifecycle field type mismatch'}}
    function RequireCall([int]$method,[int]$offset,[string]$name){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or $i.Operand.Name -ne $name){throw ('Lifecycle IL {0:X8}+{1:X4}' -f $method,$offset)}}
    RequireCall 0x06006c72 0x0026 'CopyComponent'
    RequireCall 0x06006c72 0x0068 'PrintQueue'
    RequireCall 0x06006c72 0x0079 'AttachPage'
    RequireCall 0x06006c72 0x00e9 'ResolveEmulator'
    RequireCall 0x06004ef3 0x0319 'JoinDrive'
    RequireCall 0x06004ef3 0x038b 'AttachConfig'
    RequireCall 0x06004dd1 0x0068 'CleanOptions'
    RequireCall 0x06004dd1 0x00b4 'RebuildClient'
    RequireCall 0x06000ece 0x0028 'createTileEntity'
    RequireCall 0x06000ecf 0x002a 'removeTileEntity'
    RequireCall 0x06000f79 0x002a '.ctor'
    $actor=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06004ef3).Body.Instructions|Where-Object Offset -eq 0x0219
    if($actor.Operand.Index -ne 10){throw 'Lifecycle actor argument changed'}
    foreach($t in @(0x06004eec,0x06004eed,0x06004ebf,0x06004eb4,0x060053d1)){
        foreach($i in [LedgerCurrentEvidence]::Lookup($a.MainModule,$t).Body.Instructions){if($i.OpCode.Name -in @('stfld','stsfld','stelem.ref')){throw 'Lifecycle action-target helper unexpectedly writes'}}
    }
    $rows=@(foreach($token in @($spec.Keys)+@(0x06004dd1,0x06000ece,0x06000ecf,0x06000f79,0x060010cc,0x060010ce,0x060010d0,0x060011fc,0x06005419,0x060057a3,0x060057aa)){
        $method=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token)
        [ordered]@{token=('{0:X8}' -f $token);signature=$method.FullName;il=@($method.Body.Instructions|ForEach-Object {$text=$_.ToString();if($_.Operand -is [Trivial.Mono.Cecil.ParameterDefinition]){$text+=' [argumentIndex='+$_.Operand.Index+']'};$text})}
    })
    $folder=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\production-lifecycle';$null=New-Item -ItemType Directory -Force $folder
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $folder 'native-contract.json')),([ordered]@{hash=(Get-FileHash $path).Hash;MVID=$a.MainModule.Mvid.ToString();evidence=$rows;limitations='metadata/IL only; no game invocation.52AD is a changing production clock, not identity. No destruction/unload callbacks subscribed.'}|ConvertTo-Json -Depth 7),(New-Object Text.UTF8Encoding($false)))
    Write-Output 'Production lifecycle native contracts passed: local committed pickup, actor/target/definition change, pure exact-target reads, create/remove model boundary. No game execution.'
}finally{$a.Dispose()}

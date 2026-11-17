param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Scanner HUD assembly hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
$ngui=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'NGUIAssembly.dll'))
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Scanner HUD MVID mismatch'}
    $fields=@{0x0400ab5b='Assembly-CSharp.FileTreeConnectionToken';0x0400ab6a='World';0x0400ab6e='System.Collections.Generic.Dictionary`2<Assembly-CSharp.DirectorySet,UnityEngine.GameObject>';0x0400ab72='System.Collections.Generic.List`1<Assembly-CSharp.DirectorySet>';0x0400ab7b='UnityEngine.Transform';0x04003368='Assembly-CSharp.AddinResolver';0x0400336a='System.String';0x0400332c='System.Int32';0x0400505f='System.Int32';0x04005060='Vector3i';0x04005062='Assembly-CSharp.ControlTable';0x04005068='System.Int32'}
    foreach($t in $fields.Keys){$f=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);if($f.FieldType.FullName -ne $fields[$t] -or $f.IsStatic){throw ('Scanner field mismatch {0:X8}' -f $t)}}
    function Operand([int]$method,[int]$offset,[int]$expected){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $expected){throw ('Scanner binding mismatch {0:X8}+{1:X4}' -f $method,$offset)}}
    Operand 0x0600bcea 0x0001 0x0400ab5b
    Operand 0x0600bcec 0x0007 0x0400ab7b
    Operand 0x0600bcf2 0x001c 0x0400ab6a
    # BB42 publishes an indexed copy of exactly its existing renderer dictionary keys.
    Operand 0x0600bcf4 0x020b 0x0400ab6e
    Operand 0x0600bcf4 0x021a 0x0400ab72
    function Signature([int]$method,[int]$offset,[string]$expected){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or $i.Operand.FullName -cne $expected){throw ('Scanner indexed-list relationship mismatch {0:X8}+{1:X4}' -f $method,$offset)}}
    Signature 0x0600bcf4 0x0210 'System.Collections.Generic.Dictionary`2/KeyCollection<!0,!1> System.Collections.Generic.Dictionary`2<Assembly-CSharp.DirectorySet,UnityEngine.GameObject>::get_Keys()'
    Signature 0x0600bcf4 0x0215 'System.Collections.Generic.List`1<!!0> System.Linq.Enumerable::ToList<Assembly-CSharp.DirectorySet>(System.Collections.Generic.IEnumerable`1<!!0>)'
    Operand 0x0600bcf5 0x000a 0x060053af
    # BB43 indexes that list, stores the marker in V6, then uses V6 for A971 lookup.
    Operand 0x0600bcf5 0x0096 0x0400ab72
    Signature 0x0600bcf5 0x009d '!0 System.Collections.Generic.List`1<Assembly-CSharp.DirectorySet>::get_Item(System.Int32)'
    Operand 0x0600bcf5 0x00a5 0x0400ab6e
    Signature 0x0600bcf5 0x00ac '!1 System.Collections.Generic.Dictionary`2<Assembly-CSharp.DirectorySet,UnityEngine.GameObject>::get_Item(!0)'
    foreach($spec in @(@(0x009b,'ldloc.s',5),@(0x00a2,'stloc.s',6),@(0x00aa,'ldloc.s',6))){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600bcf5).Body.Instructions|Where-Object Offset -eq $spec[0];if($i.OpCode.Name -ne $spec[1] -or $i.Operand.Index -ne $spec[2]){throw 'HUD indexed marker and dictionary key no longer the same local'}}
    Operand 0x0600bcf5 0x0662 0x06003754
    Operand 0x060037ba 0x0001 0x04003368
    Operand 0x060037ba 0x0006 0x04005068
    foreach($offset in @(0x0021,0x003c,0x0058)){Operand 0x060037ba $offset 0x04005068}
    # Raw position/definition value equality is used only as an identity fingerprint.
    foreach($t in @(0x04005060,0x04005062)){if(![LedgerCurrentEvidence]::Lookup($a.MainModule,$t).FieldType.IsValueType){throw 'HUD identity fingerprint is no longer value typed'}}
    Operand 0x060037b0 0x000e 0x0400505f
    Operand 0x060037b0 0x0018 0x0600373c
    $ctor=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600373c).Body.Instructions
    if(!($ctor|Where-Object {$_.OpCode.Name -eq 'stfld' -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x0400332c})){throw 'Marker/resource numeric identity no longer established by constructor'}
    $render=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600bcf5).Body.Instructions
    foreach($offset in @(0x03eb,0x0986,0x09b4)){$i=$render|Where-Object Offset -eq $offset;if(!$i -or $i.Operand.FullName -notmatch 'UILabel'){throw 'HUD final UILabel binding changed'}}
    $visible=($ngui.MainModule.Types|Where-Object Name -eq 'UIWidget').Methods|Where-Object Name -eq 'get_isVisible'
    foreach($i in $visible.Body.Instructions){if($i.OpCode.Name -in @('stfld','stsfld')){throw 'UI visibility getter writes state'}}
    $size=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060037ba)
    foreach($i in $size.Body.Instructions){if($i.OpCode.Name -in @('stfld','stsfld')){throw 'Localized size getter writes state'}}
    # Do NOT call FindCommand/LoadMemory: native rendering can mutate resource/marker state.
    $nativeSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\LedgerDepositHudNative.cs'))
    if($nativeSource -match 'Invoke\(0x(377e|3785|3721|54ed)'){throw 'Scanner reader calls a native rendering/simulation operation'}
    if($nativeSource -match 'foreach\s*\(DictionaryEntry\s+\w+\s+in\s+views\)|new List<DictionaryEntry>|hudOffset'){throw 'Unbounded/full copied binding traversal returned'}
    $proof=@(foreach($t in @(0x0600bcea,0x0600bcec,0x0600bcef,0x0600bcf2,0x0600bcf4,0x0600bcf5,0x060037b0,0x060037b1,0x060037b8,0x060037ba,0x0600373c)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);[ordered]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object ToString)}})
    $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\scanner-deposits';$null=New-Item -ItemType Directory -Path $out -Force
    $record=[ordered]@{assemblyHash=(Get-FileHash -LiteralPath $path).Hash;mvid=$a.MainModule.Mvid.ToString();nguiHash=(Get-FileHash -LiteralPath (Join-Path $managed 'NGUIAssembly.dll')).Hash;fields=@(foreach($t in $fields.Keys){[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t).FullName});visibilityGetter=@($visible.Body.Instructions|ForEach-Object ToString);evidence='Static Cecil inspection only; no game methods executed. BB42 A971.Keys.ToList -> A975; BB43 A975[index] -> same marker A971 lookup. 504F is the integer band input read by localized size getter3787. Published HUD view bound by object identity; precise resource counters excluded.';methods=$proof}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath((Join-Path $out 'native-contract.json')),($record|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
    'Scanner HUD native contracts passed (static only).'
} finally {$ngui.Dispose();$a.Dispose()}

param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
if((Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Browser game hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Browser MVID mismatch'}
    $fields=@{0x0400196a='System.Collections.Generic.List`1<Assembly-CSharp.NodeProvider>';0x04001995='System.Collections.Generic.HashSet`1<System.String>';0x04003253='System.Collections.Generic.Dictionary`2<System.String,Assembly-CSharp.OptionsSerializer>'}
    foreach($key in $fields.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$key).FieldType.FullName -ne $fields[$key]){throw 'Browser metadata container signature mismatch'}}
    $list=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001a03).Body.Instructions
    if($list.Count -ne 3 -or [LedgerCurrentEvidence]::Canonical($list[0].Operand.MetadataToken.ToInt32()) -ne 0x0400196a){throw 'Full template list accessor changed'}
    $register=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060019f2).Body.Instructions
    if([LedgerCurrentEvidence]::Canonical($register[0].Operand.MetadataToken.ToInt32()) -ne 0x0400196a -or [LedgerCurrentEvidence]::Canonical($register[3].Operand.MetadataToken.ToInt32()) -ne 0x0400196b){throw 'Alternative list vs first-output dictionary evidence changed'}
    $targets=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001a3d).Body.Instructions
    if([LedgerCurrentEvidence]::Canonical($targets[1].Operand.MetadataToken.ToInt32()) -ne 0x04001995 -or !($targets|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Split'})){throw 'Template target parsing changed'}
    $filter=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06001a3f).Body.Instructions
    if(!($filter|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x04001995}) -or !($filter|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Contains'})){throw 'Native template target membership changed'}
    $parse=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06005a3e).Body.Instructions
    if(!($parse|Where-Object{$_.Offset -eq 0x003f -and $_.OpCode.Name -eq 'ldc.i4.1'}) -or !($parse|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x06001a33})){throw 'Native template parser/default batch changed'}
    $extract=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06003639).Body.Instructions
    if(!($extract|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x04003251}) -or !($extract|Where-Object{$_.OpCode.Name -eq 'ldelem.ref'})){throw 'Runtime definition indexed identity evidence changed'}
    $evidence=New-Object 'System.Collections.Generic.List[string]'
    $evidence.Add('Build 5188 / Cecil static evidence only; no game method execution. See guarded source adapter.')
    foreach($token in 0x060019f2,0x06001a03,0x06001a33,0x06001a3d,0x06001a3f,0x06005a3e,0x06003639){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token);$evidence.Add(('0x{0:X8} {1}' -f $token,$m.FullName));foreach($i in $m.Body.Instructions){$evidence.Add($i.ToString())}}
    $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\item-browser-static-il.txt'
    [IO.File]::WriteAllLines($out,$evidence,(New-Object Text.UTF8Encoding($false)))
    $gui=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.IMGUIModule.dll'))
    try {
        $rotate=($gui.MainModule.Types|Where-Object {$_.FullName -eq 'UnityEngine.GUIUtility'}).Methods|Where-Object {$_.Name -eq 'RotateAroundPivot'}
        if(!($rotate.Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.GUIClip' -and $_.Operand.Name -eq 'Unclip'})){throw 'Rotation helper evidence changed'}
        $rotation=@('Shipped GUIUtility helper used by old graph; Cecil only, no Unity/game execution.');$rotation+=('0x{0:X8} {1}' -f [LedgerCurrentEvidence]::Canonical($rotate.MetadataToken.ToInt32()),$rotate.FullName);$rotation+=@($rotate.Body.Instructions|ForEach-Object {$_.ToString()})
        [IO.File]::WriteAllLines((Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\graph-rotation-static-il.txt'),$rotation,(New-Object Text.UTF8Encoding($false)))
    }finally{$gui.Dispose()}
    Write-Output 'PASS browser full recipe alternatives, exact batch default, native target membership and indexed effective definition metadata (Cecil only)'
}finally{$a.Dispose()}

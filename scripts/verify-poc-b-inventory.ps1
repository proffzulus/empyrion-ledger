[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Inventory native hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$native=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
$public=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'ModApi.dll'))
try {
    if($native.MainModule.Mvid.ToString() -ne $contract.moduleMvid){throw 'Inventory MVID mismatch'}
    $source=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerInventory.cs'))
    $fields=@(foreach($m in [regex]::Matches($source,'Bind\(0x([0-9a-f]+),"([^"]+)","([^"]+)"\)')){
        $token=0x04000000+[Convert]::ToInt32($m.Groups[1].Value,16)
        $f=[LedgerCurrentEvidence]::Lookup($native.MainModule,$token)
        if($f.IsStatic -or $f.DeclaringType.FullName -ne $m.Groups[2].Value -or $f.FieldType.FullName -ne $m.Groups[3].Value){throw 'Inventory field shape mismatch'}
        [ordered]@{token=('{0:X8}' -f $token);signature=$f.FullName}
    })
    if($fields.Count -ne 14){throw 'Expected fourteen bounded personal inventory fields'}
    $access=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerInventoryAccess.cs'))
    $accessFields=@(foreach($m in [regex]::Matches($access,'Bind(Static|List)?\(0x([0-9a-f]+),"([^"]+)","([^"]+)"\)')){
        $token=0x04000000+[Convert]::ToInt32($m.Groups[2].Value,16);$f=[LedgerCurrentEvidence]::Lookup($native.MainModule,$token)
        $expected=$m.Groups[4].Value;$kind=$m.Groups[1].Value
        $actual=$f.FieldType.FullName.Replace('/','+')
        if($kind -eq 'List'){$actual=$f.FieldType.GenericArguments[0].FullName;if($f.FieldType.ElementType.FullName -ne 'System.Collections.Generic.List`1'){throw 'Inventory access list type mismatch'}}
        if($f.IsStatic -ne ($kind -eq 'Static') -or $f.DeclaringType.FullName -ne $m.Groups[3].Value -or $actual -ne $expected){throw ('Inventory access field mismatch '+('{0:X8}' -f $token)+' '+$f.FullName)}
        [ordered]@{token=('{0:X8}' -f $token);signature=$f.FullName}
    })
    if($accessFields.Count -ne 24){throw 'Expected 24 bounded active/link/cursor fields'}
    if($access -match '\.SetValue\(|\.Invoke\(|\.GetContent\(|get_FormatMenu|CloneLine\(|TestDevice\('){throw 'Forbidden inventory invocation'}
    foreach($guard in @('ReferenceEquals(current,buffer)','F(0x1a5a,link)==null','F(0x1a5b,link)==null','(float)F(0x1a65,link)<0','allowed.Count<=512','label.text','ReferenceEquals(F(0xa9cd,grid),bagSlots)','30,9,true')){if(!$access.Contains($guard)){throw ('Missing inventory fence '+$guard)}}
    $editor=$native.MainModule.Types|Where-Object FullName -eq 'Assembly-CSharp.SolutionLayout'
    $rebuild=[LedgerCurrentEvidence]::Lookup($native.MainModule,0x060021cf)
    if(@($rebuild.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'stfld' -and $_.Operand -is [Trivial.Mono.Cecil.FieldReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x04002045}).Count -ne 1){throw 'Personal model ownership assignment changed'}
    $methodTokens=@([LedgerCurrentEvidence]::Canonical($rebuild.MetadataToken.ToInt32()))+@(0x0600B825,0x0600B826,0x060033a7,0x060033F5,0x060033D8,0x0600AF0B,0x0600BA97,0x0600BA8E,0x0600BA87,0x0600BA88,0x060022E6,0x060058d0,0x0600573a,0x06005769,0x0600BA89,0x0600BA8F,0x0600BB5C,0x0600BBA1,0x06005743,0x06005744,0x06005745,0x06005746,0x0600576b,0x06007060,0x0600C4E1,0x0600C4EA,0x0600C921,0x0600335F,0x06001DF4,0x06001B49,0x06001B4A,0x06001B4E,0x06001B4F,0x06001B52,0x06001B55,0x06001B5F,0x06001B60,0x0600BB26,0x0600BB29,0x0600BB2F,0x0600BB33,0x0600BBD5,0x0600AEFC,0x0600AF01)
    $methods=@(foreach($token in $methodTokens){
        $m=[LedgerCurrentEvidence]::Lookup($native.MainModule,$token)
        [ordered]@{token=('{0:X8}' -f $token);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object { $line=$_.ToString(); if($_.Operand -is [Trivial.Mono.Cecil.ParameterDefinition]){$line+=' paramIndex='+$_.Operand.Index};if($_.Operand -is [Trivial.Mono.Cecil.FieldReference]){$line+=' fieldToken='+('{0:X8}' -f [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()))};$line })}
    })
    $toolbar=$methods|Where-Object token -eq '0600B825'
    if(($toolbar.il -join ' ') -notmatch 'MemoryHelper::colorInitialized' -or ($toolbar.il -join ' ') -notmatch 'MethodList::RebuildPartition'){throw 'Public toolbar chain changed'}
    $copy=$methods|Where-Object token -eq '060033A7'
    if(($copy.il -join ' ') -notmatch 'ldc.i4.s 9' -or ($copy.il -join ' ') -notmatch 'PaneFactory::RebuildClient'){throw 'Native toolbar bound changed'}
    $repair=$methods|Where-Object token -eq '0600AF0B'
    if(($repair.il -join ' ') -notmatch 'stind.i4' -or ($repair.il -join ' ') -notmatch 'DirectoryStream::UpdateTextFile'){throw 'Public count-repair evidence changed'}
    $select=$methods|Where-Object token -eq '0600BA8E'
    if(($select.il -join ' ') -notmatch 'ToolbarOptionsConnectionToken::TestOptions' -or ($select.il -join ' ') -notmatch 'ItemGrid::'){throw 'Bag selector evidence changed'}
    $create=$methods|Where-Object token -eq '060022E6'
    if(($create.il -join ' ') -notmatch 'ldc.i4.5' -or ($create.il -join ' ') -notmatch 'ldc.i4.8' -or ($create.il -join ' ') -notmatch 'OptionsInvoker::.ctor'){throw 'Personal bag creation evidence changed'}
    $owned=$methods|Where-Object token -eq '0600573A'
    if(($owned.il -join ' ') -notmatch 'paramIndex=5' -or ($owned.il -join ' ') -notmatch 'fieldToken=04005288'){throw 'Personal owned input Domain evidence changed'}
    $allocation=$methods|Where-Object token -eq '0600335F'
    if(($allocation.il -join ' ') -notmatch 'ldc.i4.s 30'){throw 'Native toolbar backing allocation changed'}
    $drag=$methods|Where-Object token -eq '0600BB33'
    if(($drag.il -join ' ') -notmatch 'ItemGrid::InsertEditor' -or ($drag.il -join ' ') -notmatch 'ItemGrid::AllocateAssembly'){throw 'Native drag source settlement changed'}
    $label=$methods|Where-Object token -eq '0600BB29'
    if(($label.il -join ' ') -notmatch 'fieldToken=0400A97F' -or ($label.il -join ' ') -notmatch 'UILabel::set_text'){throw 'Cursor published count changed'}
    $companion=$methods|Where-Object token -eq '0600AEFC'
    if(($companion.il -join ' ') -notmatch 'fieldToken=0400A197' -or ($companion.il -join ' ') -notmatch 'fieldToken=04002FE0'){throw 'Normal native count companion ownership changed'}
    $shown=$methods|Where-Object token -eq '0600BB26'
    if(($shown.il -join ' ') -notmatch 'ComponentContextConnectionToken::.ctor'){throw 'Normal shown item companion construction changed'}
    $pure=[LedgerCurrentEvidence]::Lookup($native.MainModule,0x0600AF01)
    $shape=($pure.Body.Instructions|ForEach-Object {$_.OpCode.Name}) -join ','
    if($shape -ne 'ldarg.0,ldfld,ldc.i4,xor,ldc.i4.s,div,ldc.i4,sub,ret' -or [LedgerCurrentEvidence]::Canonical($pure.Body.Instructions[1].Operand.MetadataToken.ToInt32()) -ne 0x0400A19B -or $pure.Body.Instructions[2].Operand -ne 1322077852 -or $pure.Body.Instructions[4].Operand -ne 43 -or $pure.Body.Instructions[6].Operand -ne 2357){throw 'Passive count agreement expression changed'}
    $candidateFields=@(foreach($token in @(0x0400A908,0x0400A909,0x0400A90B,0x0400AD21,0x0400AD25,0x0400A9CD,0x0400682B,0x0400A9E9,0x0400A9EA,0x0400528d,0x0400528e,0x0400B56D,0x0400B56E,0x0400B57A,0x0400B57F,0x0400B586,0x0400B593)){
        $f=[LedgerCurrentEvidence]::Lookup($native.MainModule,$token);[ordered]@{token=('{0:X8}' -f $token);signature=$f.FullName;status='inspected candidate only, not used by reader'}
    })
    $interfaces=@(foreach($name in @('IPlayer','IApplication','IGui','IContainer')){
        $t=$public.MainModule.Types|Where-Object Name -eq $name
        [ordered]@{type=$t.FullName;members=@($t.Methods|ForEach-Object FullName)}
    })
    $out=Join-Path $root 'artifacts\poc-b\verification\inventory-feedback'
    $null=New-Item -ItemType Directory -Force $out
    [ordered]@{source=$path;sha256=(Get-FileHash $path).Hash;mvid=$native.MainModule.Mvid.ToString();fields=$fields;accessFields=$accessFields;methods=$methods;candidateFields=$candidateFields;publicApi=$interfaces;scope='Static metadata/IL only, not Unity acceptance. Personal backpack, active selector and settled linked toolbar; visible cursor quantity/origin. Never full linked contents or generic open-container contents. No native methods invoked.'}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $out 'native-proof.json') -Encoding UTF8
    Write-Output 'Inventory native contract passed: fourteen base +24 active/link/cursor fields; first9 of30, source/origin fences and published quantity; no native runtime invocation.'
}finally{$native.Dispose();$public.Dispose()}

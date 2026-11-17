param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed';$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Item-info game hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    $fields=@{0x04003256='DynamicProperties';0x0400325c='Assembly-CSharp.EmulatorEventArgsConnectionToken';0x04007121='DynamicProperties';0x0400196b='System.Collections.Generic.Dictionary`2<System.Int32,Assembly-CSharp.NodeProvider>';0x0400198d='System.Int32';0x0400198e='System.Int32';0x04001991='System.Collections.Generic.List`1<Assembly-CSharp.PaneFactory>';0x04002fdf='Assembly-CSharp.OptionsSerializer';0x04002fe0='Assembly-CSharp.ComponentContextConnectionToken';0x04002fe1='System.Int32';0x04002fe9='System.Int32';0x04002fec='System.Int32'}
    foreach($key in $fields.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$key).FieldType.FullName -ne $fields[$key]){throw ('Item/recipe field mismatch {0:X8}' -f $key)}}
    $format=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06006ded)
    if(!$format.IsStatic -or $format.FullName -ne 'System.String Assembly-CSharp.EmulatorTokenConnectionToken::CloneActivity(DynamicProperties,DynamicProperties,DynamicProperties,DynamicProperties/ValueCallback)'){throw 'Native properties formatter signature changed'}
    foreach($pair in @(@(0x06001a34,0x0400198d),@(0x06001a36,0x0400198e),@(0x06001a3b,0x04001991))){$ins=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$pair[0]).Body.Instructions;if($ins.Count -ne 3 -or [LedgerCurrentEvidence]::Canonical($ins[1].Operand.MetadataToken.ToInt32()) -ne $pair[1]){throw 'Recipe direct getter evidence changed'}}
    $string=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060077f3).Body.Instructions
    if($string[3].Operand.FullName -ne 'System.Object DynamicProperties/ValueWrapperObject::GetObject(System.String)' -or $string[4].Operand.FullName -ne 'System.String System.Object::ToString()'){throw 'Native formatted property getter changed'}
    $data=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060077ce).Body.Instructions|Where-Object{$_.Offset -eq 0x19}
    if($data.Operand.FullName -ne 'System.String Assembly-CSharp.QueueInvokerConnectionToken::ToString(System.Object)'){throw 'DataItem formatter/units path changed'}
    # These routines format supplied definition metadata/localized text; no gameplay action calls.
    foreach($token in 0x06006deb,0x06006dec,0x06006ded){foreach($i in [LedgerCurrentEvidence]::Lookup($a.MainModule,$token).Body.Instructions){if($i.OpCode.Name -eq 'stsfld' -or $i.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $i.Operand.DeclaringType.FullName -match 'ResourceSerializer|EditorContext|MemoryList|World|Sqlite'){throw 'Item formatter gained a world/inventory action dependency'}}}
    # Native tooltip dispatch uses the canonical descriptor ID to index block
    # definition metadata, not the generic item-wrapper DynamicProperties.
    foreach($pair in @(@(0x0600368c,0x0008,0x04001480),@(0x0600368c,0x000e,0x04002fe9),@(0x0600368c,0x001a,0x06000f1a),@(0x06000f1a,0x0044,0x04001488),@(0x06000f1a,0x004a,0x040014e7),@(0x06000f1a,0x004f,0x04007121),@(0x06000e9f,0x0001,0x0400149d))){
      $i=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$pair[0]).Body.Instructions|Where-Object Offset -eq $pair[1]
      if(!$i -or !$i.Operand -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $pair[2]){throw 'Native block tooltip property-root relationship changed'}
    }
    if(([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600368c).Body.Instructions|Where-Object Offset -eq 0x0013).OpCode.Name -ne 'ldelem.ref'){throw 'Native item/block metadata index changed'}
    $api=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'ModApi.dll'));try{if(($api.MainModule.GetType('FactionGroup').Fields|Where-Object Name -eq 'NoFaction').Constant -ne 255){throw 'Native NoFaction identity changed'}}finally{$api.Dispose()}
    Write-Output 'PASS block metadata: native selected-ID -> RegistryOptions table, own/shared property roots, canonical name field; NoFaction255 from checked ModApi. No game methods invoked.'
    Write-Output 'PASS active item/property formatter with native units, direct recipe output/count/inputs and bounded metadata routes (static only)'
}finally{$a.Dispose()}

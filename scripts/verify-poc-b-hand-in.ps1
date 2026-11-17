param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Hand-in game hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Hand-in MVID mismatch'}
    $spec=@{
        0x06006aaa='System.Void DialogueSystem::API_RemoveItemFromLocalPlayer(System.String,System.Int32,System.Int32)'
        0x0600ba92='System.Int32 Assembly-CSharp.ToolbarOptionsConnectionToken::CleanActivator(System.Int32,System.Int32,System.Int32,System.Boolean,System.Boolean)'
        0x06003380='System.Int32 Assembly-CSharp.MethodList::LoadContext(System.Int32,System.Int32,System.Int32)'
    }
    foreach($t in $spec.Keys){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);if($m.IsStatic -or $m.FullName -ne $spec[$t]){throw 'Hand-in signature changed'}}
    $calls=@{0x0001=0x06003647;0x0025=0x06006a9d;0x003f=0x0600ba92;0x0059=0x06003380}
    foreach($offset in $calls.Keys){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06006aaa).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or !($i.Operand -is [Trivial.Mono.Cecil.MethodReference]) -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $calls[$offset]){throw 'Dialogue bag/toolbar removal path changed'}}
    foreach($pair in @(@(0x0600ba92,0x00c8),@(0x06003380,0x0109))){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$pair[0]).Body.Instructions|Where-Object Offset -eq $pair[1];if($i.OpCode.Name -ne 'ldloc.0' -or $i.Next.OpCode.Name -ne 'ldarg.2' -or $i.Next.Next.OpCode.Name -ne 'sub' -or $i.Next.Next.Next.OpCode.Name -ne 'ret'){throw 'Actual removed-count return formula changed'}}
    foreach($token in @(0x0600ba92,0x06003380)){
        $method=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token)
        if(!($method.Body.Instructions|Where-Object {$_.OpCode.Name -like 'starg*' -and $_.Operand.Index -eq 1})){throw 'Native remaining-count argument mutation changed; recheck prefix capture'}
    }
    Write-Output 'Hand-in original count arguments mutate via starg; prefix capture plus return-only removal accounting required (Cecil only).'
    Write-Output 'Hand-in static IL verified: exact3 signatures; wrapper bag+toolbar fallback; actual requested-minus-remaining return counts. Game methods not invoked.'
} finally {$a.Dispose()}

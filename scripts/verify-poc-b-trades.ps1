param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Trade target hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Trade MVID mismatch'}
    $spec=@{
        0x0600cd22='System.Void Assembly-CSharp.PcitureEditorConnectionToken::ExitDeployment()'
        0x0600cd23='System.Void Assembly-CSharp.PcitureEditorConnectionToken::SelectBitmap()'
        0x060058c6='System.Single Assembly-CSharp.ActionEditor/DriveResolver::SortMemory(System.Int32,System.Boolean)'
        0x0600cd24='System.Boolean Assembly-CSharp.PcitureEditorConnectionToken::SplitBuilder(Assembly-CSharp.SolutionLayout,Assembly-CSharp.PaneFactory)'
        0x06006067='System.Void SQLiteDBGlobalData::FreeForm(System.Int32,System.String,System.Int32,System.String,Vector3i,System.Int32,System.Int32,System.Int32,System.Single)'
        0x060022b7='System.Boolean Assembly-CSharp.SolutionLayout::ChangeMenu(System.Int32)'
        0x060022c1='System.Boolean Assembly-CSharp.SolutionLayout::BatchBuildTemplate(Assembly-CSharp.PaneFactory,System.Boolean,System.Boolean,System.Boolean,System.Boolean)'
    }
    foreach($t in $spec.Keys){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t);if($m.IsStatic -or $m.FullName -ne $spec[$t]){throw 'Trade signature mismatch'}}
    # Exact offsets are certified against this hash. These are inspected, never invoked.
    $calls=@{
        0x0600cd22=@{0x008d=0x060058c6;0x00b9=0x060022b7;0x00d0=0x06001d24;0x00dd=0x0600cd24;0x01b2=0x06006067}
        0x0600cd23=@{0x0133=0x060058c6;0x015e=0x060058c6;0x01ce=0x060033d5;0x021b=0x06001c8a;0x0238=0x06001d24;0x0306=0x06006067}
    }
    foreach($t in $calls.Keys){$instructions=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t).Body.Instructions;foreach($offset in $calls[$t].Keys){$i=$instructions|Where-Object{$_.Offset -eq $offset};if(!$i -or !($i.Operand -is [Trivial.Mono.Cecil.MethodReference]) -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $calls[$t][$offset]){throw ('Trade call evidence mismatch {0:X8} IL_{1:X4}' -f $t,$offset)}}}
    foreach($t in 0x0600cd22,0x0600cd23){$audit=[LedgerCurrentEvidence]::Lookup($a.MainModule,$t).Body.Instructions|Where-Object{$_.Operand -is [Trivial.Mono.Cecil.MethodReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq 0x06006067};if($audit.Previous.OpCode.Name -ne $(if($t -eq 0x0600cd22){'ldloc.2'}else{'ldloc.s'}) -or $t -eq 0x0600cd23 -and $audit.Previous.Operand.Index -ne 6){throw 'Native audit quote local changed'}}
    $credits=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600b821).Body.Instructions|Where-Object{$_.Offset -eq 6}
    if([LedgerCurrentEvidence]::Canonical($credits.Operand.MetadataToken.ToInt32()) -ne 0x06001d23){throw 'IPlayer.Credits -> account getter evidence changed'}
    $worldName=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600b87c).Body.Instructions|Where-Object{$_.Offset -eq 0x1a}
    $auditName=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600cd22).Body.Instructions|Where-Object{$_.Offset -eq 0x163}
    if($worldName.Operand.FullName -ne $auditName.Operand.FullName){throw 'Audit world name != API playfield name source'}
    $fields=@{0x0400c024='UIToggle';0x0400c032='Assembly-CSharp.ActionEditor/DriveResolver';0x0400c034='System.Int32';0x04002fdf='Assembly-CSharp.OptionsSerializer';0x04002fe0='Assembly-CSharp.ComponentContextConnectionToken';0x04002fe1='System.Int32';0x04002fe9='System.Int32'}
    foreach($t in $fields.Keys){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t).FieldType.FullName -ne $fields[$t]){throw 'Trade selected/currency field changed'}}
    $sell=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600cd23).Body.Instructions
    if(($sell|Where-Object{$_.Offset -eq 0x01cd}).OpCode.Name -ne 'conv.i4'){throw 'Cash payout integer truncation changed'}
    foreach($offset in 0x217,0x218,0x219){if(($sell|Where-Object{$_.Offset -eq $offset}).OpCode.Name -ne 'ldc.i4.1'){throw 'Cash delivery flags changed'}}
    if(($sell|Where-Object{$_.Offset -eq 0x21a}).OpCode.Name -ne 'ldc.i4.0'){throw 'Cash final delivery flag changed'}
    $debit=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060022b7).Body.Instructions
    if(($debit|Where-Object{$_.Offset -eq 0x111}).Operand.FieldType.FullName -ne 'System.Int32' -or ($debit|Where-Object{$_.Offset -eq 0x117}).OpCode.Name -ne 'cgt' -or ($debit|Where-Object{$_.Offset -eq 0x11a}).OpCode.Name -ne 'ceq'){throw 'Cash debit return != remaining <= 0'}
    $getter=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060033d8).Body.Instructions
    $setter=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x060033d9).Body.Instructions
    if(($getter|Where-Object{$_.Offset -eq 0x1a}).Operand.MetadataToken.ToInt32() -ne 0x04002fe1 -or ($setter|Where-Object{$_.Offset -eq 0x1b}).Operand.MetadataToken.ToInt32() -ne 0x04002fe1){throw 'Null-layout cash quantity backing field changed'}
    Write-Output 'PASS bank/cash trade signatures, cash debit remaining<=0, integer payout/flags, direct quantity field and native account/audit/PF fences (static only)'
}finally{$a.Dispose()}
$unityPath=Join-Path $managed 'UnityEngine.CoreModule.dll'
if((Get-FileHash -LiteralPath $unityPath).Hash -ne '68D360251A7AEA19CBA74626D5E9D55E801C51DDA1F4B153B5BA4825C19C9E35'){throw 'Cash rounding Unity hash changed'}
$unity=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($unityPath)
try {
    $round=$unity.MainModule.GetType('UnityEngine.Mathf').Methods|Where-Object{$_.Name -eq 'RoundToInt'}
    if(($round.Body.Instructions|Where-Object{$_.Offset -eq 3}).Operand.FullName -ne 'System.Double System.Math::Round(System.Double)' -or ($round.Body.Instructions|Where-Object{$_.Offset -eq 8}).OpCode.Name -ne 'conv.i4'){throw 'Native cash rounding no longer Math.Round-to-even'}
    Write-Output 'PASS native Mathf.RoundToInt -> Math.Round(double) then int (static only)'
}finally{$unity.Dispose()}

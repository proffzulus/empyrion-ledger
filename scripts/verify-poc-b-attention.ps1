param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$game=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash $game).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Attention/window native assembly mismatch'}
Add-Type -Path (Join-Path $managed 'Trivial.Mono.Cecil.dll')
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($game)
try{
 if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Attention/window MVID mismatch'}
 $fields=@{0x0400aa90='System.Collections.Generic.List`1<Assembly-CSharp.PageSetConnectionToken>';0x0400697b='Eleon.Control';0x0400c186='Assembly-CSharp.FileTreeConnectionToken';0x0400c137='System.Boolean';0x04005d6f='Vector3i';0x04005d70='Vector3i'}
 foreach($token in $fields.Keys){$f=[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$token);if($f.FieldType.FullName -ne $fields[$token] -or $f.IsStatic){throw ('Attention field mismatch {0:X8}' -f $token)}}
 $get=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600bc76);$click=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06007157);$enabled=[LedgerCurrentEvidence]::Lookup($a.MainModule,0x06007167)
 if(!$get.IsStatic -or $get.ReturnType.FullName -ne 'Eleon.Ngui.UIActionGlobalManager' -or $click.IsStatic -or $click.ReturnType.FullName -ne 'System.Void' -or $click.Parameters.Count -ne 0 -or $enabled.ReturnType.FullName -ne 'System.Boolean'){throw 'Native UI action signatures changed'}
 function Uses([int]$method,[int]$token){if(!([LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object {$_.Operand -is [Trivial.Mono.Cecil.MemberReference] -and [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()) -eq $token})){throw ('Missing native action proof {0:X8}->{1:X8}' -f $method,$token)}}
 Uses 0x06007157 0x06007167
 Uses 0x06007157 0x0400697c
 Uses 0x06007167 0x04006982
 Uses 0x06007167 0x04006983
 Uses 0x0600bc7a 0x0400aa90
 Uses 0x0600bc7a 0x06007164
 Uses 0x0600cde7 0x0400c137
 Uses 0x0600cde5 0x0600cde7
 $controls=$a.MainModule.Types|Where-Object FullName -eq 'Eleon.Control'
 foreach($p in @{PDA=25;TechTree=24;LogisticsMenu=78;ShowPrefabLibrary=76}.GetEnumerator()){if(($controls.Fields|Where-Object Name -eq $p.Key).Constant -ne $p.Value){throw 'Native window control identity changed'}}
 $proof=@(foreach($t in @(0x0600bc76,0x0600bc7a,0x06007157,0x06007167,0x0600cde5,0x0600cde7)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$t);[ordered]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object ToString)}})
 $out=Join-Path $PSScriptRoot '..\artifacts\poc-b\verification\marker-attention';New-Item -ItemType Directory -Path $out -Force|Out-Null
 [ordered]@{assemblyHash=(Get-FileHash $game).Hash;mvid=$a.MainModule.Mvid.ToString();evidence='Static only. Registered global OptionsInfo UI action selected by exact control, OnClick honors native CanExecute. Release/fade handoff; native closability BF36 retained. Native window key aliases are runtime checked; no live registration/keyboard acceptance claimed. Aggregates use recorded PF destination/system+sector metadata, not fabricated coordinates.';fields=@(foreach($t in $fields.Keys){[LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$t).FullName});methods=$proof}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'native-contract.json') -Encoding UTF8
 'Marker attention/native window contracts passed (static only).'
}finally{$a.Dispose()}

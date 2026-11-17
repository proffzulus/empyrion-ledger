param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop';$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cecil=Join-Path $GameRoot 'Client\Empyrion_Data\Managed\Trivial.Mono.Cecil.dll'
$null=[Reflection.Assembly]::LoadFrom($cecil)
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\NativeInstallAudit.cs.txt'))) -ReferencedAssemblies $cecil,'System.Core','System.Web.Extensions'
Add-Type -Path (Join-Path $PSScriptRoot 'native-contract-evidence.cs') -ReferencedAssemblies $cecil,'System.Core','System.Web.Extensions'
$folder=Join-Path $root ('artifacts\release\fixtures\native-forward-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory $folder
$path=Join-Path $folder 'OwnedFixture.dll';$tokens=Join-Path $folder 'tokens.txt';$facts=Join-Path $folder 'facts.json'
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::CreateAssembly((New-Object Trivial.Mono.Cecil.AssemblyNameDefinition('OwnedFixture',[version]'1.0.0.0')),'OwnedFixture',[Trivial.Mono.Cecil.ModuleKind]::Dll)
$type=New-Object Trivial.Mono.Cecil.TypeDefinition('Owned','Context',([Trivial.Mono.Cecil.TypeAttributes]::Public),$a.MainModule.TypeSystem.Object);$a.MainModule.Types.Add($type)
function Method([string]$name,[int]$value){
 $m=New-Object Trivial.Mono.Cecil.MethodDefinition($name,([Trivial.Mono.Cecil.MethodAttributes]'Public,Static'),$a.MainModule.TypeSystem.Int32)
 $il=$m.Body.GetILProcessor();$il.Append($il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldc_I4,$value));$il.Append($il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ret));return $m
}
$one=Method 'Original' 1;$two=Method 'Optional' 2;$type.Methods.Add($one);$type.Methods.Add($two);$a.Write($path)
[IO.File]::WriteAllLines($tokens,@('06000001','06000002'));[LedgerContractEvidence]::Contracts($path,$tokens,$facts)
$decoded=Get-Content $facts -Raw|ConvertFrom-Json;$specs=@(foreach($entry in $decoded){$entry});$fixtureProfile=[ordered]@{Identity='irrelevant future label';Hash='deliberately unknown';Members=$specs;Capabilities=[ordered]@{journal=@(0x06000001);optional=@(0x06000002)}}
$checks=0
function Assert([bool]$v,[string]$name){if(!$v){throw "FAIL $name"};$script:checks++;Write-Output "PASS $name"}
[IO.File]::WriteAllText((Join-Path $folder 'fixture-profile.json'),($fixtureProfile|ConvertTo-Json -Depth 10))
function Results{[LedgerInstallContractAudit]::Inspect($path,($fixtureProfile|ConvertTo-Json -Depth 10))}
function Save{$a.Write($path)}
$r=Results;Assert ($r -contains 'journal=ready' -and $r -contains 'optional=ready') 'unknown hash and changed label pass full semantics'
$insert=Method 'InsertedBefore' 9;$type.Methods.Insert(0,$insert);Save;$r=Results;Assert ($r -contains 'journal=ready') 'shifted native metadata token not trusted or required'
$one.Name='Renamed';Save;$r=Results;Assert ($r -contains 'journal=ready') 'unique renamed method resolves by exact owner ABI and complete IL'
$two.Body.Instructions[0].Operand=[int]3;Save;$r=Results;Assert (@($r|Where-Object {$_ -like 'optional=unavailable:*'}).Count -eq 1) 'changed arithmetic constant rejects optional feature'
Assert ($r -contains 'journal=ready') 'independent journal survives optional semantic failure'
$duplicate=Method 'AnotherSameBody' 1;$type.Methods.Add($duplicate);Save;$r=Results;Assert (@($r|Where-Object {$_ -like 'journal=unavailable:*ambiguous*'}).Count -eq 1) 'identical renamed candidates fail ambiguous rather than select first'
$type.Methods.Remove($duplicate)|Out-Null;$type.Name='DifferentContext';Save;$r=Results;Assert (@($r|Where-Object {$_ -like 'journal=unavailable:*missing-owner*'}).Count -eq 1) 'renamed unproven owner not guessed by shape'
$type.Name='Context';$one.Name='Original';$two.Body.Instructions[0].Operand=[int]2;Save;$r=Results;Assert ($r -contains 'journal=ready' -and $r -contains 'optional=ready') 'settled compatible image recovers on independent audit'
$specs[0].Operands=@('wrong dependency');$r=Results;Assert (@($r|Where-Object {$_ -like 'journal=unavailable:*semantic*'}).Count -eq 1) 'unchanged opcode bytes with changed operand contract rejected'
$a.Dispose()
[ordered]@{checks=$checks;fixture=$folder;scope='isolated owned synthetic DLLs; no game/save writes or game methods invoked'}|ConvertTo-Json

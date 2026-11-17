param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
if((Get-FileHash -LiteralPath $path).Hash -ne '28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369'){throw 'Journal game hash mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
function Types($types){foreach($t in $types){$t;if($t.HasNestedTypes){Types $t.NestedTypes}}}
$direct=@()
foreach($file in @('Assembly-CSharp.dll','NGUIAssembly.dll')){
    . (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed $file))
    try{
        if($file -eq 'Assembly-CSharp.dll'){
            if($a.MainModule.Mvid.ToString() -ne '462483cd-2f1f-4a24-9c83-46d24b1cb3f4'){throw 'Journal MVID mismatch'}
            foreach($pair in @(@(0x0600cde5,'System.Boolean Assembly-CSharp.RegistryTreeConnectionToken::CloseXmlFile()'),@(0x0600ae60,'System.Boolean Assembly-CSharp.BitmapListConnectionToken::get_UpdatePartition()'),@(0x06006f09,'System.Boolean DebugPanelManager::get_OrderVector()'))){if([LedgerCurrentEvidence]::Lookup($a.MainModule,[int]$pair[0]).FullName -ne $pair[1]){throw 'Journal native UI signature mismatch'}}
            if([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400a126).FieldType.FullName -ne 'System.Boolean'){throw 'Native input-block flag changed'}
        }
        foreach($t in Types $a.MainModule.Types){foreach($m in $t.Methods){if(!$m.HasBody){continue};foreach($i in $m.Body.Instructions){
            if($i.Previous -and $i.Operand -is [Trivial.Mono.Cecil.MethodReference] -and $i.Operand.DeclaringType.FullName -eq 'UnityEngine.Input' -and $i.Operand.Name -match '^GetKey' -and $i.Previous.Operand -in @(93,']','right bracket')){$direct+=$file+':'+$m.FullName}
        }}}
    }finally{$a.Dispose()}
}
if($direct.Count){throw ('RightBracket direct native conflict: '+($direct -join ';'))}
$prefs=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.CoreModule.dll'))
try{
    $type=$prefs.MainModule.GetType('UnityEngine.PlayerPrefs')
    foreach($signature in @('System.String UnityEngine.PlayerPrefs::GetString(System.String,System.String)','System.Void UnityEngine.PlayerPrefs::SetString(System.String,System.String)','System.Void UnityEngine.PlayerPrefs::Save()')){
        if(!$type -or !@($type.Methods|Where-Object {$_.IsStatic -and $_.FullName -eq $signature}).Count){throw ('Journal UI preference API missing '+$signature)}
    }
}finally{$prefs.Dispose()}
$config=Join-Path $GameRoot 'Saves\inputconfig_v35.xml'
[xml]$outer=[IO.File]::ReadAllText($config)
$maps=0;$bindings=0;$f8=0;$right=0
foreach($node in $outer.SelectNodes('//xml')){
    if($node.InnerText -notmatch '<KeyboardMap'){continue}
    [xml]$map=$node.InnerText;$maps++
    foreach($entry in $map.SelectNodes('//*[local-name()="ActionElementMap"]')){
        $key=$entry.SelectSingleNode('*[local-name()="keyboardKeyCode"]');if($null -eq $key){continue};$bindings++
        if($key.InnerText -eq '93'){$right++};if($key.InnerText -eq '289'){$f8++}
    }
}
if($maps -eq 0 -or $right -ne 0){throw 'Saved keyboard maps unavailable or RightBracket already assigned'}
Write-Output "PASS journal current-profile: maps=$maps bindings=$bindings RightBracket93=$right F8-289=$f8; direct native key93 conflicts=0; native modal/typing tokens verified; config SHA256=$((Get-FileHash -LiteralPath $config).Hash). OS-layout/Unity focus remains live-test pending."

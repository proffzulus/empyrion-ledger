param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$profile=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\NativeContracts5232.json') -Raw|ConvertFrom-Json
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash $path).Hash -ne $profile.Hash -or $profile.Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Uncertified capability profile image'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
if(!('LedgerContractEvidence' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'native-contract-evidence.cs') -ReferencedAssemblies (Join-Path $managed 'Trivial.Mono.Cecil.dll'),'System.Core','System.Web.Extensions'}
$out=Join-Path $root 'artifacts\release\verification\compatibility-5232'
$null=New-Item -ItemType Directory -Force $out
$tokens=Join-Path $out 'profile-tokens.txt'
[IO.File]::WriteAllLines($tokens,@($profile.Members|ForEach-Object {'{0:X8}' -f $_.NativeToken}))
$actualPath=Join-Path $out 'profile-native-actual.json'
[LedgerContractEvidence]::Contracts($path,$tokens,$actualPath)
$actual=@{};foreach($spec in (Get-Content $actualPath -Raw|ConvertFrom-Json)){$actual[[int]$spec.Token]=$spec}
foreach($spec in $profile.Members){
 $value=$actual[[int]$spec.NativeToken]
 if(!$value -or $value.Key -cne $spec.Key -or $value.Owner -cne $spec.Owner -or $value.Body -cne $spec.Body -or $value.Static -ne $spec.Static -or ($value.Operands -join "`n") -cne ($spec.Operands -join "`n")){throw ('Native feature contract changed: {0:X8}' -f $spec.Token)}
}
foreach($cap in $profile.Capabilities.PSObject.Properties){
 if(!$cap.Value.Count){throw ('Empty capability '+$cap.Name)}
 foreach($token in $cap.Value){$spec=@($profile.Members|Where-Object Token -eq $token);if($spec.Count -ne 1 -or !$actual.ContainsKey([int]$spec[0].NativeToken)){throw ('Missing capability member '+$cap.Name)}}
}
foreach($member in @($contract.methods)+@($contract.fields)){if($profile.Capabilities.'runtime-core' -notcontains $member.token){throw 'Original passive hook contract omitted'}}
$source=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\LedgerNativeContracts.cs'))
foreach($guard in @('if(!b.Known||value==null||Key(value)!=spec.Key)','matches.Length!=1','ValidateBody(value,spec)','b.Results.TryGetValue','b.Members.TryGetValue','Safe.Hash(module.Assembly.Location)','LOCALS|','INIT|','EH|')){if(!$source.Contains($guard)){throw ('Missing capability safety/cache gate '+$guard)}}
$record=[ordered]@{target=$profile.Identity;sha256=$profile.Hash;members=$profile.Members.Count;capabilities=@($profile.Capabilities.PSObject.Properties|ForEach-Object {[ordered]@{name=$_.Name;members=$_.Value.Count}});proof='Cecil metadata and masked IL/operand/local/exception contracts; all original 8 hooks/92 fields retained; no native invocation';limitations='Moved tokens and uniquely behavior-identical method renames in an exact owner are supported; ambiguous/renamed owners or fields fail closed. Actual Mono/Unity initialization and Harmony detours remain live acceptance.'}
$record|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'native-capabilities.json') -Encoding UTF8
'PASS native capabilities: '+$profile.Members.Count+' contracts / '+$profile.Capabilities.PSObject.Properties.Name.Count+' independent feature groups (static only)'

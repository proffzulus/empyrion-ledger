[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Route inventory native hash changed'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try{
    if($a.MainModule.Mvid.ToString() -ne $contract.moduleMvid){throw 'Route inventory MVID changed'}
    $methods=@(foreach($token in @(0x060033F6,0x060033F5,0x0600CD26)){
        $v=[LedgerCurrentEvidence]::Lookup($a.MainModule,$token)
        [ordered]@{token=('{0:X8}' -f $token);signature=$v.FullName;il=@($v.Body.Instructions|ForEach-Object {$line=$_.ToString();if($_.Operand -is [Trivial.Mono.Cecil.FieldReference]){$line+=' fieldToken='+('{0:X8}' -f [LedgerCurrentEvidence]::Canonical($_.Operand.MetadataToken.ToInt32()))};$line})}
    })
    if(($methods[0].il -join ' ') -notmatch 'ldarg.2.*fieldToken=04002FEC'){throw 'Native constructor auxiliary-to-ammo identity changed'}
    if(($methods[1].il -join ' ') -notmatch 'fieldToken=04002FEC.*ItemStack::ammo'){throw 'Native stack ammo projection changed'}
    if(($methods[2].il -join ' ') -notmatch 'fieldToken=04005347.*OptionsSerializer::.ctor'){throw 'Trader auxiliary-to-item constructor changed'}
    $out=Join-Path $root 'artifacts\poc-b\verification\route-purchases'
    $null=New-Item -ItemType Directory -Force $out
    [ordered]@{source=$path;sha256=(Get-FileHash $path).Hash;mvid=$a.MainModule.Mvid.ToString();methods=$methods;scope='Static only: merchant auxiliary532C -> TextFileList constructor second argument -> raw2FD5 -> ItemStack.ammo. Existing guarded field copies only, no getter or native invocation. Actual Unity matching unverified.'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'native-proof-route.json') -Encoding UTF8
    Write-Output 'Route inventory native identity passed: trader auxiliary -> raw2FD5/ItemStack.ammo, no native invocation.'
}finally{$a.Dispose()}

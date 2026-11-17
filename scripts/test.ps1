[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$MSBuildPath)
$ErrorActionPreference='Stop';$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$managed=Join-Path $GameRoot 'Client/Empyrion_Data/Managed';$build=Join-Path $root 'artifacts/poc-b/build'
$previous=$env:EMPYRION_PROBE_MANAGED;Push-Location $root
try{
 $env:EMPYRION_PROBE_MANAGED=$managed
 & python (Join-Path $root 'tests/test_source_audit.py')
 if($LASTEXITCODE -ne 0){throw 'Source publication boundary tests failed'}
 & $MSBuildPath (Join-Path $root 'tests/poc-b/TradeLedgerProbeB.Tests.csproj') /t:Rebuild /nologo /verbosity:minimal "/p:GameManaged=$managed" "/p:ProbeDll=$(Join-Path $build 'TradeLedger.dll')" "/p:AdapterDll=$(Join-Path $build 'TradeLedger.Runtime.dll')" "/p:HarmonyDll=$(Join-Path $root 'vendor/Harmony-2.3.3-fieldtypefix/0Harmony.dll')"
 if($LASTEXITCODE -ne 0){throw 'Synthetic test compilation failed'}
 & (Join-Path $root 'artifacts/poc-b/tests/TradeLedgerProbeB.Tests.exe')
 if($LASTEXITCODE -ne 0){throw 'Synthetic regressions failed'}
 & (Join-Path $PSScriptRoot 'test-native-forward-profile.ps1') -GameRoot $GameRoot
 & (Join-Path $PSScriptRoot 'test-native-install-audit.ps1') -GameRoot $GameRoot
 & python (Join-Path $PSScriptRoot 'prepare-trade-ledger-release.py');if($LASTEXITCODE -ne 0){throw 'Package validation failed'}
 & python (Join-Path $PSScriptRoot 'prepare-trade-ledger-release.py') --check;if($LASTEXITCODE -ne 0){throw 'Reproducible package check failed'}
 & (Join-Path $PSScriptRoot 'test-trade-ledger-release.ps1')
 & (Join-Path $PSScriptRoot 'test-trade-ledger-forward-install.ps1') -GameRoot $GameRoot
}finally{$env:EMPYRION_PROBE_MANAGED=$previous;Pop-Location}
'PUBLIC OFFLINE VERIFICATION PASSED. No game process control, actual deployment, save or private fixture access occurred.'

param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'
$path=Join-Path $managed 'Assembly-CSharp.dll'
$contract=Get-Content (Join-Path $root 'src\TradeLedgerProbeB\contract-5232.json') -Raw|ConvertFrom-Json
if((Get-FileHash $path).Hash -ne $contract.hashes.'Assembly-CSharp.dll'){throw 'Vendor current1.20 target mismatch'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $managed 'Trivial.Mono.Cecil.dll'))
. (Join-Path $PSScriptRoot 'native-current-profile.ps1')
$a=[Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
 if($a.MainModule.Mvid.ToString('D') -ne $contract.moduleMvid){throw 'Vendor MVID mismatch'}
 function Name($type){return $type.FullName.Replace('/','+')}
 function Operand([int]$method,[int]$offset,[int]$token){$i=[LedgerCurrentEvidence]::Lookup($a.MainModule,$method).Body.Instructions|Where-Object Offset -eq $offset;if(!$i -or [LedgerCurrentEvidence]::Canonical($i.Operand.MetadataToken.ToInt32()) -ne $token){throw ('Vendor relationship changed {0:X8}+{1:X4}' -f $method,$offset)}}
 $controller=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C01B).DeclaringType)
 $bag=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400AD20).DeclaringType)
 $row=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C038).DeclaringType)
 $closure=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C047).DeclaringType)
 $model=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C031).FieldType)
 $entry=Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x04005346).DeclaringType)
 if($controller -ne 'Assembly-CSharp.PcitureEditorConnectionToken' -or $bag -ne 'Assembly-CSharp.FormSiteConnectionToken' -or $row -ne ($controller+'+DeviceEventArgsConnectionToken') -or $closure -ne ($controller+'+GroupProviderConnectionToken') -or $model -ne 'Assembly-CSharp.ActionEditor' -or $entry -ne ($model+'+DriveResolver')){throw 'Vendor source roles/type identities changed'}
 if((Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400533E).FieldType.GenericArguments[0])) -ne $entry -or (Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C046).FieldType)) -ne $row -or (Name ([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0600CD33).DeclaringType)) -ne $closure){throw 'Native raw-list/row/closure relationship changed'}
 Operand 0x0600CD1F 0x0041 0x0600CD32
 Operand 0x0600CD1F 0x004B 0x0400C047
 Operand 0x0600CD1F 0x006D 0x0400C044
 Operand 0x0600CD1F 0x00FB 0x0600CD2A
 Operand 0x0600CD1F 0x0100 0x0400C046
 Operand 0x0600CD1F 0x0175 0x0400C045
 Operand 0x0600CD1F 0x0183 0x0600CD33
 # Registration stores11 controller scalars and replaces two labels from THIS WindowStub.
 Operand 0x0600CDE1 0x0289 0x0400C198
 Operand 0x0600CDE1 0x028E 0x0400C135
 Operand 0x0600CDE1 0x029B 0x0400C199
 Operand 0x0600CDE1 0x02A0 0x0400C136
 if([LedgerCurrentEvidence]::Lookup($a.MainModule,0x0400C197).FieldType.FullName -ne 'System.Boolean'){throw 'Controller-backed WindowStub gate changed'}
 $source=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\DirectEvidence.cs'))
 foreach($text in @(('Bag="'+$bag+'"'),'Row=Controller+"+DeviceEventArgsConnectionToken"','Closure=Controller+"+GroupProviderConnectionToken"','Entry=Model+"+DriveResolver"','private const string Row = LedgerTraderTypes.Row','private const string Closure = LedgerTraderTypes.Closure','f.Exact(entry, LedgerTraderTypes.Entry)')){if(!$source.Contains($text)){throw ('Reader/current schema mismatch '+$text)}}
 $capture=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\TraderCaptureRuntime.cs'))
 foreach($text in @('bag.GetType().FullName == LedgerTraderTypes.Bag','ConfigCopiesMatch(config, Field(0xc01b, controller),root)','ConfigCopiesMatch(bagConfig, Field(0xad20, bag),bagRoot)','stub.gameObject==root&&!Field<bool>(0xc197,stub)','LedgerProduction.ConfigurationMismatch')){if(!$capture.Contains($text)){throw ('Vendor UI gate missing '+$text)}}
 $reader=[IO.File]::ReadAllText((Join-Path $root 'src\TradeLedgerProbeB\RuntimeObserver.cs'))
 if(!$reader.Contains('row.GetType().FullName != LedgerTraderTypes.Row')){throw 'Trader row pipeline disconnected from certified schema'}
 $out=Join-Path $root 'artifacts\poc-b\verification\vendor-markers-5229'
 $null=New-Item -ItemType Directory -Force $out
 [ordered]@{hash=$contract.hashes.'Assembly-CSharp.dll';mvid=$contract.moduleMvid;Controller=$controller;Bag=$bag;Row=$row;Closure=$closure;Model=$model;Entry=$entry;methods=@(foreach($t in @(0x0600CD1F,0x0600CD2A,0x0600CD33)){$m=[LedgerCurrentEvidence]::Lookup($a.MainModule,$t);[ordered]@{token=('{0:X8}' -f $t);signature=$m.FullName;il=@($m.Body.Instructions|ForEach-Object {$_.ToString()})}});limit='Static Cecil, no native invocation. Current publication/selection/source roles, not guessed shape matching.'}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $out 'native-vendor-contract.json') -Encoding UTF8
 'PASS vendor1.20: real Bag/row/closure/raw entry identities, native row publication and selection chain, same-root registration overrides; no native invocation'
} finally {$a.Dispose()}

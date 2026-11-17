# Offline Cecil adapter: stable Ledger contract IDs are not game metadata IDs.
# The current profile's audited NativeToken translation preserves old semantic
# assertions without mutating the game image or its Cecil metadata.
if(!$managed){$managed=Join-Path $GameRoot 'Client\Empyrion_Data\Managed'}
$ledgerProfile=Get-Content (Join-Path $PSScriptRoot '..\src\TradeLedgerProbeB\NativeContracts5232.json') -Raw|ConvertFrom-Json
if(!('LedgerCurrentEvidence' -as [type])){
 Add-Type -TypeDefinition @'
using System;using System.Collections.Generic;using Trivial.Mono.Cecil;
public static class LedgerCurrentEvidence {
 public static readonly Dictionary<int,int> Native=new Dictionary<int,int>(),Ids=new Dictionary<int,int>();
 public static IMetadataTokenProvider Lookup(ModuleDefinition module,int id){int actual;return module.LookupToken(module.Assembly.Name.Name=="Assembly-CSharp"&&Native.TryGetValue(id,out actual)?actual:id);}
 public static int Canonical(int native){int id;return Ids.TryGetValue(native,out id)?id:native;}
}
'@ -ReferencedAssemblies (Join-Path $managed 'Trivial.Mono.Cecil.dll'),'System.Core'
 foreach($spec in $ledgerProfile.Members){$id=[int]$spec.Token;$native=$(if($spec.NativeToken){[int]$spec.NativeToken}else{$id});[LedgerCurrentEvidence]::Native.Add($id,$native);[LedgerCurrentEvidence]::Ids.Add($native,$id)}
}

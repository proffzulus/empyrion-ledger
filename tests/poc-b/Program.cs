using System;using System.IO;using System.Reflection;
namespace BOfflineTests {internal static class PublicTests {
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
 public static int Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{var p=Path.Combine(Environment.GetEnvironmentVariable("EMPYRION_PROBE_MANAGED"),new AssemblyName(args.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
  try {var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   LedgerServiceHorizonTests.Run(Check,root);LedgerDepositHudWorkTests.Run(Check);LedgerDepositHudTests.Run(Check,root);LedgerDepositDiscoveryTests.Run(Check,root);
   LedgerGraphOverviewTests.Run(Check,root);LedgerDistanceTests.Run(Check,root);LedgerProfilerTests.Run(Check,root);LedgerJournalLifecycleTests.Run(Check,root);
   LedgerUiConsistencyTests.Run(Check,root);LedgerPlacesContextTests.Run(Check,root);LedgerDevicesFilterTests.Run(Check,root);LedgerRoutePurchaseTests.Run(Check,root);LedgerRequestSellerTests.Run(Check,root);
   LedgerOfferFreshnessTests.Run(Check,root);LedgerMarkerIdentityTests.Run(Check,root);LedgerProductionDueTests.Run(Check,root);
   Console.WriteLine("PUBLIC SYNTHETIC CHECKS PASSED: "+checks);return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}}

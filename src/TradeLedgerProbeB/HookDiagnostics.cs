using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;

namespace TradeLedgerProbeB.Runtime
{
    public static class HookContract
    {
        public static string TypeName(Type type)
        {
            if (type.IsGenericType)
                return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
            return type.FullName;
        }
        public static string Signature(MethodInfo method)
        {
            return TypeName(method.ReturnType) + " " + method.DeclaringType.FullName + "::" + method.Name +
                "(" + string.Join(",", method.GetParameters().Select(p => TypeName(p.ParameterType))) + ")";
        }
        public static bool Matches(MethodInfo method, MethodSpec spec)
        {
            return LedgerNativeContracts.Token(method) == spec.Token && method.DeclaringType.FullName == spec.Type &&
                method.Name == spec.Name && method.GetParameters().Length == spec.ParameterCount &&
                Signature(method) == spec.Signature && !method.IsStatic && !method.IsAbstract &&
                !method.ContainsGenericParameters && !method.DeclaringType.IsValueType && method.ReturnType == typeof(void);
        }
        public static string Describe(MethodInfo method)
        {
            MethodBody body = method.GetMethodBody();
            byte[] bytes = body == null ? null : body.GetILAsByteArray();
            string hash = "none";
            if (bytes != null) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            return "token=0x" + method.MetadataToken.ToString("X8") + " signature=" + Safe.Text(Signature(method), 700) +
                " impl=" + Safe.Text(method.GetMethodImplementationFlags().ToString()) + " generic=" + method.ContainsGenericParameters +
                " bodyBytes=" + (bytes == null ? 0 : bytes.Length) + " bodySha256=" + hash +
                " locals=" + (body == null ? 0 : body.LocalVariables.Count) + " eh=" + (body == null ? 0 : body.ExceptionHandlingClauses.Count) +
                " maxStack=" + (body == null ? 0 : body.MaxStackSize) + " initLocals=" + (body != null && body.InitLocals);
        }
    }

    // Dependency-injected state machine also exercised with deterministic failure fixtures.
    public sealed class HookPatchSession : IDisposable
    {
        private readonly IProbeHost host;
        private readonly Action<MethodInfo, MethodInfo> patch;
        private readonly Action<MethodInfo> remove;
        private readonly Func<MethodInfo, bool> isOwned;
        private readonly Func<Exception, bool> preDetourFailure;
        private readonly string scope;
        private readonly Action<MethodInfo, MethodInfo> preflight;
        private readonly List<MethodInfo> installed = new List<MethodInfo>();
        private readonly List<MethodInfo> uncertain = new List<MethodInfo>();
        public bool UnsafeState { get; private set; }
        public int Count { get { return installed.Count; } }
        public HookPatchSession(IProbeHost host, Action<MethodInfo, MethodInfo> patch, Action<MethodInfo> remove,
            Func<MethodInfo, bool> isOwned, Func<Exception, bool> preDetourFailure, string scope = "game-candidate", Action<MethodInfo, MethodInfo> preflight = null)
        { this.host = host; this.patch = patch; this.remove = remove; this.isOwned = isOwned; this.preDetourFailure = preDetourFailure; this.scope = scope; this.preflight = preflight; }

        public bool TryInstall(MethodInfo target, MethodInfo postfix)
        {
            if (UnsafeState) { host.Trace(TraceLevel.Error, "HookSkipped", "reason=prior-uncertain-patch-state token=0x" + LedgerNativeContracts.Token(target).ToString("X8")); return false; }
            string identity = "scope=" + scope + " token=0x" + LedgerNativeContracts.Token(target).ToString("X8") + " type=" + Safe.Text(target.DeclaringType.FullName) + " method=" + Safe.Text(target.Name);
            string stage = "inspect-body-and-callback";
            bool attempted = false;
            try
            {
                if (postfix == null || !postfix.IsStatic || postfix.ReturnType != typeof(void) ||
                    postfix.GetParameters().Any(p => p.ParameterType.IsByRef)) throw new InvalidOperationException("invalid-passive-callback");
                host.Trace(TraceLevel.Debug, "HookPatchBegin", "scope=" + scope + " " + HookContract.Describe(target) + " postfix=" + Safe.Text(postfix.Name) +
                    " injection="+(postfix.GetParameters().Any(p=>p.Name=="__args")?"bounded-args-array":"no-args-array")+"/no-original-method/no-ref stage=Harmony.Patch");
                stage = "replacement-preflight-no-detour";
                if (preflight != null) preflight(target, postfix);
                stage = "Harmony.Patch-create-and-detour";
                attempted = true;
                patch(target, postfix);
                stage = "verify-owned-postfix-registration";
                if (!isOwned(target)) throw new InvalidOperationException("postfix-returned-without-registration");
                installed.Add(target);
                host.Trace(TraceLevel.Info, "HookInstalled", identity + " postfix=" + Safe.Text(postfix.Name) + " kind=void-postfix registrationVerified=true callbackExecution=NOT-YET-OBSERVED");
                return true;
            }
            catch (Exception error)
            {
                host.Trace(TraceLevel.Error, "HookPatchFailed", identity + " stage=" + stage + " type=" + Safe.Text(error.GetType().FullName) + " callbackDisabledForTarget=true");
                ExceptionTrace.Write(host, "individual-hook", error, identity + " stage=" + stage);
                try
                {
                    bool owned = isOwned(target);
                    if (owned)
                    {
                        host.Trace(TraceLevel.Warn, "HookRollbackBegin", identity + " reason=owned-registration-present");
                        remove(target);
                        if (isOwned(target)) throw new InvalidOperationException("owned-postfix-remains-after-rollback");
                        host.Trace(TraceLevel.Info, "HookRollbackComplete", identity + " ownedRegistrationAbsent=true");
                    }
                    else if (!attempted || preDetourFailure(error))
                        host.Trace(TraceLevel.Warn, "HookRollbackSkipped", identity + " reason=no-owned-registration-and-pre-detour-failure noUnpatchRegeneration=true");
                    else
                        throw new InvalidOperationException("no-registration-but-native-detour-state-unproven", error);
                }
                catch (Exception cleanup)
                {
                    UnsafeState = true; uncertain.Add(target);
                    host.Trace(TraceLevel.Error, "HookRollbackUncertain", identity + " remainingPatchesStopped=true originalRestoration=UNVERIFIED");
                    ExceptionTrace.Write(host, "individual-hook-rollback", cleanup, identity);
                }
                return false;
            }
        }
        public void Dispose()
        {
            foreach (MethodInfo target in installed.Concat(uncertain).Distinct().Reverse())
            {
                string identity = "scope=" + scope + " token=0x" + LedgerNativeContracts.Token(target).ToString("X8") + " method=" + Safe.Text(target.Name);
                try
                {
                    if (!isOwned(target)) { host.Trace(TraceLevel.Debug, "HookRemoveSkipped", identity + " reason=no-owned-registration noUnpatchRegeneration=true"); continue; }
                    host.Trace(TraceLevel.Debug, "HookRemoveBegin", identity + " ownerOnly=true");
                    remove(target);
                    if (isOwned(target)) throw new InvalidOperationException("owned-postfix-remains-after-remove");
                    host.Trace(TraceLevel.Info, "HookRemoved", identity + " ownedRegistrationAbsent=true");
                }
                catch (Exception error)
                {
                    UnsafeState = true;
                    host.Trace(TraceLevel.Error, "HookRemoveError", identity + " callbackIsNoOp=true originalRestoration=UNVERIFIED");
                    ExceptionTrace.Write(host, "hook-remove", error, identity);
                }
            }
            installed.Clear(); uncertain.Clear();
        }
    }

    public static class HarmonyCanary
    {
        private static int hits;
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static int OwnEcho(int value) { return value + 1; }
        private static void OwnPostfix(int __0) { if (__0 == 41) hits++; }
        private static void EmptyPostfix() { }
        public static void Verify(IProbeHost host, Harmony harmony)
        {
            MethodInfo target = typeof(HarmonyCanary).GetMethod("OwnEcho", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(HarmonyCanary).GetMethod("OwnPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            hits = 0;
            host.Trace(TraceLevel.Info, "HarmonyCanaryBegin", "scope=own-adapter-only noGameMethodExecuted=true optional=true noDetours=true");
            bool simple = OptionalCanaryRunner.Run(host, "OwnEchoCopy", () =>
            {
                MethodInfo copy = ReplacementPreflight.Compile(target, postfix, host, "own-canary");
                var read = (Func<int, int>)copy.CreateDelegate(typeof(Func<int, int>));
                if (read(41) != 42 || hits != 1) throw new InvalidOperationException("own-canary-copy-output-or-postfix-failed");
                if (OwnEcho(41) != 42 || hits != 1) throw new InvalidOperationException("own-canary-original-changed");
            });
            bool fields = OptionalCanaryRunner.Run(host, "StaticFieldCollisionCopy", () => VerifyFieldCollision(host));
            host.Trace(TraceLevel.Info, "OptionalCanarySummary", "simple=" + simple + " staticFields=" + fields + " noDetours=true targetsRequireIndependentPreflight=true");
            if (simple && fields) host.Trace(TraceLevel.Info, "HarmonyCanaryPassed", "ownCopiesVerified=true noDetours=true gameCompatibility=UNKNOWN");
        }
        private static void VerifyFieldCollision(IProbeHost host)
        {
            FieldInfo[] fields = typeof(StaticCollisionFixture).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            if (fields.Length != 2 || fields.Any(f => f.Name != "containerLength") || fields[0].MetadataToken == fields[1].MetadataToken)
                throw new InvalidOperationException("static-field-fixture-not-prepared");
            var own = new StaticCollisionFixture();
            if (own.Read() != 10) throw new InvalidOperationException("own-static-field-original-failed");
            MethodInfo target = typeof(StaticCollisionFixture).GetMethod("Read");
            MethodInfo copy = ReplacementPreflight.Compile(target, typeof(HarmonyCanary).GetMethod("EmptyPostfix", BindingFlags.Static | BindingFlags.NonPublic), host, "own-static-field-canary");
            var read = (Func<StaticCollisionFixture, int>)copy.CreateDelegate(typeof(Func<StaticCollisionFixture, int>));
            if (read(own) != 10 || own.Read() != 10) throw new InvalidOperationException("own-static-field-copy-mismatch");
            host.Trace(TraceLevel.Info, "HarmonyFieldCanaryPassed", "scope=own-static-field-canary staticAssemblyFixture=true expectedResult=10 noDetourAttempted=true gameCodeExecuted=false");
        }
        public static HookPatchSession CreateSession(IProbeHost host, Harmony harmony, string scope = "game-candidate")
        {
            return new HookPatchSession(host,
                (target, postfix) => { harmony.Patch(target, postfix: new HarmonyMethod(postfix)); },
                target => harmony.Unpatch(target, HarmonyPatchType.Postfix, harmony.Id),
                target => { Patches info = Harmony.GetPatchInfo(target); return info != null && info.Postfixes.Any(p => p.owner == harmony.Id); },
                PreDetourFailure, scope, (target, postfix) => { ReplacementPreflight.Compile(target, postfix, host, scope); });
        }
        public static bool PreDetourFailure(Exception error)
        {
            // Pinned Harmony 2.3.3 PatchFunctions.UpdateWrapper creates the replacement BEFORE DetourMethod.
            // Only a direct InvalidProgramException in that creation frame is classified safe to isolate.
            string stack = error.StackTrace ?? "";
            return error is InvalidProgramException && stack.Contains("HarmonyLib.MethodPatcher.CreateReplacement") &&
                !stack.Contains("DetourMethod");
        }
    }
}

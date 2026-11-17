using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;

namespace TradeLedgerProbeB.Runtime
{
    // All reflective calls below are fixed metadata/compiler helpers in the hash-pinned
    // private Harmony dependency. Never invoke a game MethodInfo or copied game body.
    public static class ReplacementPreflight
    {
        private static readonly Assembly Library = typeof(Harmony).Assembly;
        private static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static object Call(MethodBase helper, object receiver, object[] args)
        {
            if (helper.DeclaringType.Assembly != Library) throw new InvalidOperationException("preflight-helper-not-pinned-library");
            try
            {
                var constructor = helper as ConstructorInfo;
                return constructor == null ? ((MethodInfo)helper).Invoke(receiver, args) : constructor.Invoke(args);
            }
            catch (TargetInvocationException error)
            {
                if (error.InnerException != null) ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        public static void AuditFields(MethodInfo target, IProbeHost host, string scope)
        {
            FieldOperandSpec[] expected = null;
            bool game = scope == "game-candidate";
            if (game && (!LedgerNativeContracts.Certified(target.Module) ||
                !BuildContract.Operands.TryGetValue(LedgerNativeContracts.Token(target), out expected)))
                throw new InvalidOperationException("preflight-game-operand-contract-missing");
            List<CodeInstruction> instructions = PatchProcessor.GetOriginalInstructions(target);
            FieldInfo[] actual = instructions.Select(i => i.operand as FieldInfo).Where(f => f != null).ToArray();
            if (actual.Length > 256) throw new InvalidOperationException("preflight-field-bound");
            if (game && actual.Length != expected.Length) throw new InvalidOperationException("preflight-original-field-count-mismatch");

            Type moduleType = Library.GetType("Mono.Cecil.ModuleDefinition", true);
            Type kind = Library.GetType("Mono.Cecil.ModuleKind", true);
            MethodInfo create = moduleType.GetMethods(Any).Single(m => m.Name == "CreateModule" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == kind);
            object module = Call(create, null, new[] { "TradeLedgerProbeB.MetadataAudit", Enum.Parse(kind, "Dll") });
            using ((IDisposable)module)
            {
                Type importerType = Library.GetType("MonoMod.Utils.MMReflectionImporter", true);
                ConstructorInfo constructor = importerType.GetConstructors(Any).Single(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == moduleType);
                object importer = Call(constructor, null, new[] { module });
                MethodInfo import = importerType.GetMethods(Any).Single(m => m.Name == "ImportReference" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(FieldInfo));
                MethodInfo resolve = Library.GetType("MonoMod.Utils.ReflectionHelper", true).GetMethods(Any).Single(m => m.Name == "ResolveReflection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.FullName == "Mono.Cecil.FieldReference");
                var seen = new HashSet<string>();
                for (int i = 0; i < actual.Length; i++)
                {
                    FieldInfo field = actual[i];
                    string declaring = HookContract.TypeName(field.DeclaringType), type = HookContract.TypeName(field.FieldType);
                    bool staticMatch = !game || (field.Name == expected[i].Name && declaring == expected[i].DeclaringType && type == expected[i].FieldType &&
                        ((expected[i].Token & unchecked((int)0xff000000)) != 0x04000000 || LedgerNativeContracts.Token(field) == expected[i].Token));
                    if (!staticMatch)
                    {
                        host.Trace(TraceLevel.Error, "FieldMappingRejected", "scope=" + scope + " methodToken=0x" + LedgerNativeContracts.Token(target).ToString("X8") +
                            " originalOffset=0x" + expected[i].Offset.ToString("X4") + " expectedFieldToken=0x" + expected[i].Token.ToString("X8") +
                            " actualFieldToken=0x" + LedgerNativeContracts.Token(field).ToString("X8") + " expectedType=" + Safe.Text(expected[i].FieldType) + " actualType=" + Safe.Text(type) + " noDetourAttempted=true");
                        throw new InvalidOperationException("preflight-original-field-identity-mismatch");
                    }
                    string key = field.Module.ModuleVersionId + ":" + LedgerNativeContracts.Token(field) + ":" + declaring + ":" + type;
                    if (!seen.Add(key)) continue;
                    object reference = Call(import, importer, new object[] { field, null });
                    var resolved = Call(resolve, null, new[] { reference }) as FieldInfo;
                    bool match = resolved != null && resolved.Module.ModuleVersionId == field.Module.ModuleVersionId && resolved.MetadataToken == field.MetadataToken &&
                        resolved.DeclaringType == field.DeclaringType && resolved.FieldType == field.FieldType && resolved.IsStatic == field.IsStatic;
                    host.Trace(match ? TraceLevel.Debug : TraceLevel.Error, match ? "FieldMappingVerified" : "FieldMappingRejected",
                        "scope=" + scope + " methodToken=0x" + LedgerNativeContracts.Token(target).ToString("X8") + " fieldToken=0x" + LedgerNativeContracts.Token(field).ToString("X8") +
                        " declaringType=" + Safe.Text(declaring) + " fieldName=" + Safe.Text(field.Name) + " fieldType=" + Safe.Text(type) +
                        " roundTripToken=" + (resolved == null ? "null" : "0x" + resolved.MetadataToken.ToString("X8")) + " metadataOnly=true noValuesRead=true");
                    if (!match) throw new InvalidOperationException("preflight-field-import-resolve-mismatch");
                }
            }
            host.Trace(TraceLevel.Debug, "FieldAuditPassed", "scope=" + scope + " token=0x" + LedgerNativeContracts.Token(target).ToString("X8") + " operands=" + actual.Length + " originalAndImportResolveMatched=true");
        }

        public static MethodInfo Compile(MethodInfo target, MethodInfo postfix, IProbeHost host, string scope, MethodInfo prefix = null)
        {
            string attribution = "scope=" + scope + " token=0x" + LedgerNativeContracts.Token(target).ToString("X8");
            AuditFields(target, host, scope);
            host.Trace(TraceLevel.Info, "ReplacementCompileBegin", attribution + " noDetourAttempted=true originalBodyNotInvoked=true");
            Type patcherType = Library.GetType("HarmonyLib.MethodPatcher", true);
            ConstructorInfo constructor = patcherType.GetConstructors(Any).Single(c => c.GetParameters().Length == 7);
            object patcher = Call(constructor, null, new object[] { target, null, prefix == null ? new List<MethodInfo>() : new List<MethodInfo> { prefix }, new List<MethodInfo> { postfix }, new List<MethodInfo>(), new List<MethodInfo>(), false });
            object[] args = { null };
            var replacement = (MethodInfo)Call(patcherType.GetMethod("CreateReplacement", Any), patcher, args);
            var emitted = args[0] as IDictionary<int, CodeInstruction>;
            if (emitted != null)
            {
                int count = 0;
                foreach (var pair in emitted.OrderBy(p => p.Key))
                {
                    MemberInfo member = pair.Value.operand as MemberInfo;
                    if (member == null || ++count > 128) continue;
                    var field = member as FieldInfo;
                    host.Trace(TraceLevel.Debug, "ReplacementOperand", attribution + " ilOffset=0x" + pair.Key.ToString("X4") +
                        " opcode=" + Safe.Text(pair.Value.opcode.Name) + " declaringType=" + Safe.Text(member.DeclaringType == null ? null : member.DeclaringType.FullName) +
                        " member=" + Safe.Text(member.Name) + (field == null ? "" : " fieldToken=0x" + LedgerNativeContracts.Token(field).ToString("X8") + " fieldType=" + Safe.Text(HookContract.TypeName(field.FieldType))) +
                        " codeMetadataOnly=true noLiteralStringsDumped=true");
                }
                if (count > 128) host.Trace(TraceLevel.Warn, "ReplacementOperandsTruncated", attribution + " memberLimit=128");
            }
            // Obtain/compile only the generated copy's entry point. No detour factory or
            // copied game delegate invocation occurs. A JIT rejection is therefore clean.
            Type platform = Library.GetType("MonoMod.Core.Platforms.PlatformTriple", true);
            MethodInfo current = platform.GetProperty("Current", Any).GetGetMethod(true);
            object triple = Call(current, null, new object[0]);
            MethodInfo native = platform.GetMethod("GetNativeMethodBody", Any, null, new[] { typeof(MethodBase) }, null);
            IntPtr pointer = (IntPtr)Call(native, triple, new object[] { replacement });
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("preflight-zero-copy-entrypoint");
            host.Trace(TraceLevel.Info, "ReplacementCompilePassed", attribution + " copyEntryPointObtained=true noDetourAttempted=true originalBodyNotInvoked=true");
            GC.KeepAlive(replacement);
            return replacement;
        }
    }
    public static class OptionalCanaryRunner
    {
        public static bool Run(IProbeHost host, string name, Action diagnostic)
        {
            try { diagnostic(); host.Trace(TraceLevel.Info, "OptionalCanaryPassed", "name=" + Safe.Text(name) + " noDetourAttempted=true"); return true; }
            catch (Exception error)
            {
                host.Trace(TraceLevel.Warn, "OptionalCanaryFailed", "name=" + Safe.Text(name) + " noDetourAttempted=true unrelatedTargetsRequireIndependentPreflight=true");
                ExceptionTrace.Write(host, "optional-canary", error, "name=" + Safe.Text(name) + " noDetourAttempted=true");
                return false;
            }
        }
    }
}

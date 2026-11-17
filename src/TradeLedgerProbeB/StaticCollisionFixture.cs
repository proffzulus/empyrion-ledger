using System.Runtime.CompilerServices;

namespace TradeLedgerProbeB.Runtime
{
    // Own code/data only. The build recipe renames these two private FieldDefs to
    // containerLength after compilation, preserving their distinct tokens/types.
    // A static assembly fixture avoids Reflection.Emit/TypeBuilder token behavior.
    public sealed class StaticCollisionFixture
    {
        private int integerValue = 7;
        private string textValue = "own";
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public int Read() { return integerValue + textValue.Length; }
    }
}

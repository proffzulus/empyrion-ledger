using System;
using System.Globalization;
using System.Text;

namespace TradeLedgerProbe
{
    internal static class Compact
    {
        // Never call arbitrary payload ToString(), properties, reflection or enumerators.
        internal static string Value(object value)
        {
            if (value == null) return "null";
            Type type = value.GetType();
            string prefix = Text(type.FullName, 160) + ":";
            if (value is string) return prefix + Text((string)value, 240);
            if (value is char) return prefix + Text(((char)value).ToString(), 1);
            if (value is bool) return prefix + ((bool)value ? "true" : "false");
            if (type.IsEnum) return prefix + Enum.Format(type, value, "D");
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
                type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
                type == typeof(double) || type == typeof(decimal))
                return prefix + ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            if (value is Guid) return prefix + ((Guid)value).ToString("D");
            // Even arrays expose only length, never contents.
            if (value is Array) return prefix + "<array length=" + ((Array)value).Length + ">";
            return prefix + "<opaque>";
        }

        internal static string Text(string text, int limit = 240)
        {
            if (text == null) return "null";
            var result = new StringBuilder("\"");
            int count = Math.Min(text.Length, limit);
            for (int i = 0; i < count; i++)
            {
                char ch = text[i];
                switch (ch)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\r': result.Append("\\r"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (char.IsControl(ch) || ch == '\u2028' || ch == '\u2029' ||
                            (ch >= '\u202a' && ch <= '\u202e') || (ch >= '\u2066' && ch <= '\u2069'))
                            result.Append("\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else result.Append(ch);
                        break;
                }
            }
            if (count < text.Length) result.Append("...[truncated; chars=" + text.Length + "]");
            return result.Append('"').ToString();
        }

        internal static string Read(Func<string> getter, int limit = 240)
        {
            try { return Text(getter(), limit); }
            catch (Exception error) { return "<error:" + error.GetType().Name + ">"; }
        }
    }
}

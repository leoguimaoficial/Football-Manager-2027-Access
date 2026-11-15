using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Minimal JSON object encoder for the live-values SSE payload: a flat map of element id to
/// display text, serialized as {"id":"text",...}. Hand-rolled rather than System.Text.Json
/// because reflection-based serializers are unreliable under IL2CPP, and the shape is
/// trivial. String escaping turns control characters into escapes, so the output is always
/// a single line and safe to put after an SSE "data:" field. The browser decodes it with
/// the built-in JSON.parse.
/// </summary>
public static class JsonMap
{
    public static string Encode(IReadOnlyDictionary<string, string> map)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var pair in map)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendString(sb, pair.Key);
            sb.Append(':');
            AppendString(sb, pair.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        if (s != null)
        {
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
        }
        sb.Append('"');
    }
}

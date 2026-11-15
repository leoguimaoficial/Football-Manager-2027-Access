using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Query-string parsing for route handlers. Shared by <see cref="AccessibilityServer"/>'s
/// generic routes and by feature route registrations so neither has to reach into the other.
/// </summary>
internal static class Query
{
    public static string Str(string query, string key)
    {
        foreach (var pair in query.Split('&'))
            if (pair.StartsWith(key + "="))
                return Uri.UnescapeDataString(pair.Substring(key.Length + 1));
        return "";
    }

    public static int Int(string query, string key)
    {
        foreach (var pair in query.Split('&'))
            if (pair.StartsWith(key + "=") && int.TryParse(pair.Substring(key.Length + 1), out var v))
                return v;
        return -1;
    }
}

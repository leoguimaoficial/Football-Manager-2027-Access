using System;

namespace FM27Access.Plugin.Server;

// Names arriving asynchronously change presentation, not the owning native
// panel/binding. All other token segments remain part of action ownership.
internal static class ActionScreen
{
    internal static bool Matches(string expected, string current)
    {
        // Retain the existing id-only developer-driver contract. Production
        // form commands send the screen snapshot they were rendered against.
        if (string.IsNullOrEmpty(expected)) return true;
        if (string.IsNullOrEmpty(current) || current == "?") return false;
        return string.Equals(Identity(expected), Identity(current), StringComparison.Ordinal);
    }

    private static string Identity(string token)
        => string.Join("\u001f", Array.FindAll(token.Split('\u001f'), part =>
            !(part.StartsWith("names:", StringComparison.Ordinal)
              && long.TryParse(part.Substring(6), out _))));
}

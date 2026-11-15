#nullable enable
using System;
using System.Collections.Generic;

namespace FM27Access.Plugin.Server;

// Compare official native strings only. A duplicate is ambiguous even when one
// row happens to occupy the stale cached index; never guess a replacement.
internal static class ChoiceCaptionMatch
{
    internal static int Unique(string? current, IReadOnlyList<string?> choices)
    {
        if (string.IsNullOrEmpty(current)) return -1;
        int found = -1;
        for (int i = 0; i < choices.Count; i++)
        {
            if (!string.Equals(current, choices[i], StringComparison.Ordinal)) continue;
            if (found >= 0) return -1;
            found = i;
        }
        return found;
    }
}

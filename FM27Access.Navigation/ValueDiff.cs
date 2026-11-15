using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Compares two snapshots of element id to display text and returns only what changed, so
/// the live-update channel sends the smallest possible payload. An entry is "changed" when
/// it is new (absent from the previous snapshot) or its text differs. Removals are NOT
/// reported here: a vanished element is a structural change handled separately (a refresh),
/// not a text update. Pure.
/// </summary>
public static class ValueDiff
{
    public static Dictionary<string, string> Changed(
        IReadOnlyDictionary<string, string> prev,
        IReadOnlyDictionary<string, string> curr)
    {
        var changed = new Dictionary<string, string>();
        foreach (var pair in curr)
        {
            if (!prev.TryGetValue(pair.Key, out var old) || old != pair.Value)
                changed[pair.Key] = pair.Value;
        }
        return changed;
    }
}

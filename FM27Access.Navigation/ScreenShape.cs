using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Composes a cheap structural "shape" signature of a screen from an ordered walk of its visible
/// elements (each contributing a name and its child count). Used only inside the post-navigation
/// settle window: the SSE loop compares this tick's shape to the last one and re-reads the screen
/// when it changes, so content that populates a few frames after a panel becomes "ready" shows up
/// without a manual refresh. Two equal shapes mean "nothing structural changed" (text-only changes
/// ride the separate values channel). Pure: no game or Unity dependency, unit-tested without the
/// game.
/// </summary>
public static class ScreenShape
{
    // FNV-1a 32-bit. Deterministic (unlike string.GetHashCode under hash randomization) and
    // order-sensitive, so adding, removing, or reordering elements changes the signature.
    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;

    public static string Compose(IEnumerable<(string name, int childCount)> nodes)
    {
        uint hash = FnvOffset;
        int count = 0;
        foreach (var (name, childCount) in nodes)
        {
            MixIn(ref hash, name);
            // Fold the child count in, then a record separator, so two adjacent nodes can never
            // run together ambiguously and a change in branching is caught.
            hash = (hash ^ (uint)childCount) * FnvPrime;
            hash = (hash ^ 0x1E) * FnvPrime;
            count++;
        }
        return count.ToString() + ":" + hash.ToString("x8");
    }

    private static void MixIn(ref uint hash, string s)
    {
        if (s == null) return;
        foreach (char c in s)
        {
            hash = (hash ^ (byte)c) * FnvPrime;
            hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
        }
    }
}

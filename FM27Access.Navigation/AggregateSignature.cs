using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Combines several per-panel signatures (screen tokens or shape hashes) into one value for the
/// multi-panel HTML surface (docs/archive/plans/removing panel cycling plan.md §7.4 / Phase 3). The SSE loop
/// watches this combined value: because it is order-sensitive and delimited, adding, removing, or
/// reordering a panel — or any single panel's own signature changing — moves the result, so a set
/// change that does not move the speech active panel still triggers a refresh. Pure: no game or
/// Unity dependency, unit-tested without the game.
/// </summary>
public static class AggregateSignature
{
    // A unit-separator (0x1F) delimiter that cannot occur in a panel identity, title, or shape hash,
    // so two different partitions of the same characters (["a","b"] vs ["ab"]) can never collide.
    private const char Separator = (char)0x1F;

    public static string Combine(IEnumerable<string> parts)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var part in parts)
        {
            if (!first) sb.Append(Separator);
            sb.Append(part);
            first = false;
        }
        return sb.ToString();
    }
}

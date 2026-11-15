using System;
using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Reads a display-text snapshot from a set of id-to-element sources, isolating per-element
/// read failures: a throw on one element drops that one entry (recorded in
/// <paramref name="failures"/>) and the rest are still read. Pure (no game/Unity dependency);
/// the caller supplies the read function and decides what to do with the failures (log +
/// untrack). This is what keeps a single dead tracked element from blinding the whole live
/// channel — and, upstream, screen-change detection. Unit-tested without the game.
/// </summary>
public static class LiveSnapshot
{
    public readonly record struct Failure(string Key, Exception Error);

    public static Dictionary<string, string> Build(
        IEnumerable<KeyValuePair<string, IAccessibleElement>> sources,
        Func<IAccessibleElement, string> read,
        out List<Failure> failures)
    {
        var snap = new Dictionary<string, string>();
        failures = null;
        foreach (var pair in sources)
        {
            try { snap[pair.Key] = read(pair.Value); }
            catch (Exception ex) { (failures ??= new List<Failure>()).Add(new Failure(pair.Key, ex)); }
        }
        return snap;
    }
}

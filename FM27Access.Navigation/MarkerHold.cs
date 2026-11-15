using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Keeps a transient "couldn't read this" marker out of the live-value push.
///
/// A panel that rebuilds its controls in place — the tunnel interview and press conference
/// recreate their answer buttons per question, the team talk its lines — leaves the elements
/// present for a few hundred ms before their text binds. A live-value sample taken inside that
/// window reads the honest unlabeled marker, and pushing it overwrites the correct labels the
/// last render put on the page. Usually a later sample pushed the bound text back; when a
/// screen/restructure event landed in between it adopted the whole snapshot as the new baseline,
/// so the diff went empty while the page still showed markers and the labels never returned
/// (unlabeled until a manual refresh).
///
/// The marker is provisional, not wrong: declining to push it leaves the page showing the
/// authoritative text from its last render rather than replacing it with a gap. The grace is
/// counted in samples and bounded, so a control that is genuinely unlabeled is still reported as
/// one shortly after — this defers an honest marker, it never suppresses one.
///
/// Pure (no game dependency) and stateful across samples, so one instance belongs to one live
/// channel. Unit-tested without the game.
/// </summary>
public sealed class MarkerHold
{
    private readonly string _marker;
    private readonly int _holdSamples;
    private readonly Dictionary<string, int> _held = new();

    /// <param name="marker">The degrade text to treat as provisional.</param>
    /// <param name="holdSamples">Consecutive samples an id may read as the marker before it is
    /// pushed as the control's real text.</param>
    public MarkerHold(string marker, int holdSamples)
    {
        _marker = marker;
        _holdSamples = holdSamples;
    }

    /// <summary>
    /// Forget every grace counter. Called when the page the ids belong to is left behind, which
    /// is also what bounds this map's growth over a session.
    /// </summary>
    public void Reset() => _held.Clear();

    /// <summary>
    /// Returns <paramref name="snapshot"/> with each newly-marker-valued entry held back:
    /// rewritten to <paramref name="baseline"/>'s value for that id (so it produces no diff, and
    /// the real text still diffs through when it binds) or dropped when the baseline has none.
    /// An id that has read as the marker for the full grace passes through unchanged. Returns the
    /// input instance when nothing is held, so an ordinary sample allocates nothing.
    /// </summary>
    public Dictionary<string, string> Apply(
        Dictionary<string, string> snapshot,
        IReadOnlyDictionary<string, string> baseline)
    {
        Dictionary<string, string> held = null;
        foreach (var pair in snapshot)
        {
            if (pair.Value != _marker)
            {
                if (_held.Count != 0) _held.Remove(pair.Key);
                continue;
            }

            _held.TryGetValue(pair.Key, out int samples);
            // Persisted past the grace: this is the control's real state, so let it through and
            // stop counting up (the push moves the baseline, so it pushes once, not every sample).
            if (samples >= _holdSamples) continue;
            _held[pair.Key] = samples + 1;

            held ??= new Dictionary<string, string>(snapshot);
            if (baseline.TryGetValue(pair.Key, out var previous)) held[pair.Key] = previous;
            else held.Remove(pair.Key);
        }

        // A panel that rebuilds in place keeps its screen token, so Reset never runs and ids
        // retired by the rebuild would accumulate. Drop them once the map outgrows the live set —
        // a stale count would otherwise shorten the grace of an id that comes back.
        if (_held.Count > snapshot.Count) PruneRetired(snapshot);

        return held ?? snapshot;
    }

    private void PruneRetired(Dictionary<string, string> snapshot)
    {
        var retired = new List<string>();
        foreach (var entry in _held)
            if (!snapshot.ContainsKey(entry.Key)) retired.Add(entry.Key);
        foreach (var key in retired) _held.Remove(key);
    }
}

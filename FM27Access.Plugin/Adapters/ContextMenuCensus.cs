using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The dev driver's record of every menu-mark decision made during one screen walk: for each control
/// that was asked "do you have a right-click menu?", which holder answered, of what kind, and how far
/// from the control it sat.
///
/// It exists because the mark is decided in one place (<see cref="ContextMenuOpener.Resolve"/>) but
/// EMITTED from a dozen render paths (plain stops, table cells, report headers, synthetic tables), so
/// reconstructing the marked set from the HTML loses the evidence for WHY each mark exists. Recording
/// at the single decision point captures every path for free, and the resulting census is what makes a
/// change to the scoping rule reviewable as a diff across the whole game rather than screen by screen.
///
/// Off by default and written only from the main thread (the walk), so the player's build path pays
/// nothing but a bool test.
/// </summary>
internal static class ContextMenuCensus
{
    /// <summary>One resolution, as it was decided during a walk.</summary>
    internal sealed class Entry
    {
        public string OwnerLabel;
        public string OwnerType;
        public bool Marked;
        public MenuHolderKind Kind;
        public string HolderType;
        public string HolderText;
        public int Depth;
        public int HolderCount;
    }

    // Bounded so a pathological screen (a fully streamed 500-row table) cannot grow the buffer without
    // limit. Overflow is counted and reported, never silently dropped.
    private const int MaxEntries = 4000;

    private static readonly List<Entry> _entries = new();

    public static bool Recording { get; private set; }

    public static int Dropped { get; private set; }

    public static void Begin()
    {
        _entries.Clear();
        Dropped = 0;
        Recording = true;
    }

    public static void End() => Recording = false;

    /// <summary>The entries recorded so far. Read while still recording — the report's own render pass
    /// runs inside the run so a mid-report failure still leaves the buffer intact for the finally.</summary>
    public static List<Entry> Snapshot() => _entries;

    /// <summary>
    /// Record one decision. Called from the adapter boundary, where the owner's rendered label is
    /// available — the holder text alone cannot say whether a mark is sensible; the comparison is
    /// against what the player HEARS the control called.
    /// </summary>
    public static void Record(IAccessibleElement owner, in MenuResolution r)
    {
        if (_entries.Count >= MaxEntries) { Dropped++; return; }
        _entries.Add(new Entry
        {
            OwnerLabel = ContextMenuOpener.OwnerLabel(owner),
            OwnerType = owner.GetType().Name,
            Marked = r.Target != null,
            Kind = r.Kind,
            HolderType = r.HolderType,
            HolderText = r.HolderText,
            Depth = r.Depth,
            HolderCount = r.HolderCount,
        });
    }

}

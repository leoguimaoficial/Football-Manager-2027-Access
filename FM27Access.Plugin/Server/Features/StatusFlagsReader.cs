using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Features;

/// <summary>
/// Reads every status flag out of a squad table's "Inf" cell.
///
/// The game renders that cell as an <c>SI.Bindable.ExpandingList</c> named "StatusFlags". Collapsed,
/// it parents exactly ONE badge into the row tree and builds the remainder inside a FloatingElement
/// only on hover or click, so a tree walk finds one flag however many the player carries — and the
/// cell reads as complete when it is not. On a live First Team table six of the nine flagged rows
/// were hiding a second flag, including "failed fitness test, not recommended to be selected".
///
/// The hidden flags are not reachable by walking (their elements are reparented into the popup and
/// do not exist until it opens), so this reads the widget's own data instead:
/// <list type="bullet">
/// <item>the always-visible badge, through its BindingRoot key and the binding subsystem — the same
/// read MatchSquadReadout uses for a match row;</item>
/// <item>every remaining flag, from <c>m_items</c>. TypedValue is an abstract class, so that list is
/// a generic over a reference type and none of the IL2CPP struct-generic hazard applies.</item>
/// </list>
/// Each entry is a DynamicReference, which derives from Record (Dictionary&lt;uint, TypedValue&gt;),
/// and its properties resolve by the game's own NAMES through PropertyIdentifierSet — never by their
/// hashed ids, which would go stale silently.
///
/// Nothing here expands the widget: no Expand/ShowFloatingElement call, no popup layer, and
/// m_floatingElement is never cast (a FloatingElement is not a VisualElement; casting one is a hard
/// process crash).
/// </summary>
internal static class StatusFlagsReader
{
    // The game's property names on a status-flag record, resolved through PropertyIdentifierSet.
    private const string ShortProp = "StatusTitleShort";
    private const string FullProp = "StatusTitleFull";
    private const string ReasonProp = "StatusReason";
    // The binding node child that carries the always-visible badge's own record.
    private const string FlagNode = "StatusFlag";
    // ExpandingList is a general-purpose widget; this is the row parameter a STATUS-FLAG one binds
    // its items under. Gating on it keeps an ExpandingList used for anything else out of this
    // reader entirely — without the gate, a widget holding some other shape would resolve none of
    // the status properties and get reported as an unreadable gap, replacing content that was
    // rendering perfectly well with a marker claiming it could not be read.
    private const string StatusRowParam = "statusflag";

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static;

    // Resolved once: this runs per cell per compose, and the lookups are the expensive part.
    private static readonly PropertyInfo ItemsProp =
        typeof(SI.Bindable.ExpandingList).GetProperty("m_items", All);
    private static readonly PropertyInfo AlwaysVisibleProp =
        typeof(SI.Bindable.ExpandingList).GetProperty("m_alwaysVisibleElement", All);

    /// <summary>
    /// The cell's flags, or null when the cell holds no status-flag widget at all (the normal case
    /// for every other column). <paramref name="unreadable"/> is set when the widget IS present but
    /// its flags could not be read — the caller must then render a gap marker rather than the
    /// partial list, because a partial list is indistinguishable from a complete one.
    /// </summary>
    internal static List<StatusFlagsHtml.StatusFlag> Read(VisualElement cell, VisualElement badge, out bool unreadable)
    {
        unreadable = false;
        var widget = FindExpandingList(cell, badge);
        if (widget == null) return null;

        SI.Bindable.ExpandingList typed;
        string rowParam;
        try
        {
            typed = widget.Cast<SI.Bindable.ExpandingList>();
            rowParam = typed.RowParamName;
        }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] ExpandingList cast failed ({e.GetType().Name}); cell flags unreadable.");
            unreadable = true;
            return null;
        }

        // Some other use of the widget: not this reader's data, and not this reader's gap to report.
        // Checked before anything can set the unreadable flag, so an unrelated ExpandingList is
        // never reported as a status-flag cell that failed.
        if (!string.Equals(rowParam, StatusRowParam, StringComparison.OrdinalIgnoreCase)) return null;

        if (ItemsProp == null || AlwaysVisibleProp == null)
        {
            Degrade.Note("[StatusFlags] ExpandingList backing fields not found; cell flags unreadable.");
            unreadable = true;
            return null;
        }

        var flags = new List<StatusFlagsHtml.StatusFlag>();

        // Flag 0 — the one badge the tree does hold. Its own text is the cross-check: a badge that
        // visibly says something but whose record will not read is a gap, not an empty cell.
        var visible = AlwaysVisible(typed);
        var first = visible == null ? null : ReadBoundFlag(visible);
        if (first != null) flags.Add(first);
        else if (visible != null && HasText(visible, 0))
        {
            Degrade.Note("[StatusFlags] visible badge present but its record would not read.");
            unreadable = true;
            return null;
        }

        // Flags 1..N — the collapsed remainder, which exists only as data until the popup opens.
        if (!ReadItems(typed, flags))
        {
            unreadable = true;
            return null;
        }

        // Never observed live, so record the first sighting rather than assume it away: every cell
        // seen so far hid exactly one flag, which leaves "m_items is the complete overflow" read
        // from the widget's code rather than from play.
        if (flags.Count > 2 && !_multiFlagSeen)
        {
            _multiFlagSeen = true;
            Log.Info($"[StatusFlags] first cell carrying {flags.Count} flags: m_items holds more than one hidden flag.");
        }

        // A widget that yielded nothing is not a status-flag cell as far as the renderer is
        // concerned: returning an empty list would blank a cell that still has its badge element to
        // show. Hand it back to the ordinary cell rendering instead.
        return flags.Count > 0 ? flags : null;
    }

    private static bool _multiFlagSeen;

    private static bool ReadItems(SI.Bindable.ExpandingList typed, List<StatusFlagsHtml.StatusFlag> into)
    {
        Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> items;
        try { items = ItemsProp.GetValue(typed) as Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>; }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] m_items read threw {e.GetType().Name}; cell flags unreadable.");
            return false;
        }
        if (items == null) return true; // no backing list yet: nothing collapsed, nothing to hide

        int count;
        try { count = items.Count; }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] m_items.Count threw {e.GetType().Name}; cell flags unreadable.");
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            SI.Core.TypedValue item;
            try { item = items[i]; }
            catch (Exception e)
            {
                Degrade.Note($"[StatusFlags] m_items[{i}] threw {e.GetType().Name}; cell flags unreadable.");
                return false;
            }
            var flag = ReadRecordCore(item, out bool empty);
            if (flag == null)
            {
                // FM27 retains an empty overflow record after a booked player
                // is withdrawn. All three native caption fields are explicitly
                // blank; it is not an unreadable flag or a missing property.
                if (empty) continue;
                Degrade.Note($"[StatusFlags] hidden flag {i} would not read; cell flags unreadable.");
                return false;
            }
            into.Add(flag);
        }
        return true;
    }

    private static VisualElement AlwaysVisible(SI.Bindable.ExpandingList typed)
    {
        try { return AlwaysVisibleProp.GetValue(typed) as VisualElement; }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] m_alwaysVisibleElement read threw {e.GetType().Name}.");
            return null;
        }
    }

    // The always-visible badge's flag record, reached through the binding subsystem: m_items holds
    // only the collapsed remainder, so this is the sole route to the badge the player already sees.
    private static StatusFlagsHtml.StatusFlag ReadBoundFlag(VisualElement element)
    {
        try
        {
            // FM27 wraps ExpandingListItem's BindingRoot in a TemplateContainer.
            // Stay within the visible badge, excluding its hidden tooltip roots.
            var root = FindBadgeBindingRoot(element, 0);
            if (root == null) return null;
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null) return null;
            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) return null;
            foreach (var child in node.Children)
            {
                if (child.Name != FlagNode) continue;
                var childKey = child.Key;
                var value = bindings.Get(ref childKey);
                return value == null ? null : ReadRecord(value.TryCast<SI.Core.TypedValue>());
            }
            return null;
        }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] visible badge binding read threw {e.GetType().Name}.");
            return null;
        }
    }

    private static SI.Bindable.BindingRoot FindBadgeBindingRoot(VisualElement element, int depth)
    {
        if (element == null || depth > 8 || !ElementBridge.IsVisible(element)) return null;
        var root = element.TryCast<SI.Bindable.BindingRoot>();
        if (root != null) return root;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var found = FindBadgeBindingRoot(element.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    // One flag's title and reason off its Record. The reason carries inline entity-link tokens (a
    // loan club, an Under 19s squad), so it goes through the same decode-then-clean every other
    // game string on this surface does.
    private static StatusFlagsHtml.StatusFlag ReadRecord(SI.Core.TypedValue value)
        => ReadRecordCore(value, out _);

    private static StatusFlagsHtml.StatusFlag ReadRecordCore(SI.Core.TypedValue value, out bool empty)
    {
        empty = false;
        if (value == null) return null;
        SI.Core.Record record;
        try { record = (value.Get() as Il2CppSystem.Object)?.TryCast<SI.Core.Record>(); }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] flag record unwrap threw {e.GetType().Name}.");
            return null;
        }
        if (record == null) return null;

        string shortName = null, full = null, reason = null;
        try
        {
            var registry = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var walker = record.GetEnumerator();
            while (walker.MoveNext())
            {
                var entry = walker.Current;
                string name = registry.GetInfo(entry.Key).Name;
                if (name == FullProp) full = Text(entry.Value);
                else if (name == ShortProp) shortName = Text(entry.Value);
                else if (name == ReasonProp) reason = Text(entry.Value);
            }
        }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] flag properties threw {e.GetType().Name}.");
            return null;
        }

        // The full title is what the player should hear; the abbreviation stands in only when the
        // game itself has no long form. Neither means there is nothing honest to render.
        string title = !string.IsNullOrEmpty(full) ? full : shortName;
        if (string.IsNullOrEmpty(title))
        {
            empty = full == "" && shortName == "" && reason == "";
            return null;
        }
        return new StatusFlagsHtml.StatusFlag(title, reason);
    }

    private static string Text(SI.Core.TypedValue value)
    {
        if (value == null) return null;
        string raw;
        try { raw = value.AsString(); }
        catch (Exception e)
        {
            Degrade.Note($"[StatusFlags] AsString threw {e.GetType().Name}.");
            return null;
        }
        if (string.IsNullOrEmpty(raw)) return raw;
        return TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(raw));
    }

    // Walks UP from the cell's own badge to the cell, not down through the cell's subtree. A
    // downward search would visit every descendant of every cell of every row — the mod's hottest
    // path — to answer "no" for the fourteen columns that are not this one. The ancestor chain is a
    // dozen typed casts, and a cell with no stop at all has no flags to miss: the widget always
    // parents its first badge into the row, so no badge means no flags.
    private static VisualElement FindExpandingList(VisualElement cell, VisualElement badge)
    {
        if (cell == null || badge == null) return null;
        var current = badge;
        for (int i = 0; i < 20 && current != null; i++)
        {
            try
            {
                if (current.TryCast<SI.Bindable.ExpandingList>() != null) return current;
                if (current.Pointer == cell.Pointer) return null;
                current = current.hierarchy.parent;
            }
            catch { return null; }
        }
        return null;
    }

    private static bool HasText(VisualElement element, int depth)
    {
        if (element == null || depth > 10) return false;
        try
        {
            if (element.GetIl2CppType().Name == "SIText"
                && !string.IsNullOrEmpty(element.Cast<SI.Bindable.SIText>().text))
                return true;
            int count = element.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                if (HasText(element.hierarchy[i], depth + 1)) return true;
        }
        catch { return false; }
        return false;
    }
}

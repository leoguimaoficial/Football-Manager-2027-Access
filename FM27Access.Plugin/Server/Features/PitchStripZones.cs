using System;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Names the six strips of a Data Hub pitch zone map (<c>ZoneMapCard</c>, e.g. Team Analytics'
/// "Passes attempted"). The game draws six columns across the pitch, the direction of play left to
/// right, each holding only a percentage, so the table could only number them "Zone 1..6".
///
/// Each strip's binding carries a <c>zone</c> record whose <c>PitchZoneSegment</c> (0 to 5) is its
/// place along the pitch, own goal end first. Verified 2026-10-04 against the card's own summary
/// ("We made the most passes just after the halfway line" sits on segment 3, the busiest strip) and
/// the drawing (the halfway line falls between segments 2 and 3). Three strips per half, named by
/// their place within the half rather than by distances the drawing does not state.
///
/// Named only on a six-strip map; any other map, or an unreadable segment, returns null so the
/// table keeps its numbering (all-or-nothing, see SyntheticTables.Spec.RowLabelFrom). Main thread only.
/// </summary>
internal static class PitchStripZones
{
    private const string ZoneNode = "zone";
    private const string SegmentField = "PitchZoneSegment";
    private const string StripName = "zone frame";
    private const int Strips = 6;

    // Mod-authored: each strip by its half and its place in it.
    private static readonly string[] Names =
    {
        "Own half, deepest",
        "Own half, middle",
        "Own half, nearest halfway",
        "Opposition half, nearest halfway",
        "Opposition half, middle",
        "Opposition half, deepest",
    };

    internal static string NameOf(VisualElement strip)
    {
        try
        {
            if (CountStrips(strip.hierarchy.parent) != Strips) return null;
            for (var p = strip; p != null; p = p.hierarchy.parent)
            {
                var node = BindingRecords.NodeOf(p);
                if (node == null) continue;
                var zone = BindingRecords.FindNode(node, ZoneNode, 0);
                if (zone == null) return null;
                var rec = BindingRecords.RecordOf(BindingRecords.ValueOf(zone));
                if (rec == null) return null;
                double? segment = BindingRecords.NumberOf(BindingRecords.Field(BindingRecords.Fields(rec), SegmentField));
                return segment is >= 0 and < Strips && segment == Math.Floor(segment.Value) ? Names[(int)segment.Value] : null;
            }
            return null;
        }
        catch (Exception e)
        {
            Degrade.Note($"pitch strip zones: segment read failed: {e.GetType().Name}");
            return null;
        }
    }

    // The strips are drawn as siblings; a map with any other count is not the six-strip layout the
    // names describe. The strip's own wrapper may sit between it and its siblings, so count at the
    // first level holding more than one strip.
    private static int CountStrips(VisualElement parent)
    {
        for (int up = 0; up < 4 && parent != null; up++, parent = parent.hierarchy.parent)
        {
            int count = 0;
            int n = parent.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                if (HasStrip(parent.hierarchy[i], 3)) count++;
            if (count > 1) return count;
        }
        return 0;
    }

    private static bool HasStrip(VisualElement el, int depth)
    {
        if (el.name == StripName) return true;
        if (depth == 0) return false;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (HasStrip(el.hierarchy[i], depth - 1)) return true;
        return false;
    }
}

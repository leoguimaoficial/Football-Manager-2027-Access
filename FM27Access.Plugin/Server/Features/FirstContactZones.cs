using System;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Names the three cells of a set-piece First Contact zone map (the "Set Piece First Contacts -
/// Own Box / Opposition Box" analysis cards). The game draws them as a bare strip of percentages
/// with no text, so the mod could only number them "Zone 1..3", which says nothing about where
/// across the goalmouth a team wins the ball.
///
/// Each cell binds a <c>PitchZoneSegment</c> id — a stable identifier, unlike the draw position —
/// and the game's localisation table describes the map as covering "near post / far post / central
/// (between posts)". The id-to-zone mapping below was read off the cards' own Summary prose, which
/// names both the busiest zone ("it happens mostly in the far post area") and a frequency tier per
/// zone ("rarely / sometimes / often ... in the near post area"), across four cards:
///
///   Opposition Box, our club   0% / 6% / 22%    "mostly far post";  near rarely, centre rarely, far sometimes
///   Own Box, our club          81% / 81% / 45%  "mostly centre";    near often,  centre often,  far sometimes
///   Opposition Box, opponent   10% / 14% / 0%   "mostly centre";    all three rarely
///   Own Box, opponent          96% / 100% / 81% "mostly centre";    all three often
///
/// The first two are individually decisive — the named busiest zone and every frequency tier line
/// up with exactly one assignment — and the other two agree. Note the Summary's PHASE words are
/// unreliable (a card headed "Own Box" describes its own rows as "attacking"); only its zone words
/// were used.
///
/// Unknown ids resolve to null so the caller falls back to numbering: a renumbered template must
/// degrade to "Zone 1" rather than confidently name the wrong side of the goal. Main thread only.
/// </summary>
internal static class FirstContactZones
{
    // The binding child holding a cell's zone reference, and the id property on it.
    private const string ZoneChild = "zone";
    private const string SegmentProperty = "PitchZoneSegment";

    // Segment id -> zone. The own-box map and the opposition-box map carry distinct id runs for the
    // same three places.
    private const string NearPost = "Near Post";
    private const string Centre = "Centre";
    private const string FarPost = "Far Post";

    private static string ZoneFor(int segment) => segment switch
    {
        97 or 100 => NearPost,
        98 or 101 => Centre,
        99 or 102 => FarPost,
        _ => null,
    };

    /// <summary>
    /// The zone name for one <c>zone frame</c> cell, or null when its id is missing or unrecognised.
    /// </summary>
    internal static string NameOf(VisualElement zoneFrame)
    {
        try
        {
            var root = FindBindingRoot(zoneFrame);
            if (root == null) return null;

            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null) return null;

            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) return null;

            foreach (var child in node.Children)
            {
                if (!string.Equals(child.Name, ZoneChild, StringComparison.Ordinal)) continue;
                var childKey = child.Key;
                var zone = bindings.Get(ref childKey)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                if (zone == null) return null;
                var raw = ValueOf(zone, SegmentProperty);
                return raw != null && int.TryParse(raw.AsString(), out int segment) ? ZoneFor(segment) : null;
            }
            return null;
        }
        catch (Exception e)
        {
            Degrade.Note($"first contact zones: segment read failed: {e.GetType().Name}");
            return null;
        }
    }

    private static SI.Core.TypedValue ValueOf(SI.Bindable.DynamicReference reference, string name)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var keys = reference.Keys.GetEnumerator();
        while (keys.MoveNext())
        {
            var key = keys.Current;
            string keyName;
            try { keyName = reg.GetInfo((uint)key).Name; }
            catch { continue; }
            if (string.Equals(keyName, name, StringComparison.Ordinal)) return reference[key];
        }
        return null;
    }

    // The cell's binding root sits above it (the streamed row wrapper), not inside.
    private static SI.Bindable.BindingRoot FindBindingRoot(VisualElement el)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
        {
            var root = p.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
        }
        return null;
    }
}

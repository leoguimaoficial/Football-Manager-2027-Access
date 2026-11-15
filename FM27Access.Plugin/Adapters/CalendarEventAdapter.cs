using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// One event tile on the schedule calendar (a training session, a match, a fixture result). The tile is
/// an SIButton with no bound click command (verified live: <c>m_clickableAction</c> is null) — it is
/// only a container for the event's texts and icons. A completed match/result, however, carries the same
/// shape as a table player name: its competition/opponent/score texts are SIText embedded hyperlinks
/// (<c>&lt;link="0"&gt;</c>) whose link opens the match report. So activating finds the link-bearing
/// SIText inside the tile and fires its link through the shared <see cref="SITextLink"/> path — the data
/// (the link id from the markup) drives it, not a screen position. Training sessions, upcoming "Match"
/// placeholders, and milestones carry no link and no command, so there is nothing to activate; that is
/// recorded as an honest gap rather than a silent no-op. The SIButton stays the backing element so the
/// label/state still come from ButtonAdapter (it collects the tile texts: "Training", "Friendly, 0-7, …").
/// </summary>
internal sealed class CalendarEventAdapter : ButtonAdapter
{
    public CalendarEventAdapter(VisualElement element) : base(element) { }

    // FM27's compact calendar template draws a session icon without its text.
    // The surrounding calendar event still binds the official localised name.
    // Restrict this to that icon-only template so full event tiles keep their
    // existing text/icon reading and unrelated images cannot borrow a session.
    internal static string CompactEventLabel(VisualElement image)
    {
        var up = FindCompactEvent(image, out bool namedCompactLayout);
        if (up == null) return null;
        // Stay on this event's nearest binding root; searching a whole day
        // could borrow another event's player, opponent or training session.
        var root = up.hierarchy.parent;
        for (int j = 0; j < 4 && root != null; j++, root = root.hierarchy.parent)
            if (root.GetIl2CppType().Name == "BindingRoot") break;
        var node = root == null ? null : Server.BindingRecords.NodeOf(root);
        var eventNode = node == null ? null : Server.BindingRecords.FindNode(node, "Event", 3);
        var record = eventNode == null ? null : Server.BindingRecords.RecordOf(Server.BindingRecords.ValueOf(eventNode));
        if (record == null) return null;
        var fields = Server.BindingRecords.Fields(record);
        // FM27 also draws this aggregate in unnamed component templates. Its
        // own three-category record identifies it; tile size/name does not.
        bool trainingSummary = Server.BindingRecords.NumberOf(
            Server.BindingRecords.Field(fields, "SessionType1")) != null;
        if (!namedCompactLayout && !trainingSummary) return null;
        if (image.name == "PersonImage")
        {
            var player = Server.BindingRecords.Field(fields, "Player");
            if (Server.BindingRecords.PersonOf(player) == null)
                return Speech.Degrade.Read("calendar portrait has no bound Player reference");
            return Server.PlayerNameRequests.NameForEntity(eventNode.Key, player, image) ?? Speech.Degrade.Unreadable;
        }
        if (image.name == "CompetitionIcon")
            return Server.PlayerNameRequests.NameForEntity(eventNode.Key,
                Server.BindingRecords.Field(fields, "Competition"), image);
        // The aggregate tile's three glyphs show categories, not three session
        // captions. Non-training events retain their own reference/name path.
        if (trainingSummary)
        {
            var sprite = SpriteHelper.ReadSpriteName(image);
            if (sprite == null) return null;
            if (sprite.StartsWith("training physical_", System.StringComparison.OrdinalIgnoreCase)) return "Physical training";
            if (sprite.StartsWith("training general_", System.StringComparison.OrdinalIgnoreCase)) return "General training";
            // The aggregate has no SessionName; this observed native category
            // glyph carries real information, just like the existing families.
            if (sprite.StartsWith("training set pieces_", System.StringComparison.OrdinalIgnoreCase)) return "Set-piece training";
            if (sprite.StartsWith("abroad-plane_", System.StringComparison.OrdinalIgnoreCase)) return "Travel";
            if (sprite.StartsWith("in-possession_", System.StringComparison.OrdinalIgnoreCase)) return "In-possession training";
            if (sprite.StartsWith("out-of-possession_", System.StringComparison.OrdinalIgnoreCase)) return "Out-of-possession training";
            if (sprite == "miscellaneous") return "Miscellaneous training";
            return null; // Rest and other established assets retain SpriteHelper's mapping.
        }
        var name = Server.BindingRecords.TextOf(Server.BindingRecords.Field(fields, "SessionName"));
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (SpriteHelper.ReadSpriteName(image)?.StartsWith("national_", System.StringComparison.OrdinalIgnoreCase) == true)
            return "Internationals";
        var opponent = Server.BindingRecords.Field(fields, "TeamOpponent");
        if (Server.BindingRecords.TeamOf(opponent) == null) return null;
        name = Server.PlayerNameRequests.NameForEntity(eventNode.Key, opponent, image);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var venue = Server.BindingRecords.TextOf(Server.BindingRecords.Field(fields, "MatchVenue"));
        return string.IsNullOrWhiteSpace(venue) ? name : name + ", " + venue;
    }

    internal static bool IsCompactPortrait(VisualElement image)
        => image?.name == "PersonImage" && FindCompactEvent(image, out _) != null;

    private static VisualElement FindCompactEvent(VisualElement image, out bool namedCompactLayout)
    {
        namedCompactLayout = false;
        var up = image.hierarchy.parent;
        for (int i = 0; i < 20 && up != null; i++, up = up.hierarchy.parent)
        {
            namedCompactLayout |= up.name is "4x4Tile" or "8x4Tile";
            if (up.name != "CalendarEventDynamicItem") continue;
            // Only the category Icon is a candidate outside legacy named
            // layouts. CompactEventLabel still requires its actual summary
            // record, so ordinary full-size pictures keep their old behavior.
            return namedCompactLayout || image.name == "Icon" ? up : null;
        }
        return null;
    }

    public override void Activate()
    {
        var link = FindLinkText(Element);
        if (link != null && SITextLink.Activate(link)) return;
        if (HasBoundCommand(Element)) { base.Activate(); return; }
        if (HasContextMenu && ContextMenuOpener.Send(Element, out var error)) return;
        throw new System.InvalidOperationException("The calendar event has no readable native action.");
    }

    internal static bool HasBoundCommand(VisualElement element)
        => NativeButtonCommand.HasBoundCommand(element);

    // The first SIText descendant whose rich-text carries a <link=> tag (the competition/opponent/score
    // of a played match all share link id "0", so any one opens the report). Null for link-less tiles
    // (Training, upcoming "Match", milestones).
    private static VisualElement FindLinkText(VisualElement root)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIText" && SITextLink.HasLink(child.Cast<TextElement>().text))
                return child;
            var found = FindLinkText(child);
            if (found != null) return found;
        }
        return null;
    }
}

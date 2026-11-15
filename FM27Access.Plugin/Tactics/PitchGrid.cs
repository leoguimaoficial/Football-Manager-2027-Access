using System.Collections.Generic;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Tactics;

/// <summary>
/// The live VisualElement read that produces the game-free <see cref="PitchFormation"/> both
/// surfaces consume. Walks a pitch container's <c>FMPitchIndicator</c> slots and reads each
/// occupied one (slot, player name, role, shirt number) forward into the model — the 24-slot
/// geometry and row labels live once, in <see cref="PitchNavigation"/>. No state is cached:
/// occupancy is re-read from the tree on every call, per the Golden Rule.
/// </summary>
internal static class PitchGrid
{
    /// <summary>
    /// Read every occupied slot under <paramref name="pitch"/> into a <see cref="PitchFormation"/>,
    /// the shared model for speech and HTML. Works in any context that lays players out on
    /// FMPitchIndicator slots — the editable tactics pitch and the read-only advice cards alike —
    /// because it reads the slot structure, not the editable shirt button. Returns an empty
    /// formation when <paramref name="pitch"/> is null or holds no occupied slot.
    /// </summary>
    internal static PitchFormation ReadFormation(VisualElement pitch)
    {
        var players = new List<PitchPlayer>();
        if (pitch != null) CollectPlayers(pitch, players, new PitchNameLookup(pitch));
        return PitchFormation.FromPlayers(players);
    }

    // The FMTacticsPanel family that lays players out on the 24 named FMPitchIndicator slots:
    // FMTacticsWithAnalysis (the editable In/Out-of-Possession pitch) and its base FMTacticsPanel
    // (the read-only Combined summary and the advice/report cards). Detected by widget type, not by
    // element name, so it holds in every context. PitchDrawing's continuous-coordinate pitches (pass
    // map, live match) are NOT this family and are deliberately excluded.
    internal static bool IsPitchContainer(VisualElement el)
        => IsPitchContainer(el, el.GetIl2CppType().Name);

    // Overload for callers that have already resolved the IL2CPP type name, to avoid a second marshal.
    internal static bool IsPitchContainer(VisualElement el, string typeName)
        => typeName == "FMTacticsPanel" || typeName == "FMTacticsWithAnalysis";

    // The editable pitch is the only one whose drag-drop and per-player action buttons actually do
    // anything; every other FMTacticsPanel is a read-only presentation. The concrete subclass is the
    // honest signal (FMTacticsWithAnalysis is the interactable widget), so actions are offered only
    // where they work rather than wherever a shirt button happens to render.
    internal static bool IsEditablePitch(VisualElement el)
        => el.GetIl2CppType().Name == "FMTacticsWithAnalysis";

    // True when a pitch container sits somewhere under <paramref name="el"/>. The suggested-lineup
    // card wraps its whole pitch in one SIButton; the HTML walk would otherwise collapse that button
    // into a single opaque blob, so it uses this to descend through the wrapper and reach the pitch.
    // Bounded by a node budget (the wrapper is shallow) so it stays cheap to run per candidate button.
    internal static bool SubtreeHasPitchContainer(VisualElement el, int maxNodes = 800)
    {
        var st = new Stack<VisualElement>();
        st.Push(el);
        int budget = 0;
        while (st.Count > 0 && budget++ < maxNodes)
        {
            var e = st.Pop();
            if (e.Pointer != el.Pointer && IsPitchContainer(e)) return true;
            int c = e.hierarchy.childCount;
            for (int i = 0; i < c; i++) st.Push(e.hierarchy[i]);
        }
        return false;
    }

    private static void CollectPlayers(VisualElement el, List<PitchPlayer> players, PitchNameLookup lookup)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "FMPitchIndicator")
        {
            // An occupied slot carries a player name or a shirt number; empty slots have neither
            // (their indicator holds no shirt subtree at all) and are skipped so the formation holds
            // only the eleven (or fewer) filled positions. On numbers-only pitches (the collapsed
            // Team Selection pitch, the Squad Overview Tactics tile) the shirt-name text is blank;
            // the occupant is then resolved from that shirt's actual Player reference,
            // with the inherited same-PlayerIndex squad-row lookup as a fallback.
            // PlayerNameText carries rich-text markup (<color>/<link>/<style>) that
            // TextElement.text does not strip, so the fields are cleaned here — the model's
            // contract is already-plain text.
            var name = ReadPlayerName(el);
            var number = ReadNumber(el);
            if (name == null && number != null)
                name = lookup.NameFor(el);
            if (name != null || number != null)
                players.Add(new PitchPlayer(el.name, Clean(ReadRole(el)), Clean(number), Clean(name)));
            else if (HasPositionOnlyMarker(el))
                players.Add(new PitchPlayer(el.name, null, null, null, positionOnly: true));
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectPlayers(el.hierarchy[i], players, lookup);
    }

    // FM27's expected-formation diagrams draw anonymous positions rather than players.
    // Empty indicators also exist for every unused slot, so the indicator itself is
    // insufficient: require the visible native shape template and its own one-field
    // Position record. Do not infer occupancy from geometry, colour or team lineups.
    private static bool HasPositionOnlyMarker(VisualElement indicator)
    {
        foreach (string name in new[] { "any-player", "goal-keeper" })
        {
            var marker = FindByName(indicator, name);
            if (marker == null || !ElementBridge.IsVisibleInHierarchy(marker)) continue;
            var record = Server.BindingRecords.RecordAbove(marker, "Position", ancestorBudget: 4, depth: 0);
            if (record == null) continue;
            var fields = Server.BindingRecords.Fields(record);
            if (fields.Count == 1 && fields.ContainsKey("Position")) return true;
        }
        return false;
    }

    /// <summary>slot name → occupant player name (rich-text stripped). Empty slots are absent.</summary>
    internal static Dictionary<string, string> ReadOccupants(VisualElement pitch)
    {
        var result = new Dictionary<string, string>();
        foreach (var player in ReadFormation(pitch).Players)
            if (!string.IsNullOrEmpty(player.Name)) result[player.Slot] = player.Name;
        return result;
    }

    // The named-overview slot template (the Opposition Report's Expected Tactic pitch): a portrait
    // cell followed by two frames holding the player's name and position. Its texts carry no
    // distinguishing name of their own, so the frame is the anchor.
    private const string NamedOverviewSlot = "data_display-names-overview-pitch-indicator-large frame";

    // The occupant's name: PlayerNameText on the full-size pitch, fm-tactics-shirt-name on compact
    // shirt templates (present there but legitimately blank on numbers-only tiles), and the second
    // frame of the named-overview slot on the scouting pitches that use neither.
    private static string ReadPlayerName(VisualElement indicator)
    {
        var name = TextOf(FindByName(indicator, "PlayerNameText"));
        if (name != null) return name;
        name = TextOf(FindByName(indicator, "fm-tactics-shirt-name"));
        return name ?? NamedOverviewText(indicator, 0) ?? LinkedPlayerName(indicator);
    }

    // FM27's compact overview/TOTW template leaves the shirt-name blank and
    // renders the player as a sibling link below it. Read that visible native
    // text rather than resolving a number against the human team's squad.
    private static string LinkedPlayerName(VisualElement element)
    {
        if (!ElementBridge.IsVisible(element)) return null;
        var text = element.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text) && Adapters.SITextLink.HasLink(text)) return text;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var name = LinkedPlayerName(element.hierarchy[i]);
            if (name != null) return name;
        }
        return null;
    }

    // The nth text of the named-overview slot's non-portrait frames: 0 is the player, 1 the
    // position. Null when this slot is not that template, or the frame is absent.
    private static string NamedOverviewText(VisualElement indicator, int index)
    {
        var slot = FindByName(indicator, NamedOverviewSlot);
        if (slot == null) return null;
        var texts = new List<string>();
        CollectSlotTexts(slot, texts, index + 1);
        return index < texts.Count ? texts[index] : null;
    }

    private static void CollectSlotTexts(VisualElement el, List<string> texts, int max)
    {
        if (texts.Count >= max) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var t = el.TryCast<TextElement>()?.text;
            if (!string.IsNullOrEmpty(t)) texts.Add(t);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectSlotTexts(el.hierarchy[i], texts, max);
    }

    private static string TextOf(VisualElement el)
    {
        var textEl = el?.TryCast<TextElement>();
        return textEl != null && !string.IsNullOrEmpty(textEl.text) ? textEl.text : null;
    }

    // The role badge: the RoleButton's "text" leaf (e.g. "CFD"). Null when the slot has no role
    // shown (a read-only card that omits roles), which Describe folds out rather than degrading.
    private static string ReadRole(VisualElement indicator)
    {
        var roleButton = FindByName(indicator, "RoleButton");
        if (roleButton == null) return NamedOverviewText(indicator, 1);
        var roleText = FindByName(roleButton, "text");
        var textEl = roleText?.TryCast<TextElement>();
        return textEl != null && !string.IsNullOrEmpty(textEl.text) ? textEl.text : null;
    }

    // The shirt number leaf. Null when the slot shows no number.
    private static string ReadNumber(VisualElement indicator)
    {
        var numberEl = FindByName(indicator, "fm-tactics-shirt-number");
        var textEl = numberEl?.TryCast<TextElement>() ?? KitNumberText(indicator);
        return textEl != null && !string.IsNullOrEmpty(textEl.text) ? textEl.text : null;
    }

    // FM27's read-only kit template has one anonymous number leaf rather than
    // FMTacticsShirt. Its own view scope also identifies the exact player.
    internal static TextElement KitNumberText(VisualElement indicator)
    {
        var kit = FindByName(indicator, "data_display-tactics-kit-squad-outfield");
        if (kit == null) return null;
        var texts = new List<TextElement>();
        CollectKitTexts(kit, texts);
        return texts.Count == 1 && int.TryParse(texts[0].text, out _) ? texts[0] : null;
    }

    private static void CollectKitTexts(VisualElement element, List<TextElement> texts)
    {
        if (!ElementBridge.IsVisible(element)) return;
        var text = element.TryCast<TextElement>();
        if (text != null && !string.IsNullOrWhiteSpace(text.text)) texts.Add(text);
        for (int i = 0; i < element.hierarchy.childCount; i++)
            CollectKitTexts(element.hierarchy[i], texts);
    }

    private static string Clean(string s)
        => string.IsNullOrEmpty(s) ? s : FM27Access.Speech.TextFilters.CleanForDisplay(s);

    internal static VisualElement FindByName(VisualElement el, string name)
    {
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindByName(el.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }
}

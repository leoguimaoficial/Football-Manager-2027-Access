using System;
using System.Collections.Generic;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Mentoring card's relationship diagram, read into a semantic table.
///
/// Each mentoring group draws its players as nodes and the influence between every ORDERED pair as
/// an arrow. The arrow's strength is carried entirely by its line style — the graph key beside it
/// reads None / Light / Average / Significant — so a text walk finds the arrows to be empty
/// containers and the whole point of the screen (who is actually learning from whom, and how
/// strongly) reaches a blind player as nothing at all. The names and personalities walk out as a
/// loose run of paragraphs beside it, with the key's four words orphaned above them.
///
/// The strength IS readable, just not from the drawn line: each arrow's binding context sets the
/// pair (<c>mentoringgroupplayeroneid</c> influencing <c>mentoringgroupplayertwoid</c>) and exposes
/// the game's own <c>MentoringGroupRelationshipScore</c> for it, on the same 0-3 scale the graph
/// key's variables node maps to its four labels. This reads both and renders one matrix: a row per
/// player, their personality, and their influence on each of the others.
///
/// HTML-only, like the chart and match-stats consolidations: the speech surface keeps the raw
/// stops. Read live per render, never cached. Decline in place — a group whose players cannot be
/// named and paired keeps its raw stops rather than showing a half-built matrix — and degrade in
/// place for a single unreadable score, which becomes an honest marker in its own cell.
/// </summary>
internal static class MentoringDiagram
{
    internal const string DiagramName = "MentoringRelationshipDiagram";
    internal const string GraphKeyName = "frame-graph-key";

    // The binding child that holds one ordered pair's influence, in the arrow's own remapped
    // context, and the path to it from the arrow's remapper node.
    private static readonly string[] ScorePath = { "Team", "TeamTraining", "MentoringGroupRelationshipScore" };

    private const string PersonalityHeader = "Personality";
    private const string PlayerHeader = "Player";

    // Mod chrome for the per-opponent columns: "Influence on <player>". The bare name alone would
    // not say which way the influence runs, and the direction is the whole content of the diagram.
    private const string InfluenceOnPrefix = "Influence on ";

    private sealed class Player
    {
        public int Id;
        public string Name;
        public string Personality;
        public FM27Access.Navigation.IAccessibleElement Action;
    }

    /// <summary>One ordered pair: <see cref="FromId"/>'s influence on <see cref="ToId"/>.</summary>
    private sealed class Arrow
    {
        public int FromId;
        public int ToId;
        public int Score = -1;   // -1 = unreadable
    }

    /// <summary>
    /// True when el is a group's relationship diagram and a table was emitted in its place. False
    /// leaves the raw walk untouched.
    /// </summary>
    internal static bool TryBuild(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        if (el.name != DiagramName) return false;
        try
        {
            var table = TryReadTable(el);
            if (table == null) return false;
            items.Add(table);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MentoringDiagram] read failed, raw stops kept: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// True when el is the graph key of a diagram this reader will table, so the walk can drop the
    /// key's four orphan words. A diagram it cannot read keeps its key: the legend is useless on
    /// its own, but so is silently removing it while the arrows stay unreadable.
    /// </summary>
    internal static bool IsRedundantGraphKey(VisualElement el)
    {
        if (el.name != GraphKeyName) return false;
        try
        {
            var diagram = FindNearbyDiagram(el);
            return diagram != null && TryReadTable(diagram) != null;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MentoringDiagram] graph-key check failed, key kept: {ex.Message}");
            return false;
        }
    }

    // The key and the diagram sit in sibling subtrees of the same block, so search from the key's
    // nearest ancestor that holds both.
    private static VisualElement FindNearbyDiagram(VisualElement key)
    {
        var current = key.hierarchy.parent;
        for (int i = 0; current != null && i < 4; i++)
        {
            var found = FindDescendant(current, DiagramName, 0, 8);
            if (found != null) return found;
            current = current.hierarchy.parent;
        }
        return null;
    }

    private static ScreenWalker.SyntheticTableItem TryReadTable(VisualElement diagram)
    {
        var players = ReadPlayers(diagram);
        // Two players is the smallest group the game builds; one node is a half-loaded template.
        if (players.Count < 2) return null;

        var arrows = new List<Arrow>();
        CollectArrows(diagram, arrows, 0);
        if (arrows.Count == 0) return null;

        var levels = ReadLevelLabels(diagram);

        // Rows are only trustworthy if every arrow's pair resolves to players in this group: a
        // partially resolved matrix would silently drop a relationship the diagram is drawing.
        var byId = new Dictionary<int, Player>();
        foreach (var p in players) byId[p.Id] = p;
        foreach (var a in arrows)
            if (!byId.ContainsKey(a.FromId) || !byId.ContainsKey(a.ToId))
            {
                Degrade.Note($"[MentoringDiagram] arrow {a.FromId}->{a.ToId} is not between this group's players; raw stops kept");
                return null;
            }

        var headers = new List<string>(players.Count + 2) { PlayerHeader, PersonalityHeader };
        foreach (var p in players) headers.Add(InfluenceOnPrefix + p.Name);

        var rows = new List<List<string>>(players.Count);
        var actions = new List<FM27Access.Navigation.IAccessibleElement>(players.Count);
        foreach (var from in players)
        {
            var row = new List<string>(headers.Count) { from.Name, from.Personality };
            foreach (var to in players)
            {
                if (to.Id == from.Id) { row.Add(""); continue; }
                var arrow = arrows.Find(a => a.FromId == from.Id && a.ToId == to.Id);
                if (arrow == null) { row.Add(""); continue; }
                row.Add(LabelFor(arrow.Score, levels));
            }
            rows.Add(row);
            actions.Add(from.Action);
        }
        return new ScreenWalker.SyntheticTableItem { Headers = headers.ToArray(), Rows = rows, RowActions = actions };
    }

    // The graph key's own label for a score. An unreadable score, or one the key does not name,
    // degrades to a marker: a blank cell would read as "no influence", which is a different fact.
    private static string LabelFor(int score, Dictionary<int, string> levels)
    {
        if (score >= 0 && levels.TryGetValue(score, out var label)) return label;
        Degrade.Note($"[MentoringDiagram] relationship score {score} has no label in the graph key");
        return Degrade.Unreadable;
    }

    // Each Player* slot: the drawn name and personality, plus the person id its binding carries,
    // which is what the arrows pair on. A slot missing any of the three is dropped, and the caller
    // declines once too few remain — a matrix missing a player is worse than the loose lines.
    private static List<Player> ReadPlayers(VisualElement diagram)
    {
        var slots = new List<VisualElement>();
        CollectSlots(diagram, slots, 0);

        var players = new List<Player>(slots.Count);
        foreach (var slot in slots)
        {
            int id = ReadPersonId(slot);
            if (id == 0) continue;

            // FM27 nests its player-name link more deeply than the original
            // text walk reaches; personality remains shallow. Resolve the
            // actual entity-name component before interpreting the other text,
            // so personality can never become a player's identity.
            var nameElement = ElementBridge.FindNameDescendant(slot);
            string name = nameElement == null ? null : CleanOrNull(ScreenWalker.ReadFirstText(nameElement));
            var texts = new List<string>();
            CollectTexts(slot, texts, 0, nameElement);
            // Name then personality, the order the player cell draws them; a slot for a player the
            // game will not show a personality for draws only the name.
            if (name == null && texts.Count < 2) continue;
            FM27Access.Navigation.IAccessibleElement action = null;
            if (nameElement != null)
            {
                var parts = Adapters.SITextLink.ReadParts(nameElement);
                if (parts != null)
                    foreach (var part in parts)
                        if (part.CanActivate && CleanOrNull(part.GetLabel()) == name)
                        { action = part; break; }
                // Do not freeze an unresolved native profile link as plain
                // text. The original walk retains it until its data binds.
                if (parts == null && Adapters.SITextLink.HasLink(nameElement.TryCast<TextElement>()?.text))
                    continue;
            }
            players.Add(new Player
            {
                Id = id,
                Name = name ?? texts[0],
                Personality = name != null ? (texts.Count > 0 ? texts[0] : Degrade.Unavailable)
                    : texts[1],
                Action = action,
            });
        }
        return players;
    }

    // The player nodes of the diagram, in draw order. Named PlayerOne..PlayerN by the template —
    // but so are the layout frames that GROUP them ("PlayerTwoAndThree"), so only a Player* element
    // holding no further Player* element is a slot. Taking the grouping frame instead silently
    // merges two players into one row.
    private static void CollectSlots(VisualElement el, List<VisualElement> slots, int depth)
    {
        if (depth > 8) return;
        string name = el.name ?? "";
        if (IsPlayerName(name) && !HasPlayerDescendant(el, 0))
        {
            slots.Add(el);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectSlots(el.hierarchy[i], slots, depth + 1);
    }

    private static bool IsPlayerName(string name)
        => name.StartsWith("Player", StringComparison.Ordinal) && name != "PlayerImage";

    private static bool HasPlayerDescendant(VisualElement el, int depth)
    {
        if (depth > 8) return false;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (IsPlayerName(child.name ?? "")) return true;
            if (HasPlayerDescendant(child, depth + 1)) return true;
        }
        return false;
    }

    private static void CollectArrows(VisualElement el, List<Arrow> arrows, int depth)
    {
        if (depth > 8) return;
        if ((el.name ?? "").StartsWith("RelationshipArrow", StringComparison.Ordinal))
        {
            if (!ElementBridge.IsVisible(el)) return;
            var arrow = ReadArrow(el);
            if (arrow != null) arrows.Add(arrow);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectArrows(el.hierarchy[i], arrows, depth + 1);
    }

    // The arrow's innermost remapper holds the pair it draws and, through the team's training
    // context, the game's score for exactly that pair.
    private static Arrow ReadArrow(VisualElement arrowRoot)
    {
        var remapper = FindRemapper(arrowRoot, 0);
        if (remapper == null) return null;
        var key = remapper.Key;
        if (!TryGetNode(ref key, out var node)) return null;

        int from = 0, to = 0;
        foreach (var child in node.Children)
        {
            if (child.Name == "mentoringgroupplayeroneid") from = ReadInt(child.Key);
            else if (child.Name == "mentoringgroupplayertwoid") to = ReadInt(child.Key);
        }
        if (from == 0 || to == 0) return null;

        return new Arrow { FromId = from, ToId = to, Score = ReadScore(node) };
    }

    // MentoringGroupRelationshipScore, reached through Team > TeamTraining in the arrow's own
    // remapped context. -1 when any step is missing: the caller turns that into a marker cell.
    private static int ReadScore(SI.Bindable.IReadOnlyNode node)
    {
        var current = node;
        for (int i = 0; i < ScorePath.Length; i++)
        {
            SI.Bindable.IReadOnlyNode next = null;
            foreach (var child in current.Children)
                if (child.Name == ScorePath[i]) { next = child; break; }
            if (next == null) return -1;
            if (i == ScorePath.Length - 1)
            {
                var leafKey = next.Key;
                return ReadIntOrMissing(leafKey);
            }
            var childKey = next.Key;
            if (!TryGetNode(ref childKey, out current)) return -1;
        }
        return -1;
    }

    // The person id under a player slot: the "Person" binding's UniqueId, wherever in the slot's
    // nested remappers it resolves.
    private static int ReadPersonId(VisualElement slot)
    {
        var remappers = new List<SI.Bindable.BindingRemapper>();
        CollectRemappers(slot, remappers, 0);
        foreach (var remapper in remappers)
        {
            var key = remapper.Key;
            if (!TryGetNode(ref key, out var node)) continue;
            int id = FindUniqueIdUnderPerson(node, 0);
            if (id != 0) return id;
        }
        return 0;
    }

    private static int FindUniqueIdUnderPerson(SI.Bindable.IReadOnlyNode node, int depth)
    {
        if (depth > 3) return 0;
        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            if (child.Name == "Person")
            {
                if (!TryGetNode(ref childKey, out var person)) continue;
                foreach (var grand in person.Children)
                    if (grand.Name == "UniqueId") return ReadInt(grand.Key);
                continue;
            }
            if (!TryGetNode(ref childKey, out var sub)) continue;
            int found = FindUniqueIdUnderPerson(sub, depth + 1);
            if (found != 0) return found;
        }
        return 0;
    }

    // The graph key's four levels: its variables node gives each level's number, its matching
    // container gives the drawn label. The game's own words, so the table speaks the same
    // vocabulary as the screen and needs no mod-authored scale.
    private static Dictionary<int, string> ReadLevelLabels(VisualElement diagram)
    {
        var labels = new Dictionary<int, string>();
        var key = FindNearbyDiagramKey(diagram);
        if (key == null) return labels;

        var vars = FindVariables(key, 0);
        if (vars == null) return labels;
        var varsKey = vars.Key;
        if (!TryGetNode(ref varsKey, out var node)) return labels;

        foreach (var child in node.Children)
        {
            int level = ReadIntOrMissing(child.Key);
            if (level < 0) continue;
            // The variables are named for the key's own containers; the container holds the
            // drawn, localised word.
            var container = FindDescendantCaseInsensitive(key, child.Name, 0, 4);
            string text = container == null ? null : CleanOrNull(ScreenWalker.ReadFirstText(container));
            if (text != null) labels[level] = text;
        }
        return labels;
    }

    private static VisualElement FindNearbyDiagramKey(VisualElement diagram)
    {
        var current = diagram.hierarchy.parent;
        for (int i = 0; current != null && i < 6; i++)
        {
            var found = FindDescendant(current, GraphKeyName, 0, 6);
            if (found != null) return found;
            current = current.hierarchy.parent;
        }
        return null;
    }

    // --- element and binding helpers ---

    private static void CollectTexts(VisualElement el, List<string> texts, int depth, VisualElement excluded = null)
    {
        if (depth > 10 || texts.Count >= 4) return;
        if (excluded != null && el.Pointer == excluded.Pointer) return;
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ScreenWalker.ReadFirstText(el));
            if (t != null) texts.Add(t);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], texts, depth + 1, excluded);
    }

    private static string CleanOrNull(string s)
    {
        string cleaned = ServerText.Clean(s);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned.Trim();
    }

    private static SI.Bindable.BindingRemapper FindRemapper(VisualElement el, int depth)
    {
        if (depth > 8) return null;
        SI.Bindable.BindingRemapper deepest = null;
        var self = el.TryCast<SI.Bindable.BindingRemapper>();
        if (self != null) deepest = self;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindRemapper(el.hierarchy[i], depth + 1);
            if (found != null) deepest = found;
        }
        return deepest;
    }

    private static void CollectRemappers(VisualElement el, List<SI.Bindable.BindingRemapper> found, int depth)
    {
        if (depth > 8) return;
        var self = el.TryCast<SI.Bindable.BindingRemapper>();
        if (self != null) found.Add(self);
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectRemappers(el.hierarchy[i], found, depth + 1);
    }

    private static SI.Bindable.BindingVariables FindVariables(VisualElement el, int depth)
    {
        if (depth > 6) return null;
        var self = el.TryCast<SI.Bindable.BindingVariables>();
        if (self != null) return self;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindVariables(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindDescendant(VisualElement el, string name, int depth, int max)
    {
        if (depth > max) return null;
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendant(el.hierarchy[i], name, depth + 1, max);
            if (found != null) return found;
        }
        return null;
    }

    // The key's variable names and its container names agree only case-insensitively ("None" the
    // variable, "none" the container).
    private static VisualElement FindDescendantCaseInsensitive(VisualElement el, string name, int depth, int max)
    {
        if (depth > max) return null;
        if (string.Equals(el.name, name, StringComparison.OrdinalIgnoreCase)) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendantCaseInsensitive(el.hierarchy[i], name, depth + 1, max);
            if (found != null) return found;
        }
        return null;
    }

    private static bool TryGetNode(ref SI.Bindable.Bindings.Key key, out SI.Bindable.IReadOnlyNode node)
    {
        node = null;
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return false;
        return bindings.TryGetNode(ref key, out node);
    }

    private static int ReadInt(SI.Bindable.Bindings.Key key)
    {
        int value = ReadIntOrMissing(key);
        return value < 0 ? 0 : value;
    }

    private static int ReadIntOrMissing(SI.Bindable.Bindings.Key key)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return -1;
        var localKey = key;
        var value = bindings.Get(ref localKey);
        var numeric = value?.TryCast<SI.Core.NumericTypedValue>();
        if (numeric == null) return -1;
        return (int)numeric.Value;
    }
}

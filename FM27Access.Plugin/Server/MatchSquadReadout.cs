using System;
using System.Collections.Generic;
using System.Globalization;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// On-demand "top/bottom three of your on-pitch players" readouts for the HTML surface during a
/// match: Alt+1 ranks by match rating, Alt+2 by condition (single tap speaks the best three,
/// double tap the worst three). The values come from the in-match
/// "Your Squad" panel (<c>OverviewPrototype</c>): each player row is a <c>BindingRoot</c> whose
/// binding node materialises the live <c>MatchRating</c> and <c>Condition</c> the game's own row
/// widgets display, so the readout speaks exactly what a sighted player sees in the squad strip.
/// The rows are the current on-pitch XI; red-carded or subbed-off entries are excluded so the
/// ranking never names a player who is no longer on the pitch.
///
/// Ranking and phrasing live in <see cref="SquadReadout"/> (Navigation, unit-tested); this class
/// only gathers the live rows. Any row whose value cannot be read is skipped and spoken as an
/// honest trailing gap ("2 players unreadable" — an unread player could outrank everyone listed);
/// no readable rows at all degrades to "<metric> unavailable". Main thread only — every entry
/// point marshals via <see cref="MainThreadDispatcher"/>. Stateless: every call re-finds the panel
/// and re-reads current binding values, never caching game state.
/// </summary>
internal static class MatchSquadReadout
{
    /// <summary>Alt+1: the three best (single tap) or worst (double tap) match ratings among your on-pitch players.</summary>
    public static string DescribeRatings(SquadRank rank) => Describe(SquadMetric.Rating, rank);

    /// <summary>Alt+2: the three best (single tap) or worst (double tap) conditions among your on-pitch players.</summary>
    public static string DescribeCondition(SquadRank rank) => Describe(SquadMetric.Condition, rank);

    // A broken panel/binding read would otherwise fire the same degrade on every keypress; log a
    // given reason once until a different one appears. The marker is still spoken every call.
    private static string _lastDegradeReason;

    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    private static string MetricWord(SquadMetric metric) =>
        metric == SquadMetric.Rating ? "ratings" : "condition";

    private static string Describe(SquadMetric metric, SquadRank rank)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() => Compose(metric, rank));
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] {MetricWord(metric)} read failed: {ex.Message}");
            return MetricWord(metric) + " " + Speech.Degrade.Unavailable;
        }
    }

    private static string Compose(SquadMetric metric, SquadRank rank)
    {
        var rows = FindPlayerRows();
        if (rows == null)
        {
            NoteDegradeOnce("[MatchSquadReadout] squad panel (OverviewPrototype PlayersList) not found");
            return MetricWord(metric) + " " + Speech.Degrade.Unavailable;
        }

        var entries = new List<SquadReadout.Entry>();
        int unreadable = 0;
        foreach (var row in rows)
        {
            var read = ReadRow(row, metric);
            if (read.OffPitch) continue;
            if (read.Entry is { } entry) entries.Add(entry);
            else unreadable++;
        }

        var line = SquadReadout.Compose(entries, metric, rank, unreadable);
        if (line == null)
        {
            NoteDegradeOnce($"[MatchSquadReadout] no readable player rows ({unreadable} unreadable)");
            return MetricWord(metric) + " " + Speech.Degrade.Unavailable;
        }
        return line;
    }

    /// <summary>
    /// Alt+3: the body-language overview — how many on-pitch players sit in each of the game's
    /// five icon bands, most populous first, zero bands omitted ("body language: 6 neutral,
    /// 2 very positive, 2 negative, 1 positive"). The band is read from the row icon's sprite
    /// (asset family happiness=1/2/neutral/4/5) — the game's own five-way categorisation, the
    /// same read the row labels make every live-values tick. NOT from the binding's numeric
    /// InteractionBodyLanguageLevel: that value is on a different scale than the icon (verified
    /// live — level 5 "Composed" renders happiness=neutral, level 0 "Complacent" renders
    /// happiness=1), so mapping it ourselves would invent bands the game never showed. A row
    /// whose sprite is missing or unmapped counts as unreadable and is spoken as the trailing
    /// gap clause.
    /// </summary>
    public static string DescribeBodyLanguage()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ComposeBodyLanguage);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] body language read failed: {ex.Message}");
            return "body language " + Speech.Degrade.Unavailable;
        }
    }

    // Canonical band order, best to worst — the tie-break order ComposeBandCounts relies on.
    private static readonly string[] BandWords = { "very positive", "positive", "neutral", "negative", "very negative" };

    private static string ComposeBodyLanguage()
    {
        var rows = FindPlayerRows();
        if (rows == null)
        {
            NoteDegradeOnce("[MatchSquadReadout] squad panel (OverviewPrototype PlayersList) not found");
            return "body language " + Speech.Degrade.Unavailable;
        }

        var counts = new int[BandWords.Length];
        var nativeWords = new Dictionary<string, int>(StringComparer.Ordinal);
        int unreadable = 0;
        foreach (var row in rows)
        {
            var root = row.TryCast<SI.Bindable.BindingRoot>();
            if (root == null) { unreadable++; continue; }
            if (IsOffPitch(ReadBindingValues(root))) continue;

            var sprite = ReadRowBandSprite(row);
            int band = BandIndexFromSprite(sprite);
            if (band < 0)
            {
                // FM27's named vector families do not encode FM26's five
                // happiness bands. Keep the game's own localized word rather
                // than inventing a positive/negative threshold or losing it.
                if (IsNamedBodyLanguage(sprite) && TryReadRowBodyLanguage(row, out var word))
                {
                    nativeWords.TryGetValue(word, out int count);
                    nativeWords[word] = count + 1;
                    continue;
                }
                NoteDegradeOnce($"[MatchSquadReadout] body-language sprite unmapped or unreadable (sprite: {sprite ?? "missing"})");
                unreadable++;
                continue;
            }
            counts[band]++;
        }

        var bandCounts = new List<(string Band, int Count)>(BandWords.Length);
        for (int i = 0; i < BandWords.Length; i++) bandCounts.Add((BandWords[i], counts[i]));
        foreach (var pair in nativeWords) bandCounts.Add((pair.Key, pair.Value));
        var line = SquadReadout.ComposeBandCounts(bandCounts, unreadable);
        if (line == null)
        {
            NoteDegradeOnce($"[MatchSquadReadout] no countable body language ({unreadable} unreadable)");
            return "body language " + Speech.Degrade.Unavailable;
        }
        return line;
    }

    /// <summary>
    /// The Alt+3 double tap: which players carry a non-neutral body language, each with the
    /// game's detailed word ("Vázquez Complacent, Wilson Nervous"). Neutral is the resting state
    /// of most of the XI most of the time, so the double tap filters it out and names the
    /// exceptions; when everyone is neutral it says so. Band from the row icon's sprite (the
    /// game's own five-way categorisation, as the overview), word from the row binding's
    /// InteractionBodyLanguageString (as the row labels). A row whose band, name, or word cannot
    /// be read counts as unreadable and is spoken as the trailing gap clause.
    /// </summary>
    public static string DescribeBodyLanguageDetail()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ComposeBodyLanguageDetail);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] body language detail read failed: {ex.Message}");
            return "body language " + Speech.Degrade.Unavailable;
        }
    }

    private const int NeutralBand = 2; // index into BandWords

    private static string ComposeBodyLanguageDetail()
    {
        var rows = FindPlayerRows();
        if (rows == null)
        {
            NoteDegradeOnce("[MatchSquadReadout] squad panel (OverviewPrototype PlayersList) not found");
            return "body language " + Speech.Degrade.Unavailable;
        }

        var nonNeutral = new List<(string Name, string Word)>();
        int neutral = 0, unreadable = 0;
        foreach (var row in rows)
        {
            var root = row.TryCast<SI.Bindable.BindingRoot>();
            if (root == null) { unreadable++; continue; }
            if (IsOffPitch(ReadBindingValues(root))) continue;

            var sprite = ReadRowBandSprite(row);
            int band = BandIndexFromSprite(sprite);
            if (band < 0 && !IsNamedBodyLanguage(sprite)) { unreadable++; continue; }
            if (band == NeutralBand) { neutral++; continue; }

            var name = ReadPlayerName(row);
            if (name != null && TryReadRowBodyLanguage(row, out var word))
                nonNeutral.Add((name, word));
            else
                unreadable++;
        }

        var line = SquadReadout.ComposeNonNeutral(nonNeutral, neutral, unreadable);
        if (line == null)
        {
            NoteDegradeOnce($"[MatchSquadReadout] no countable body language ({unreadable} unreadable)");
            return "body language " + Speech.Degrade.Unavailable;
        }
        return line;
    }

    // The row's body-language icon: the image under one of the Data frame's SIVisible children
    // (the same slot SquadPlayerButtonAdapter reads for the label's band fallback).
    private static string ReadRowBandSprite(VisualElement row)
    {
        // FM27 inserts another wrapper above the named morph-icon slot.
        var morph = FindDescendantNamed(row, "BodyLanguageMorphIcon");
        var nativeImage = morph == null ? null : FindDescendantNamed(morph, "image");
        if (nativeImage != null) return Adapters.SpriteHelper.ReadSpriteName(nativeImage);
        var dataFrame = FindDescendantNamed(row, "Data frame");
        if (dataFrame == null) return null;
        int count = dataFrame.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = dataFrame.hierarchy[i];
            if (child.GetIl2CppType().Name != "SIVisible") continue;
            var image = FindDescendantNamed(child, "image");
            if (image == null) continue;
            var sprite = Adapters.SpriteHelper.ReadSpriteName(image);
            if (sprite != null) return sprite;
        }
        return null;
    }

    private static int BandIndexFromSprite(string spriteName)
    {
        if (spriteName == null) return -1;
        // FM27 uses named vector-image assets in the same native band slot.
        // This family was observed beside the localized "Normal" label.
        if (spriteName.StartsWith("body_language_neutral_", StringComparison.Ordinal)) return 2;
        if (spriteName.Contains("happiness=5")) return 0;       // very positive
        if (spriteName.Contains("happiness=4")) return 1;       // positive
        if (spriteName.Contains("happiness=neutral")) return 2; // neutral
        if (spriteName.Contains("happiness=2")) return 3;       // negative
        if (spriteName.Contains("happiness=1")) return 4;       // very negative
        return -1;
    }

    private static bool IsNamedBodyLanguage(string spriteName)
        => spriteName?.StartsWith("body_language_", StringComparison.Ordinal) == true;

    /// <summary>
    /// The visible player-row BindingRoots of the in-match squad panel, in display order, or null
    /// when the panel or its list is not present (not in a match, or the panel unloaded on another
    /// match tab). Visibility-gated because the list is virtualised: a recycled row that is hidden
    /// holds stale content and must never be read.
    /// </summary>
    private static List<VisualElement> FindPlayerRows()
    {
        VisualElement panel = null;
        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        for (int i = 0; i < layers.Count && panel == null; i++)
        {
            var open = layers[i].Open;
            if (open == null) continue;
            for (int p = 0; p < open.Count; p++)
            {
                var candidate = open[p];
                if (candidate != null && candidate.name == "OverviewPrototype") { panel = candidate; break; }
            }
        }
        if (panel == null) return null;

        var list = FindDescendantNamed(panel, "PlayersList");
        if (list == null) return null;

        var rows = new List<VisualElement>();
        CollectRows(list, rows);
        return rows;
    }

    private static void CollectRows(VisualElement el, List<VisualElement> rows)
    {
        if (el.name == "BindingRoot_SquadOverviewPlayerBlock")
        {
            if (ElementBridge.IsVisible(el)) rows.Add(el);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectRows(el.hierarchy[i], rows);
    }

    private static VisualElement FindDescendantNamed(VisualElement el, string name)
    {
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendantNamed(el.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// The live condition percentage for the squad row containing <paramref name="el"/>, read
    /// from the row's binding node exactly like the Alt+2 readout. For the row-button label:
    /// the number is strictly more informative than the condition sprite's coarse band (91–100
    /// all render the same "Excellent" heart). False when <paramref name="el"/> is not inside a
    /// squad row or the value is unreadable — the caller falls back to the sprite band word,
    /// which is the game's own (coarser) truth, so no gap marker is needed.
    /// </summary>
    internal static bool TryReadRowCondition(VisualElement el, out double value)
    {
        value = 0;
        try
        {
            var root = FindRowRoot(el);
            if (root == null) return false;

            var values = ReadBindingValues(root);
            return values.TryGetValue(ConditionProp, out var raw) && TryParseTrailingDouble(raw, out value);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] row condition read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The game's own detailed body-language word ("Calm", "Composed", "Complacent", ...) for the
    /// squad row containing <paramref name="el"/>, for the row-button label: the same localized
    /// string the player-card menu shows, read from the row's BodyLanguage sub-reference
    /// (InteractionBodyLanguageString — verified live alongside the numeric level the coarse
    /// positive/neutral/negative icon uses). False when the row or the string is unreadable —
    /// the caller falls back to the icon's band word, which is truthful, just vaguer.
    /// </summary>
    internal static bool TryReadRowBodyLanguage(VisualElement el, out string word)
    {
        word = null;
        try
        {
            var root = FindRowRoot(el);
            if (root == null) return false;

            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            var rootKey = root.Key;
            if (bindings == null || !bindings.TryGetNode(ref rootKey, out var node)) return false;

            var blNode = FindNodeNamed(node, "BodyLanguage", 0);
            if (blNode == null) return false;
            var blKey = blNode.Key;
            var dynRef = bindings.Get(ref blKey)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return false;

            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                if (reg.GetInfo((uint)key).Name != "InteractionBodyLanguageString") continue;
                word = dynRef[key]?.Get()?.ToString();
                return !string.IsNullOrWhiteSpace(word);
            }
            return false;
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] row body-language read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The per-player match status the squad row's own widgets display as icons: goals, assists,
    /// the two card flags and the injury type. All five come off the row's binding node in one
    /// read — the same source the game binds its own goal/assist counters and card pips to, and
    /// strictly better than scraping the rendered spacer text, which only exists once the counter
    /// widget has been laid out.
    /// </summary>
    internal readonly struct RowStatus
    {
        public readonly long Goals;
        public readonly long Assists;
        public readonly long InjuryType; // 0 = uninjured; non-zero codes are unmapped (see the adapter)
        public readonly bool Yellow;
        public readonly bool Red;

        public RowStatus(long goals, long assists, long injuryType, bool yellow, bool red)
        {
            Goals = goals;
            Assists = assists;
            InjuryType = injuryType;
            Yellow = yellow;
            Red = red;
        }
    }

    /// <summary>
    /// Outcome of <see cref="TryReadRowStatus"/>. <c>NotASquadRow</c> and <c>Unreadable</c> are
    /// deliberately distinct: outside a match there is no squad-row binding and therefore no card
    /// or goal state to report, so the caller must stay silent; a row that exists but will not
    /// yield its values is a real gap the caller must mark.
    /// </summary>
    internal enum RowStatusRead { NotASquadRow, Unreadable, Ok }

    /// <summary>
    /// Reads <see cref="RowStatus"/> for the squad row containing <paramref name="el"/>.
    /// </summary>
    internal static RowStatusRead TryReadRowStatus(VisualElement el, out RowStatus status)
    {
        status = default;
        try
        {
            var root = FindRowRoot(el);
            if (root == null) return RowStatusRead.NotASquadRow;

            var values = ReadBindingValues(root);
            // The card flag is the presence probe: it is bound unconditionally on every match
            // squad row, so its absence means the node did not materialise rather than "no card".
            if (!values.ContainsKey(YellowCardProp)) return RowStatusRead.Unreadable;

            status = new RowStatus(
                ReadCount(values, GoalsProp),
                ReadCount(values, AssistsProp),
                ReadCount(values, InjuryTypeProp),
                ReadFlag(values, YellowCardProp),
                ReadFlag(values, RedCardProp));
            return RowStatusRead.Ok;
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] row status read failed: {ex.Message}");
            return RowStatusRead.Unreadable;
        }
    }

    private static bool ReadFlag(Dictionary<string, string> values, string prop) =>
        values.TryGetValue(prop, out var raw) && raw.EndsWith("True", StringComparison.Ordinal);

    // An absent or unparseable counter reads as zero: the counters are bound on every row, and
    // the icon they drive is simply hidden at zero, so "no value" and "none" are the same state.
    private static long ReadCount(Dictionary<string, string> values, string prop) =>
        values.TryGetValue(prop, out var raw) && TryParseTrailingDouble(raw, out var value)
            ? (long)Math.Round(value)
            : 0;

    /// <summary>The squad-row BindingRoot enclosing <paramref name="el"/>, or null.</summary>
    private static SI.Bindable.BindingRoot FindRowRoot(VisualElement el)
    {
        var current = el;
        for (int i = 0; i < 12 && current != null; i++)
        {
            if (current.name == "BindingRoot_SquadOverviewPlayerBlock")
                return current.TryCast<SI.Bindable.BindingRoot>();
            current = current.hierarchy.parent;
        }
        return null;
    }

    // The collapsed match strip omits the written player name. Reuse the row's
    // existing identity boundary and verified native name subscription, never
    // a shirt number joined against another table or the human team's squad.
    internal static string RowPlayerName(VisualElement element)
    {
        var root = FindRowRoot(element);
        if (root == null) return null;
        var written = ReadPlayerName(root);
        if (written != null) return written;
        try
        {
            var node = BindingRecords.NodeOf(root);
            var info = node == null ? null : BindingRecords.FindNode(node, "playerinfo", 0);
            var personNode = info == null ? null : BindingRecords.FindNode(info, "Person", 0);
            var person = personNode == null ? null : BindingRecords.ValueOf(personNode);
            if (BindingRecords.PersonOf(person) == null) return null;
            return PlayerNameRequests.NameForEntity(personNode.Key, person, root);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] collapsed row name read failed: {ex.Message}");
            return null;
        }
    }

    private static SI.Bindable.IReadOnlyNode FindNodeNamed(SI.Bindable.IReadOnlyNode node, string name, int depth)
    {
        if (depth > 4) return null;
        foreach (var child in node.Children)
        {
            if (child.Name == name) return child;
            var found = FindNodeNamed(child, name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private readonly struct RowRead
    {
        public readonly SquadReadout.Entry? Entry; // null = unreadable, speak the gap clause
        public readonly bool OffPitch;             // red-carded / subbed off: excluded, not a gap

        public RowRead(SquadReadout.Entry? entry, bool offPitch)
        {
            Entry = entry;
            OffPitch = offPitch;
        }
    }

    // The binding-node property names on the row's MatchPlayerReference (verified live 2026-07-11:
    // MatchRating is the rating float, Condition a 0-100 percentage, the booleans the pitch gates).
    private const string RatingProp = "MatchRating";
    private const string ConditionProp = "Condition";
    private static readonly string[] OffPitchProps = { "IsSub", "RedCardedInMatch" };
    private const string GoalsProp = "Goals";
    private const string AssistsProp = "Assists";
    private const string InjuryTypeProp = "MatchInjuryType";
    private const string YellowCardProp = "YellowCardedInMatch";
    private const string RedCardProp = "RedCardedInMatch";

    private static bool IsOffPitch(Dictionary<string, string> values)
    {
        foreach (var prop in OffPitchProps)
            if (values.TryGetValue(prop, out var flag) && flag.EndsWith("True", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static RowRead ReadRow(VisualElement row, SquadMetric metric)
    {
        string name = null;
        try
        {
            name = ReadPlayerName(row);
            var root = row.TryCast<SI.Bindable.BindingRoot>();
            if (name == null || root == null)
            {
                NoteDegradeOnce($"[MatchSquadReadout] player row missing {(name == null ? "name text" : "BindingRoot")}");
                return new RowRead(null, offPitch: false);
            }

            var values = ReadBindingValues(root);
            if (IsOffPitch(values)) return new RowRead(null, offPitch: true);

            var wanted = metric == SquadMetric.Rating ? RatingProp : ConditionProp;
            if (values.TryGetValue(wanted, out var raw) && TryParseTrailingDouble(raw, out var value))
                return new RowRead(new SquadReadout.Entry(name, value), offPitch: false);

            NoteDegradeOnce($"[MatchSquadReadout] {wanted} not readable for {name}");
            return new RowRead(null, offPitch: false);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchSquadReadout] row read failed for {name ?? "a player"}: {ex.Message}");
            return new RowRead(null, offPitch: false);
        }
    }

    // The row's player name is the SIText inside the "data_display-cell-*" cell (link markup and
    // colour tags stripped). The other SITexts in the row (position title, shirt number, rating)
    // sit under differently named parents, so the cell name is the discriminator.
    private static string ReadPlayerName(VisualElement el)
    {
        var parentName = el.hierarchy.parent?.name;
        if (parentName != null && parentName.StartsWith("data_display-cell", StringComparison.Ordinal))
        {
            var text = el.TryCast<SI.Bindable.SIText>();
            if (text != null)
            {
                var clean = Speech.TextFilters.CleanForDisplay(text.text);
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = ReadPlayerName(el.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Every named leaf value in the row's binding-node subtree, as raw TypedValue strings. The
    /// row's live values (MatchRating, Condition, the card/sub booleans) hang off the node's
    /// remapper child under a "playerinfo" reference; walking the whole small subtree is simpler
    /// and more template-change-proof than hardcoding that intermediate path.
    /// </summary>
    private static Dictionary<string, string> ReadBindingValues(SI.Bindable.BindingRoot root)
    {
        var values = new Dictionary<string, string>();
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var rootKey = root.Key;
        if (bindings == null || !bindings.TryGetNode(ref rootKey, out var node)) return values;
        CollectNodeValues(bindings, node, 0, values);
        return values;
    }

    private static void CollectNodeValues(SI.Bindable.BindingSubsystem bindings,
        SI.Bindable.IReadOnlyNode node, int depth, Dictionary<string, string> values)
    {
        if (depth > 4) return; // the wanted values sit at depth 2; bound tight so a read stays cheap
        foreach (var child in node.Children)
        {
            var childName = child.Name;
            if (!values.ContainsKey(childName))
            {
                var childKey = child.Key;
                var value = bindings.Get(ref childKey);
                if (value != null) values[childName] = value.ToString();
            }
            CollectNodeValues(bindings, child, depth + 1, values);
        }
    }

    // A NumericTypedValue stringifies as "SI.Core.NumericTypedValue, 6.69999980926514"; take the
    // trailing number (the established ParseTrailingLong read, widened to double for ratings).
    private static bool TryParseTrailingDouble(string typedValueString, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(typedValueString)) return false;
        var comma = typedValueString.LastIndexOf(',');
        var numeric = comma >= 0 ? typedValueString.Substring(comma + 1).Trim() : typedValueString.Trim();
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

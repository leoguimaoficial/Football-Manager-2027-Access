using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Features;

/// <summary>
/// Reads a roster table's form cell — the game's pip tile — into display text for the HTML
/// surface: the last up-to-5 match ratings, most recent first, numbers only.
///
/// The cell's button holds <c>tile states-player-form-small</c> → a <c>HasMatches</c> switch
/// whose active branch is either <c>MatchRating</c> (a virtualised list of up to 5 pips, each an
/// AsyncBindingRoot whose Tooltip carries a BindingRoot) or <c>NoMatches</c> (the game's own
/// localized empty-state text). Each data-bearing pip's binding node has a <c>MatchRating100</c>
/// child (rating × 10). Pip document order is oldest-first — proven by aligning goalkeepers'
/// per-pip conceded counts against dated season results — so the formatter reverses it.
///
/// Everything is read live from the binding subsystem at model-build time; nothing is cached.
/// The visual pips are colour-only and unreadable as text, which is why this goes to the data.
/// </summary>
internal static class FormRatingsReader
{
    private const string RatingNode = "MatchRating100";
    private const string PlayerFormNode = "playerform";

    // The pip list's items-container binding key (protected on StreamedObjectList). The model
    // route reads the pips straight off this container's node children, so it works even when
    // the row was realized moments ago and the per-pip AsyncBindingRoots have not bound yet —
    // which is exactly the state table rows are in during a cell sweep.
    private static readonly System.Reflection.PropertyInfo ItemsContainerKeyProp =
        typeof(SI.Bindable.StreamedObjectList).GetProperty("m_itemsContainerKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// The cell's ratings text, or null when the stop is not a form-pip cell (every other
    /// column — no behavior change). A present-but-unreadable tile returns a gap marker, never
    /// null and never an empty string: an empty cell would read as "no data", which is a claim
    /// about the player this reader cannot honestly make when the read failed.
    /// </summary>
    internal static string Read(VisualElement stopElement)
    {
        var tile = FindTile(stopElement);
        if (tile == null) return null;

        try
        {
            return ReadTile(tile);
        }
        catch (Exception e)
        {
            return Degrade.Read($"[FormRatings] tile read threw {e.GetType().Name}: {e.Message}");
        }
    }

    // The tile is a direct child of the cell's button (the same shape FormButtonAdapter.IsFormButton
    // keys on). Direct children only: this runs for every button stop of every table cell, and a
    // subtree search here would be a per-cell cost on the mod's hottest path.
    private static VisualElement FindTile(VisualElement stopElement)
    {
        if (stopElement == null) return null;
        try
        {
            return Adapters.FormButtonAdapter.FindFormTile(stopElement);
        }
        catch { return null; }
    }

    private static string ReadTile(VisualElement tile)
    {
        // The HasMatches switch and its two branches. Bounded name lookups, not a full walk.
        var hasMatches = FindDescendantNamed(tile, "HasMatches", 3);
        if (hasMatches == null)
            return Degrade.Read("[FormRatings] form tile has no HasMatches switch.");

        var noMatches = FindDescendantNamed(hasMatches, "NoMatches", 2);
        if (noMatches != null && ElementBridge.IsVisible(noMatches))
        {
            // The game's own empty-state text, verbatim.
            string empty = FirstTextIn(noMatches, 0);
            return string.IsNullOrEmpty(empty)
                ? Degrade.Read("[FormRatings] NoMatches branch active but its text would not read.")
                : empty;
        }

        // Model route first: the pip list's container node holds every pip's record whether or
        // not the pip elements have bound yet. Falls back to the realized-element walk, which
        // covers a tile whose list element has not received its container key.
        var ratings = ReadOrderedSeries(hasMatches);
        if (ratings.Count == 0)
            return Degrade.Read("[FormRatings] tile active but no pip ratings resolved.");
        return FormRatingsHtml.Format(ratings);
    }

    // The full form-history graph uses the same playerform/MatchRating100
    // bindings as the inherited five-pip cell. Keep native order here; only
    // the roster-cell formatter reverses it for its familiar recent-first text.
    internal static List<int?> ReadOrderedSeries(VisualElement source)
    {
        var ratings = new List<int?>();
        if (!TryReadModel(source, ratings))
        {
            int budget = 0;
            CollectPipRatings(source, ratings, ref budget, 0);
        }
        return ratings;
    }

    // True when the container node yielded at least one rating-bearing item. Items are ordered
    // by their node NAME, an explicit chronological index (0 = oldest — verified against the
    // realized pips' document order); enumeration order is not trusted. An item whose name will
    // not parse makes the whole model read untrustworthy — the sequence the player hears would
    // be in an order the mod guessed — so the read reports failure and the caller degrades.
    private static bool TryReadModel(VisualElement hasMatches, List<int?> ratings)
    {
        var listEl = FindStreamedList(hasMatches, 0);
        if (listEl == null || ItemsContainerKeyProp == null) return false;

        SI.Bindable.BindingSubsystem bindings;
        SI.Bindable.IReadOnlyNode container;
        try
        {
            var typed = listEl.Cast<SI.Bindable.StreamedObjectList>();
            if (ItemsContainerKeyProp.GetValue(typed) is not SI.Bindable.Bindings.Key key) return false;
            bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null || !bindings.TryGetNode(ref key, out container)) return false;
        }
        catch (Exception e)
        {
            Degrade.Note($"[FormRatings] pip container read threw {e.GetType().Name}.");
            return false;
        }

        var items = new List<(int Order, int? Rating)>();
        try
        {
            foreach (var item in container.Children)
            {
                if (!TryRatingFromNode(bindings, item, out int? rating))
                    continue; // not a pip item (never observed; skip, don't invent a slot)

                if (!int.TryParse(item.Name, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int order))
                {
                    Degrade.Note($"[FormRatings] pip item name \"{item.Name}\" is not a chronological index; model order untrustworthy.");
                    return false;
                }
                items.Add((order, rating));
            }
        }
        catch (Exception e)
        {
            Degrade.Note($"[FormRatings] pip container walk threw {e.GetType().Name}.");
            return false;
        }
        if (items.Count == 0) return false;

        items.Sort((a, b) => a.Order.CompareTo(b.Order));
        foreach (var item in items) ratings.Add(item.Rating);
        return true;
    }

    private static VisualElement FindStreamedList(VisualElement el, int depth)
    {
        if (el == null || depth > 6) return null;
        try
        {
            if (el.GetIl2CppType().Name == "StreamedObjectList") return el;
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var found = FindStreamedList(el.hierarchy[i], depth + 1);
                if (found != null) return found;
            }
        }
        catch { return null; }
        return null;
    }

    // Every data-bearing pip BindingRoot under the tile, in document order (oldest first). A
    // BindingRoot with no node, or whose node has no MatchRating100 child, is the visual layer
    // and is skipped; a root whose MatchRating100 child exists but will not read holds its slot
    // as null so the gap lands in the right chronological position.
    private static void CollectPipRatings(VisualElement el, List<int?> ratings, ref int budget, int depth)
    {
        if (el == null || depth > 20 || budget++ > 600) return;

        SI.Bindable.BindingRoot root = null;
        try { root = el.TryCast<SI.Bindable.BindingRoot>(); } catch { }
        if (root != null && TryReadPip(root, out int? rating))
        {
            ratings.Add(rating);
            return; // a pip's BindingRoot has no nested pips
        }

        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            VisualElement child;
            try { child = el.hierarchy[i]; } catch { return; }
            CollectPipRatings(child, ratings, ref budget, depth + 1);
        }
    }

    // True when this BindingRoot is a data-bearing pip (its node carries a rating).
    // rating is null when the rating node exists but its value would not read.
    private static bool TryReadPip(SI.Bindable.BindingRoot root, out int? rating)
    {
        rating = null;
        try
        {
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null) return false;
            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) return false;
            return TryRatingFromNode(bindings, node, out rating);
        }
        catch (Exception e)
        {
            Degrade.Note($"[FormRatings] pip binding read threw {e.GetType().Name}.");
            return false;
        }
    }

    // A pip's rating off its node. The MatchRating100 leaf lives one level down, under the
    // node's "playerform" child (the per-match record's node); the direct-child check stays as
    // cheap insurance against the game flattening that nesting. True when a rating node was
    // found at either level; rating stays null when it was found but would not read.
    private static bool TryRatingFromNode(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode node, out int? rating)
    {
        rating = null;
        foreach (var child in node.Children)
        {
            if (child.Name == RatingNode)
            {
                rating = RatingValue(bindings, child);
                return true;
            }
            if (child.Name != PlayerFormNode) continue;
            foreach (var sub in child.Children)
            {
                if (sub.Name != RatingNode) continue;
                rating = RatingValue(bindings, sub);
                return true;
            }
        }
        return false;
    }

    private static int? RatingValue(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode ratingNode)
    {
        var key = ratingNode.Key;
        var value = bindings.Get(ref key);
        var typed = value?.TryCast<SI.Core.TypedValue>();
        string raw = null;
        try { raw = typed?.AsString(); } catch { }
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            return parsed;
        Degrade.Note($"[FormRatings] pip MatchRating100 would not read (raw=\"{raw}\").");
        return null;
    }

    private static VisualElement FindDescendantNamed(VisualElement el, string name, int maxDepth)
    {
        if (el == null) return null;
        try
        {
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = el.hierarchy[i];
                if (child.name == name) return child;
                if (maxDepth > 1)
                {
                    var deeper = FindDescendantNamed(child, name, maxDepth - 1);
                    if (deeper != null) return deeper;
                }
            }
        }
        catch { return null; }
        return null;
    }

    private static string FirstTextIn(VisualElement el, int depth)
    {
        if (el == null || depth > 6) return null;
        try
        {
            var text = el.TryCast<TextElement>();
            if (text != null)
            {
                string raw = text.text;
                if (!string.IsNullOrWhiteSpace(raw))
                    return TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(raw.Trim()));
            }
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var found = FirstTextIn(el.hierarchy[i], depth + 1);
                if (found != null) return found;
            }
        }
        catch { return null; }
        return null;
    }
}

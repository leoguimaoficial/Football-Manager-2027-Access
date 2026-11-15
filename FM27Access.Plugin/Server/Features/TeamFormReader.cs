using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Features;

/// <summary>
/// Reads a standings table's team "Form" cell into display text for the HTML surface: the last
/// up-to-5 league results, most recent first, runs collapsed ("2W, L, 2D").
///
/// The cell holds a StreamedObjectList of colour-only pips. FM26 wraps it in an element named
/// <c>form</c>; current FM27 leaves it unnamed, so its bound result schema identifies it.
/// The list's items-container node has one child per match, named by chronological index
/// (0 = oldest, verified: the highest index matches every team's latest result), each with a
/// <c>teamform</c> child (3 win, 1/2 draw, 0 defeat — see <see cref="TeamFormHtml"/>). The model
/// is read rather than the realized pips so a freshly realized row reads before its pips bind.
///
/// The W/D/L letters are the game's own league-table header abbreviations, resolved through the
/// translation table so they follow the game language. Everything is read live; nothing cached.
/// </summary>
internal static class TeamFormReader
{
    private const string FormName = "form";
    private const string ValueNode = "teamform";

    // Runtime GameIDs of the league-table header abbreviations — the run 27303..27309 reads
    // P, W, D, (unrelated), L, GD, PTS, which pins these as Won/Drawn/Lost.
    private const int WinLetterId = 27304;
    private const int DrawLetterId = 27305;
    private const int LossLetterId = 27307;

    private static readonly System.Reflection.PropertyInfo ItemsContainerKeyProp =
        typeof(SI.Bindable.StreamedObjectList).GetProperty("m_itemsContainerKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// The cell's form text, or null when no named form/list or bound result schema identifies it.
    /// An identified form list that cannot be read returns the inherited gap marker. An unnamed
    /// empty list contains no result schema; the native empty cell remains unchanged.
    /// </summary>
    internal static string Read(VisualElement cell)
    {
        try
        {
            var form = FindNamed(cell, FormName, 8);
            var list = form == null ? null : FindTyped(form, "StreamedObjectList", 2);
            // FM27's current standings cell drops the old named wrapper, but
            // retains the same chronological teamform model. Identify that
            // native result schema instead of guessing from the column title,
            // pip colours or list position. All reading/formatting stays FM26.
            list ??= FindBoundResults(cell, 8);
            if (list == null) return null;
            return ReadList(list);
        }
        catch (Exception e)
        {
            return Degrade.Value($"[TeamForm] form cell read threw {e.GetType().Name}: {e.Message}");
        }
    }

    private static VisualElement FindBoundResults(VisualElement element, int depth)
    {
        if (depth < 0 || !ElementBridge.IsVisibleInHierarchy(element)) return null;
        if (element.GetIl2CppType().Name == "StreamedObjectList"
            && ItemsContainerKeyProp?.GetValue(element.Cast<SI.Bindable.StreamedObjectList>()) is SI.Bindable.Bindings.Key key)
        {
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings.TryGetNode(ref key, out var container))
                foreach (var item in container.Children)
                    foreach (var value in item.Children)
                        if (value.Name == ValueNode) return element;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var found = FindBoundResults(element.hierarchy[i], depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static string ReadList(VisualElement listEl)
    {
        if (ItemsContainerKeyProp == null)
            return Degrade.Value("[TeamForm] StreamedObjectList.m_itemsContainerKey not found.");

        var typed = listEl.Cast<SI.Bindable.StreamedObjectList>();
        if (ItemsContainerKeyProp.GetValue(typed) is not SI.Bindable.Bindings.Key key)
            return Degrade.Value("[TeamForm] form list has no items-container key.");
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (!bindings.TryGetNode(ref key, out var container))
            return Degrade.Value("[TeamForm] form list container node not found.");

        var items = new List<(int Order, int? Value)>();
        foreach (var item in container.Children)
        {
            SI.Bindable.IReadOnlyNode valueNode = null;
            foreach (var child in item.Children)
                if (child.Name == ValueNode) { valueNode = child; break; }
            if (valueNode == null) continue; // not a result item (never observed)

            if (!int.TryParse(item.Name, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int order))
                return Degrade.Value($"[TeamForm] item name \"{item.Name}\" is not a chronological index.");
            items.Add((order, ReadValue(bindings, valueNode)));
        }

        // An empty container is also what an unbound list looks like, so it cannot be told apart
        // from a team with no league matches yet; say so rather than show a blank cell.
        if (items.Count == 0)
            return Degrade.Value("[TeamForm] form list has no result items.");

        if (!TryLetters(out var letters))
            return Degrade.Unavailable;

        items.Sort((a, b) => a.Order.CompareTo(b.Order));
        var oldestFirst = new List<int?>(items.Count);
        foreach (var item in items) oldestFirst.Add(item.Value);
        return TeamFormHtml.Format(oldestFirst, letters);
    }

    private static int? ReadValue(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode node)
    {
        var key = node.Key;
        var typed = bindings.Get(ref key)?.TryCast<SI.Core.TypedValue>();
        string raw = typed?.AsString();
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            if (value < 0 || value > 3) Degrade.Note($"[TeamForm] unknown teamform value {value}.");
            return value;
        }
        Degrade.Note($"[TeamForm] teamform would not read (raw=\"{raw}\").");
        return null;
    }

    // The three letters must each resolve to a short, distinct string. A game update that shifts
    // these runtime ids would otherwise put an unrelated word in every form cell.
    private static bool TryLetters(out TeamFormHtml.Letters letters)
    {
        string win = Letter(WinLetterId), draw = Letter(DrawLetterId), loss = Letter(LossLetterId);
        letters = new TeamFormHtml.Letters(win, draw, loss);
        bool ok = IsLetter(win) && IsLetter(draw) && IsLetter(loss)
                  && win != draw && draw != loss && win != loss;
        if (!ok) Degrade.Note($"[TeamForm] result letters failed the sanity check (\"{win}\", \"{draw}\", \"{loss}\").");
        return ok;
    }

    private static string Letter(int id) =>
        TextFilters.CleanForDisplay(new SI.Translation.TranslationID(id, string.Empty).TranslatedString)?.Trim();

    internal static string ResultLetter(int value)
        => TryLetters(out var letters)
            ? TeamFormHtml.Format(new int?[] { value }, letters)
            : Degrade.Unavailable;

    private static bool IsLetter(string s) => !string.IsNullOrEmpty(s) && s.Length <= 3;

    // Bounded name lookup below the cell: the form element sits a few levels under it.
    private static VisualElement FindNamed(VisualElement el, string name, int maxDepth)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (child.name == name) return child;
            if (maxDepth > 1)
            {
                var deeper = FindNamed(child, name, maxDepth - 1);
                if (deeper != null) return deeper;
            }
        }
        return null;
    }

    private static VisualElement FindTyped(VisualElement el, string typeName, int maxDepth)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName) return child;
            if (maxDepth > 1)
            {
                var deeper = FindTyped(child, typeName, maxDepth - 1);
                if (deeper != null) return deeper;
            }
        }
        return null;
    }
}

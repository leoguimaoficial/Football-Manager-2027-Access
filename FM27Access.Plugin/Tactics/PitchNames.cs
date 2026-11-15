using System.Collections.Generic;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Tactics;

/// <summary>
/// Resolves occupant names for pitch shirts that render number-only (the collapsed Team
/// Selection pitch binds shirt numbers but leaves the shirt-name text blank). The pitch slots
/// expose their resolved PlayerIndex key and its sibling Player reference in FM27. Ask the
/// existing verified native name reader for that exact person; no realized table row is needed.
/// The inherited FM26 row join remains a fallback when that native shirt path is absent.
/// The row map is built lazily (only a numbers-only pitch pays
/// for it) and lives for one <see cref="PitchGrid.ReadFormation"/> call, so nothing is cached
/// across reads. A slot or row whose bindings can't be read is skipped: the slot keeps its
/// number-only identity, which <c>PitchFormation</c> already presents honestly.
/// </summary>
internal sealed class PitchNameLookup
{
    private readonly VisualElement _pitch;
    private Dictionary<long, string> _byPlayerIndex;

    internal PitchNameLookup(VisualElement pitch) => _pitch = pitch;

    /// <summary>The occupant name for this slot, or null when it cannot be read.</summary>
    internal string NameFor(VisualElement indicator)
    {
        // FM27's compact shirt no longer contains a BindingRoot and its table
        // names are SIText links, not FM26's demoted SIButton. Read the exact
        // Position.Player sibling of the shirt's resolved PlayerIndex instead.
        var nativeName = NameFromShirt(indicator) ?? NameFromKit(indicator);
        if (nativeName != null) return nativeName;
        long playerIndex = ReadPlayerIndex(FindBindingRoot(indicator, 0), "Position");
        if (playerIndex < 0) return null;
        _byPlayerIndex ??= BuildRowNameMap();
        return _byPlayerIndex.TryGetValue(playerIndex, out var name) ? name : null;
    }

    private static string NameFromShirt(VisualElement indicator)
    {
        var shirt = FindShirt(indicator);
        if (shirt == null) return null;
        try
        {
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            var key = shirt.m_playerIndexKey;
            if (bindings == null || key.m_key == 0 || !bindings.TryGetNode(ref key, out var node)
                || node.Name != "PlayerIndex" || node.Parent == null) return null;
            var player = Server.BindingRecords.FindNode(node.Parent, "Player", 0);
            if (player == null) return null;
            var person = Server.BindingRecords.ValueOf(player);
            if (Server.BindingRecords.PersonOf(person) == null) return null;
            return Server.PlayerNameRequests.NameForEntity(player.Key, person, shirt);
        }
        catch (System.Exception ex)
        {
            Log.Warn($"[PitchNames] shirt occupant read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    internal static FM.UI.PersonReference PersonForShirt(VisualElement indicator)
    {
        var shirt = FindShirt(indicator);
        if (shirt == null) return null;
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var key = shirt.m_playerIndexKey;
        if (bindings == null || key.m_key == 0 || !bindings.TryGetNode(ref key, out var node)
            || node.Name != "PlayerIndex" || node.Parent == null) return null;
        var player = Server.BindingRecords.FindNode(node.Parent, "Player", 0);
        return player == null ? null : Server.BindingRecords.PersonOf(Server.BindingRecords.ValueOf(player));
    }

    private static FM.UI.FMTacticsShirt FindShirt(VisualElement element)
    {
        var shirt = element.TryCast<FM.UI.FMTacticsShirt>();
        if (shirt != null) return shirt;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            shirt = FindShirt(element.hierarchy[i]);
            if (shirt != null) return shirt;
        }
        return null;
    }

    private static string NameFromKit(VisualElement indicator)
    {
        var number = PitchGrid.KitNumberText(indicator);
        if (number == null) return null;
        try
        {
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            var key = SI.Bindable.BindingUtils.GetViewKey(number);
            if (bindings == null || key.m_key == 0 || !bindings.TryGetNode(ref key, out var node)) return null;
            var personNode = Server.BindingRecords.FindNode(node, "Person", 0);
            var person = personNode == null ? null : Server.BindingRecords.ValueOf(personNode);
            if (Server.BindingRecords.PersonOf(person) == null) return null;
            return Server.PlayerNameRequests.NameForEntity(personNode.Key, person, number);
        }
        catch (System.Exception ex)
        {
            Log.Warn($"[PitchNames] kit occupant read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // Every squad-table row pairs a demoted name-link button (SIButton named "SIButton" with a
    // dropdown in a sibling subtree — the same predicate DropdownAdapter names the row by) with
    // a binding node whose "binding" DynamicReference carries the row's PlayerIndex.
    private Dictionary<long, string> BuildRowNameMap()
    {
        var map = new Dictionary<long, string>();
        var root = _pitch;
        while (root.hierarchy.parent != null) root = root.hierarchy.parent;
        int budget = 0;
        CollectRows(root, map, ref budget);
        return map;
    }

    private void CollectRows(VisualElement el, Dictionary<long, string> map, ref int budget)
    {
        if (budget++ > 20000) return;
        string type;
        try { type = el.GetIl2CppType().Name; }
        catch (System.Exception e) { Log.Warn($"[PitchNames] type read failed: {e.GetType().Name}"); return; }

        if (type == "SIButton" && el.name == "SIButton" && ElementBridge.HasSiblingSubtreeWithDropdown(el))
        {
            var texts = new List<string>();
            CollectTexts(el, texts);
            if (texts.Count > 0)
            {
                long playerIndex = ReadPlayerIndex(FindAncestorBindingRoot(el), "binding");
                if (playerIndex >= 0)
                    map[playerIndex] = TextFilters.CleanForDisplay(string.Join(", ", texts));
            }
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectRows(el.hierarchy[i], map, ref budget);
    }

    // The name-link button's texts; the row's shirt-number badge is excluded (the pitch model
    // carries the number separately).
    private static void CollectTexts(VisualElement el, List<string> texts)
    {
        if (el.name == "fm-tactics-shirt-number") return;
        var textEl = el.TryCast<TextElement>();
        if (textEl != null && !string.IsNullOrEmpty(textEl.text)) texts.Add(textEl.text);
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], texts);
    }

    // Reads PlayerIndex out of the named child of a binding node: the child's value is a
    // ReferenceTypedValue wrapping a DynamicReference whose PlayerIndex property is the
    // database person index — the join key shared by pitch slots and squad rows.
    private static long ReadPlayerIndex(SI.Bindable.BindingRoot root, string childName)
    {
        if (root == null) return -1;
        try
        {
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null) return -1;
            var rootKey = root.Key;
            if (!bindings.TryGetNode(ref rootKey, out var node)) return -1;

            foreach (var child in node.Children)
            {
                if (child.Name != childName) continue;
                var childKey = child.Key;
                var value = bindings.Get(ref childKey);
                var dynRef = value?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                if (dynRef == null) return -1;

                var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
                var keys = dynRef.Keys.GetEnumerator();
                while (keys.MoveNext())
                {
                    var key = keys.Current;
                    if (reg.GetInfo((uint)key).Name != "PlayerIndex") continue;
                    return ParseTrailingLong(dynRef[key]?.ToString());
                }
                return -1;
            }
        }
        catch (System.Exception e)
        {
            Log.Warn($"[PitchNames] PlayerIndex read failed: {e.GetType().Name}: {e.Message}");
        }
        return -1;
    }

    // A NumericTypedValue stringifies as "SI.Core.NumericTypedValue, 5349"; take the trailing
    // number (the established PresetButtonAdapter/PlayerInstructionsButtonAdapter read).
    private static long ParseTrailingLong(string typedValueString)
    {
        if (string.IsNullOrEmpty(typedValueString)) return -1;
        int comma = typedValueString.LastIndexOf(',');
        var numeric = comma >= 0 ? typedValueString.Substring(comma + 1).Trim() : typedValueString.Trim();
        return long.TryParse(numeric, out var value) ? value : -1;
    }

    private static SI.Bindable.BindingRoot FindBindingRoot(VisualElement el, int depth)
    {
        if (depth > 10) return null;
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root != null) return root;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindBindingRoot(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static SI.Bindable.BindingRoot FindAncestorBindingRoot(VisualElement el)
    {
        var current = el.hierarchy.parent;
        for (int depth = 0; current != null && depth < 25; depth++)
        {
            var root = current.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
            current = current.hierarchy.parent;
        }
        return null;
    }
}

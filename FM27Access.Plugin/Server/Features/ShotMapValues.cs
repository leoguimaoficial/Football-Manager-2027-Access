using System;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads a shot-map cell's goal count from the map's binding rather than from the cell's drawn text.
///
/// Needed because the game's own shot map never renders the Long-Range cell: its
/// <c>SIVisible_NoData</c> branch is always the live one, so the cell draws the same "no goals" dash
/// whether the value is zero or not. Verified 2026-08-02 by totals — Goal Areas, Goal Types and Goal
/// Times must sum alike, and Areas came up short by exactly
/// <c>LocationOutsideBoxFarFromGoal</c> on the one map where that value was non-zero (15 vs 17;
/// every map where it was zero agreed at 13/13/13 and 19/19/19).
///
/// Reproducing the dash faithfully would mean telling the player "none" when the game's own data
/// says two, so the value is read from the binding instead. This is a deliberate divergence from
/// what is drawn on screen, and the only one: a cell the game DOES render keeps its rendered text.
///
/// Every zone's remapper node carries a shared reference listing all thirteen locations by name with
/// their values, so any zone under the map can be used as the anchor. Main thread only.
/// </summary>
internal static class ShotMapValues
{
    // The remapper child holding the shared all-locations reference, and the value property on each
    // location within it.
    private const string LocationsChild = "_remap0";
    private const string ValueProperty = "SubjectStatValue";

    /// <summary>
    /// The named location's count from <paramref name="mapContainer"/>'s binding. False when the map
    /// carries no such reading — the caller then keeps whatever the cell drew, which is the honest
    /// fallback rather than a fabricated number.
    /// </summary>
    internal static bool TryReadLocation(VisualElement mapContainer, string property, out int value)
    {
        value = 0;
        try
        {
            var remapper = FindRemapper(mapContainer, 0);
            if (remapper == null) return false;

            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            if (bindings == null) return false;

            var key = remapper.Key;
            if (!bindings.TryGetNode(ref key, out var node)) return false;

            foreach (var child in node.Children)
            {
                if (!string.Equals(child.Name, LocationsChild, StringComparison.Ordinal)) continue;
                var childKey = child.Key;
                var locations = bindings.Get(ref childKey)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                if (locations == null) return false;
                var location = FindProperty(locations, property);
                if (location == null) return false;
                var raw = FindValue(location, ValueProperty);
                return raw != null && int.TryParse(raw.AsString(), out value);
            }
            return false;
        }
        catch (Exception e)
        {
            Degrade.Note($"shot map: {property} read failed: {e.GetType().Name}");
            return false;
        }
    }

    // The named property of a DynamicReference, resolved through the property registry (the keys are
    // numeric ids; only the registry knows their names). Null when the reference does not carry it.
    private static SI.Bindable.DynamicReference FindProperty(SI.Bindable.DynamicReference reference, string name)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var keys = reference.Keys.GetEnumerator();
        while (keys.MoveNext())
        {
            var key = keys.Current;
            string keyName;
            try { keyName = reg.GetInfo((uint)key).Name; }
            catch { continue; }
            if (!string.Equals(keyName, name, StringComparison.Ordinal)) continue;
            return reference[key]?.Get()?.TryCast<SI.Bindable.DynamicReference>();
        }
        return null;
    }

    // Overload for the leaf read: the value property is a plain numeric, not a reference.
    private static SI.Core.TypedValue FindValue(SI.Bindable.DynamicReference reference, string name)
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

    private static SI.Bindable.BindingRemapper FindRemapper(VisualElement el, int depth)
    {
        if (depth > 8) return null;
        var r = el.TryCast<SI.Bindable.BindingRemapper>();
        if (r != null) return r;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindRemapper(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}

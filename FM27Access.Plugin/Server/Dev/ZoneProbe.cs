using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the analysis zone maps (the Opposition Report's Goal Areas shot map, the
/// set-piece First Contact zone map, the Team Analytics zone map). Each zone renders as an indexed
/// template with a count and no name of any kind, so the companion can only label them positionally
/// ("Zone 4"), which tells a blind player nothing.
///
/// The game DOES hold names for these locations (its localisation table carries 23 strings
/// commented "Goal analysis; goal area of pitch"). This dumps each zone's binding node — child
/// names, typed values, and any DynamicReference properties — to establish whether a zone carries a
/// readable id the mod could map to those names, and prints each zone's container nesting so the
/// grid's row/column structure is visible without a layout-flushing geometry read.
///
/// GET /zoneprobe. Read-only, main thread, no geometry.
/// </summary>
internal static class ZoneProbe
{
    // The indexed zone templates of each map family. Matched by name prefix: the trailing -N is the
    // zone's draw order, which is all the tree exposes today.
    private static readonly string[] ZonePrefixes =
    {
        "data_display-graph-shotmap-data-",
        "data_display-graph-zonemap-data-",
        "data_display-graph-first-contact-data-",
        // The set-piece First Contact map streams its cells instead of indexing them: each is a
        // plainly-named "zone frame" holding one percentage.
        "zone frame",
    };

    internal static string Run()
    {
        var sb = new StringBuilder();
        var panel = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
        if (panel == null) return "no open panel\n";

        var zones = new List<VisualElement>();
        Collect(panel, zones, 0);
        if (zones.Count == 0)
        {
            sb.Append("no indexed zone templates on the active panel; prefixes tried:\n");
            foreach (var p in ZonePrefixes) sb.Append("  ").Append(p).Append('\n');
            return sb.ToString();
        }

        sb.Append("zones=").Append(zones.Count).Append('\n');
        foreach (var zone in zones)
        {
            sb.Append("========== ").Append(zone.name).Append(" ==========\n");
            sb.Append("  nesting: ").Append(NestingPath(zone)).Append('\n');
            sb.Append("  texts: ").Append(Texts(zone)).Append('\n');
            DumpBindings(zone, sb);
        }
        return sb.ToString();
    }

    private static void Collect(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > 90) return;
        var name = el.name;
        if (!string.IsNullOrEmpty(name))
            foreach (var prefix in ZonePrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], into, depth + 1);
    }

    // The zone's position in its container grid, as a chain of child indices from the map layout
    // down to the zone: "1/0/2" means layout child 1, its child 0, its child 2. This is the grid
    // structure the drawn map encodes, read without touching layout or worldBound.
    private static string NestingPath(VisualElement zone)
    {
        var parts = new List<string>();
        var el = zone;
        for (int i = 0; i < 6 && el.hierarchy.parent != null; i++)
        {
            var parent = el.hierarchy.parent;
            int index = -1;
            for (int c = 0; c < parent.hierarchy.childCount; c++)
                if (parent.hierarchy[c].Pointer == el.Pointer) { index = c; break; }
            parts.Insert(0, index + " of " + parent.hierarchy.childCount
                + (string.IsNullOrEmpty(parent.name) ? "" : " [" + parent.name + "]"));
            el = parent;
        }
        return string.Join("  <  ", parts.ToArray());
    }

    private static string Texts(VisualElement el)
    {
        var sb = new StringBuilder();
        CollectTexts(el, sb, 0);
        return sb.ToString();
    }

    private static void CollectTexts(VisualElement el, StringBuilder sb, int depth)
    {
        if (depth > 12) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var t = el.TryCast<TextElement>()?.text;
            if (!string.IsNullOrEmpty(t)) sb.Append('"').Append(Speech.TextFilters.CleanForDisplay(t)).Append("\" ");
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectTexts(el.hierarchy[i], sb, depth + 1);
    }

    // The zone's own binding node (searched downward first, then the nearest ancestor), listing
    // every child name/value and, for reference-typed values, the DynamicReference's properties by
    // name. A zone id or location name, if the game exposes one at all, surfaces here.
    private static void DumpBindings(VisualElement zone, StringBuilder sb)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append("  no BindingSubsystem\n"); return; }

        DumpRemapperNode(zone, sb, bindings);

        var root = FindBindingRootBelow(zone, 0) ?? FindBindingRootAbove(zone);
        if (root == null) { sb.Append("  no BindingRoot near this zone\n"); return; }

        var rootKey = root.Key;
        if (!bindings.TryGetNode(ref rootKey, out var node)) { sb.Append("  TryGetNode failed\n"); return; }

        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            SI.Core.TypedValue value;
            try { value = bindings.Get(ref childKey); }
            catch (Exception e) { sb.Append("  child \"").Append(child.Name).Append("\" = err:").Append(e.GetType().Name).Append('\n'); continue; }
            sb.Append("  child \"").Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
            DumpReference(value, sb);
        }
    }

    private static void DumpReference(SI.Core.TypedValue typedValue, StringBuilder sb)
    {
        try
        {
            if (typedValue == null) return;
            var reference = typedValue.Get();
            var dynRef = reference?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;

            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                string name;
                try { name = reg.GetInfo((uint)key).Name; }
                catch { name = "key:" + key; }
                SI.Core.TypedValue inner = null;
                string val;
                try { inner = dynRef[key]; val = inner?.ToString() ?? "null"; }
                catch (Exception e) { val = "err:" + e.GetType().Name; }
                sb.Append("    ref ").Append(name).Append(" = ").Append(val).Append('\n');
                // One level deeper for the named Location* properties: each is itself a reference
                // whose SubjectStatValue is that location's goal count. Reading them lets a zone
                // cell be matched to its location by value, without touching the remapper's
                // Mappings (List<RemapInfo> — generic over a struct, unsafe to reflect).
                if (name != null && name.StartsWith("Location", StringComparison.Ordinal))
                    DumpLocationValue(inner, sb);
            }
        }
        catch (Exception e)
        {
            sb.Append("    ref dump failed: ").Append(e.GetType().Name).Append('\n');
        }
    }

    // The zone's own remapper — the element that binds THIS cell to its slice of the map's data,
    // and so the only place a per-zone identity could live. Its Mappings list is deliberately NOT
    // read: List<RemapInfo> is generic over a struct, which IL2CPP interop cannot reflect safely.
    // The remapper's binding NODE is a plain read and carries the same information if it is there.
    private static void DumpRemapperNode(VisualElement zone, StringBuilder sb, SI.Bindable.BindingSubsystem bindings)
    {
        var remapper = FindRemapper(zone, 0) ?? zone.hierarchy.parent?.TryCast<SI.Bindable.BindingRemapper>();
        if (remapper == null) { sb.Append("  no BindingRemapper on this zone\n"); return; }
        var key = remapper.Key;
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append("  remapper node: TryGetNode failed\n"); return; }
        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            SI.Core.TypedValue value;
            try { value = bindings.Get(ref childKey); }
            catch (Exception e) { sb.Append("  remap \"").Append(child.Name).Append("\" = err:").Append(e.GetType().Name).Append('\n'); continue; }
            sb.Append("  remap \"").Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
            DumpReference(value, sb);
        }
    }

    private static void DumpLocationValue(SI.Core.TypedValue typedValue, StringBuilder sb)
    {
        try
        {
            var dynRef = typedValue?.Get()?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;
            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                string name;
                try { name = reg.GetInfo((uint)key).Name; }
                catch { continue; }
                try { sb.Append("        ").Append(name).Append(" = ").Append(dynRef[key]?.ToString() ?? "null").Append('\n'); }
                catch (Exception e) { sb.Append("        ").Append(name).Append(" = err:").Append(e.GetType().Name).Append('\n'); }
            }
        }
        catch (Exception e)
        {
            sb.Append("        location dump failed: ").Append(e.GetType().Name).Append('\n');
        }
    }

    private static SI.Bindable.BindingRemapper FindRemapper(VisualElement el, int depth)
    {
        if (depth > 6) return null;
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

    private static SI.Bindable.BindingRoot FindBindingRootBelow(VisualElement el, int depth)
    {
        if (depth > 8) return null;
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root != null) return root;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindBindingRootBelow(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static SI.Bindable.BindingRoot FindBindingRootAbove(VisualElement el)
    {
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent)
        {
            var root = p.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
        }
        return null;
    }
}

using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the Mentoring card's relationship diagram. Each group draws its three
/// players as nodes and the influence between every ordered pair as an arrow whose LINE STYLE
/// carries the strength (the graph key beside it reads None / Light / Average / Significant), so
/// the strength is invisible to a text walk. This dumps every arrow template's element chain, the
/// USS classes its style setter may have added, and its binding node, to establish whether the
/// strength is readable. GET /mentoringprobe. Read-only.
/// </summary>
internal static class MentoringProbe
{
    // The graph key's four container names, the most likely USS class names the arrow's style
    // setter applies, plus the variants a "line-" prefixed convention would use.
    private static readonly string[] CandidateClasses =
    {
        "none", "light", "average", "significant",
        "line-none", "line-light", "line-average", "line-significant",
        "line_none", "line_light", "line_average", "line_significant",
        "mentoring-none", "mentoring-light", "mentoring-average", "mentoring-significant",
    };

    internal static string Run()
    {
        // Every open panel, not just the highest layer: a Processing overlay sits above the card
        // whenever the game is ticking, and it holds no diagram.
        VisualElement panel = null;
        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        for (int i = 0; i < layers.Count && panel == null; i++)
        {
            var open = layers[i].Open;
            if (open == null) continue;
            for (int p = 0; p < open.Count; p++)
                if (open[p] != null && open[p].name == "MentoringCard") { panel = open[p]; break; }
        }
        if (panel == null) return "MentoringCard is not open\n";

        var diagram = FindByName(panel, "RelationshipDiagram", 0);
        if (diagram == null) return "no RelationshipDiagram on the Mentoring card\n";

        var sb = new StringBuilder();
        sb.Append("========== GRAPH KEY ==========\n");
        var key = FindByName(panel, "frame-graph-key", 0);
        if (key == null) sb.Append("no frame-graph-key\n");
        else
        {
            DumpTree(key, sb, 0, 6);
            DumpRemappers(key, sb, 0);
            foreach (var level in new[] { "none", "light", "average", "significant" })
            {
                var swatch = FindByName(key, level, 0);
                if (swatch == null) continue;
                sb.Append("  key ").Append(level).Append(':');
                var lineEl = FindByName(swatch, "line", 0);
                DumpLineStyle(lineEl ?? swatch, sb);
            }
        }

        sb.Append("========== PLAYERS ==========\n");
        foreach (var slot in new[] { "PlayerOne", "PlayerTwo", "PlayerThree", "PlayerFour", "PlayerFive" })
        {
            var node = FindByName(diagram, slot, 0);
            if (node == null) continue;
            sb.Append("--- ").Append(slot).Append('\n');
            DumpRemappers(node, sb, 0);
            var texts = new StringBuilder();
            CollectTexts(node, texts);
            sb.Append("  texts=").Append(texts).Append('\n');
        }

        sb.Append("========== ARROWS ==========\n");
        DumpArrows(diagram, sb);
        return sb.ToString();
    }

    private static void DumpArrows(VisualElement el, StringBuilder sb)
    {
        string name = el.name ?? "";
        if (name.StartsWith("RelationshipArrow"))
        {
            sb.Append("--- ").Append(name).Append(" visible=")
              .Append(ElementBridge.IsVisible(el)).Append('\n');
            DumpTree(el, sb, 1, 8);
            DumpRemappers(el, sb, 0);
            foreach (var part in new[] { "ArrowLine", "LineStyleSetter", "Line" })
            {
                var partEl = FindByName(el, part, 0);
                if (partEl == null) continue;
                sb.Append("  ").Append(part).Append(':');
                DumpLineStyle(partEl, sb);
            }
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            DumpArrows(el.hierarchy[i], sb);
    }

    private static void DumpTree(VisualElement el, StringBuilder sb, int depth, int max)
    {
        if (depth > max) return;
        string type;
        try { type = el.GetIl2CppType().Name; }
        catch { return; }

        sb.Append(new string(' ', depth * 2)).Append(type)
          .Append(" name=\"").Append(el.name).Append('"');
        foreach (var cls in CandidateClasses)
        {
            try { if (el.ClassListContains(cls)) sb.Append(" cls:").Append(cls); }
            catch { }
        }
        var text = el.TryCast<TextElement>();
        if (text != null && !string.IsNullOrEmpty(text.text)) sb.Append(" text=\"").Append(text.text).Append('"');
        sb.Append('\n');

        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            DumpTree(el.hierarchy[i], sb, depth + 1, max);
    }

    private static void CollectTexts(VisualElement el, StringBuilder sb)
    {
        var textEl = el.TryCast<TextElement>();
        if (textEl != null && !string.IsNullOrEmpty(textEl.text)) sb.Append(textEl.text).Append('|');
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], sb);
    }

    private static VisualElement FindByName(VisualElement el, string name, int depth)
    {
        if (depth > 60) return null;
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindByName(el.hierarchy[i], name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    // Every BindingRemapper / BindingVariables in the subtree, with its bound node's children.
    private static void DumpRemappers(VisualElement el, StringBuilder sb, int depth)
    {
        if (depth > 10) return;
        var remapper = el.TryCast<SI.Bindable.BindingRemapper>();
        if (remapper != null)
        {
            var key = remapper.Key;
            sb.Append("  remapper \"").Append(el.name).Append("\":\n");
            DumpChildren(ref key, sb);
        }
        var vars = el.TryCast<SI.Bindable.BindingVariables>();
        if (vars != null)
        {
            var key = vars.Key;
            sb.Append("  variables \"").Append(el.name).Append("\":\n");
            DumpChildren(ref key, sb);
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            DumpRemappers(el.hierarchy[i], sb, depth + 1);
    }

    private static void DumpChildren(ref SI.Bindable.Bindings.Key key, StringBuilder sb)
        => DumpChildren(ref key, sb, 2, 0);

    private static void DumpChildren(ref SI.Bindable.Bindings.Key key, StringBuilder sb, int maxDepth, int depth)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append("    no BindingSubsystem\n"); return; }
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append("    TryGetNode failed\n"); return; }
        string pad = new string(' ', 4 + depth * 2);
        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            var value = bindings.Get(ref childKey);
            sb.Append(pad).Append('"').Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
            if (depth < maxDepth) DumpChildren(ref childKey, sb, maxDepth, depth + 1);
        }
    }

    // For a ReferenceTypedValue wrapping a DynamicReference, list its properties (name = value).
    private static void DumpReference(SI.Core.TypedValue typedValue, StringBuilder sb)
    {
        try
        {
            if (typedValue == null) return;
            var reference = typedValue.Get();
            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;

            // The mentoring references are InteropReferences: a flat (property, int) set. Dump
            // every property the reference accepts, with its name from the identifier registry.
            var interop = reference?.TryCast<SI.Interop.InteropReference>();
            if (interop != null)
            {
                var props = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
                interop.GetProperties(props);
                for (int i = 0; i < props.Count; i++)
                {
                    uint id = (uint)props[i];
                    string name;
                    try { name = reg.GetInfo(id).Name; }
                    catch { name = "prop:" + id; }
                    string val = interop.TryGetProperty(id, out int v) ? v.ToString() : "absent";
                    sb.Append("      ref ").Append(name).Append(" = ").Append(val).Append('\n');
                }
                return;
            }

            var dynRef = reference?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;

            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                string name;
                try { name = reg.GetInfo((uint)key).Name; }
                catch { name = "key:" + key; }
                string val;
                try { val = dynRef[key]?.ToString() ?? "null"; }
                catch (System.Exception e) { val = "err:" + e.GetType().Name; }
                sb.Append("      ref ").Append(name).Append(" = ").Append(val).Append('\n');
            }
        }
        catch (System.Exception e) { sb.Append("      ref dump err:").Append(e.GetType().Name).Append('\n'); }
    }

    // The drawn line's colour and thickness, the two things the graph key distinguishes levels by.
    private static void DumpLineStyle(VisualElement line, StringBuilder sb)
    {
        try
        {
            var rs = line.resolvedStyle;
            sb.Append("  line bg=").Append(rs.backgroundColor.ToString())
              .Append(" color=").Append(rs.color.ToString())
              .Append(" borderTop=").Append(rs.borderTopWidth)
              .Append(" opacity=").Append(rs.opacity)
              .Append('\n');
        }
        catch (System.Exception e) { sb.Append("  line style err:").Append(e.GetType().Name).Append('\n'); }
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

    private static void DumpNode(SI.Bindable.BindingRoot root, StringBuilder sb)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append("  no BindingSubsystem\n"); return; }
        var rootKey = root.Key;
        if (!bindings.TryGetNode(ref rootKey, out var node)) { sb.Append("  TryGetNode failed\n"); return; }

        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            var value = bindings.Get(ref childKey);
            sb.Append("  child \"").Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
        }
    }
}

using System;
using System.Text;
using FM27Access.Plugin.Diagnostics;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the binding data behind a drawn widget: finds the first element of IL2CPP
/// type or exact native element name <c>type</c> on the rendered panels, then walks up its ancestors and, for every BindingRoot,
/// dumps the binding node tree (names, depth-limited) with each record-valued node's field names.
/// The recon step for reading a chart from the record it plots (see LastMatchCharts).
/// GET /nodeprobe?type=FMXGMatchStoryChart[&amp;nth=0][&amp;depth=3]. Read-only.
/// </summary>
internal static class NodeProbe
{
    internal static string Run(string type, int nth, int depth)
    {
        if (string.IsNullOrEmpty(type)) return "usage: /nodeprobe?type=<Il2CppTypeName>[&nth=0][&depth=3]\n";
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            int budget = 0, seen = 0;
            var el = Find(root, type, nth, ref seen, ref budget);
            if (el == null) continue;
            var sb = new StringBuilder();
            var up = el;
            for (int i = 0; i < 120 && up != null; i++, up = up.hierarchy.parent)
            {
                var node = BindingRecords.NodeOf(up);
                if (node == null) continue;
                sb.Append("=== ancestor ").Append(i).Append(' ').Append(up.GetIl2CppType().Name)
                  .Append(" \"").Append(up.name).Append("\"\n");
                try { Dump(node, sb, 1, depth); }
                catch (Exception e) { sb.Append("  dump threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
            }
            return sb.Length > 0 ? sb.ToString() : "found the element but no BindingRoot above it\n";
        }
        return $"no {type} (nth={nth}) on the rendered panels\n";
    }

    /// <summary>
    /// GET /subtree?name=Goals[&amp;nth=0][&amp;count=1]: the raw tree under the nth (and following
    /// count-1) elements NAMED <c>name</c> on the rendered panels — a window into one widget when the
    /// whole-panel /gui dump hits its node cap.
    /// </summary>
    internal static string Subtree(string name, int nth, int count)
    {
        if (string.IsNullOrEmpty(name)) return "usage: /subtree?name=<element name>[&nth=0][&count=1]\n";
        var sb = new StringBuilder();
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            var found = new System.Collections.Generic.List<VisualElement>();
            int budget = 0;
            CollectNamed(root, name, found, ref budget);
            for (int i = nth; i < found.Count && i < nth + count; i++)
            {
                sb.Append("=== ").Append(name).Append(" #").Append(i).Append(" of ").Append(found.Count).Append('\n');
                TreeDump.Write(found[i], sb, new TreeDump.Options());
                AppendSprites(found[i], sb, 0);
            }
        }
        return sb.Length > 0 ? sb.ToString() : $"no element named {name} (nth={nth}) on the rendered panels\n";
    }

    // Every drawn background image under el (an icon whose meaning is its sprite: a goal glyph's
    // count, a sub arrow's direction), which the tree dump does not show.
    private static void AppendSprites(VisualElement el, StringBuilder sb, int depth)
    {
        string sprite = Adapters.SpriteHelper.ReadSpriteName(el);
        if (sprite != null)
            sb.Append("  sprite @depth ").Append(depth).Append(' ').Append(el.GetIl2CppType().Name)
              .Append(" \"").Append(el.name).Append("\" -> ").Append(sprite).Append('\n');
        // GetClasses() does not enumerate under IL2CPP interop; the resolved colour is what is drawn.
        if (el.GetIl2CppType().Name == "SIText")
            sb.Append("  color @depth ").Append(depth).Append(" SIText \"").Append(el.name).Append("\" ")
              .Append(el.resolvedStyle.color.ToString()).Append('\n');
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            AppendSprites(el.hierarchy[i], sb, depth + 1);
    }

    private static void CollectNamed(VisualElement el, string name, System.Collections.Generic.List<VisualElement> into, ref int budget)
    {
        if (budget++ > 60000) return;
        if (el.name == name) into.Add(el);
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectNamed(el.hierarchy[i], name, into, ref budget);
    }

    private static VisualElement Find(VisualElement el, string type, int nth, ref int seen, ref int budget)
    {
        if (budget++ > 40000) return null;
        if ((el.GetIl2CppType().Name == type || el.name == type) && seen++ == nth) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = Find(el.hierarchy[i], type, nth, ref seen, ref budget);
            if (found != null) return found;
        }
        return null;
    }

    private static void Dump(SI.Bindable.IReadOnlyNode node, StringBuilder sb, int indent, int depth)
    {
        if (depth < 0) return;
        foreach (var child in node.Children)
        {
            sb.Append(' ', indent * 2).Append(child.Name);
            try
            {
                var value = BindingRecords.ValueOf(child);
                var rec = BindingRecords.RecordOf(value);
                if (rec != null)
                    sb.Append("  [record: ").Append(string.Join(", ", BindingRecords.Fields(rec).Keys)).Append(']');
                else if (value != null)
                {
                    string text = value.AsString();
                    if (text != null) sb.Append(" = ").Append(text.Length > 240 ? text.Substring(0, 240) + "..." : text);
                    var reference = value.Get()?.TryCast<SI.Interop.InteropReference>();
                    if (reference?.m_id != null)
                    {
                        sb.Append(" [native ").Append(reference.GetIl2CppType().FullName).Append(']');
                        for (int i = 0; i < reference.m_id.Count; i++)
                        {
                            var pair = reference.m_id[i];
                            var identifiers = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
                            string name = identifiers.HasID(pair.ID) ? identifiers.GetInfo(pair.ID).Name : "id:" + pair.ID;
                            sb.Append(" {").Append(name)
                                .Append('=').Append(pair.Value).Append('}');
                        }
                    }
                }
            }
            catch (Exception e) { sb.Append("  (value threw ").Append(e.GetType().Name).Append(')'); }
            sb.Append('\n');
            Dump(child, sb, indent + 1, depth - 1);
        }
    }
}

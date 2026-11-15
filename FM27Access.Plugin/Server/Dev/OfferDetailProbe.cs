using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-driver probe for the Transfer Activity offer-detail block, which draws a run of bare values
/// with no labels beside them: the club, the division, the positions, a role-shaped word, the wage
/// range, and a date. Three of those are provable from element names
/// (<c>description-text</c>, <c>SIVisible("HasDivision")</c>, the club combo cell); two are not.
///
/// This probe exists to settle what the last two ARE before any mod-authored label is written for
/// them. Labelling "Target Forward" as a role or "6/30/2026" as a contract expiry on the strength
/// of where they sit in the layout would be a guess, and a wrong label presents wrong data as real
/// — the one thing the surface must never do. So: read the binding each text hangs under and print
/// the property names the game itself uses.
///
///   GET /offerprobe            every text leaf in the detail block, with its bound property name
///   GET /offerprobe?op=props   every property on the block's binding node, name and value
///
/// Read-only.
/// </summary>
internal static class OfferDetailProbe
{


    internal static string Drive(string op)
    {
        try
        {
            var block = FindDetailBlock();
            if (block == null)
                return "Offer detail block not found. Open Recruitment > Transfer Activity with an offer selected.";
            return op == "props" ? Props(block) : Leaves(block);
        }
        catch (Exception ex)
        {
            return $"probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    // The block is the contract/wages half of the detail card. Anchor on the wages frame, which is
    // the one named ancestor both unlabelled values share.
    private static VisualElement FindDetailBlock()
    {
        if (FM27Access.Navigation.NavigationManager.GetAllOpenPanels() is not { } openPanels) return null;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            var found = FindNamed(panel, "Frame_Wages", 0);
            if (found != null) return found.hierarchy.parent?.hierarchy.parent ?? found;
        }
        return null;
    }

    // Every text leaf under the block, with the binding property its nearest remapper carries.
    private static string Leaves(VisualElement block)
    {
        var sb = new StringBuilder();
        sb.Append("========== OFFER DETAIL LEAVES ==========\n");
        var texts = new List<VisualElement>();
        CollectTexts(block, texts, 0);
        foreach (var t in texts)
        {
            string text = ScreenWalker.ReadFirstText(t);
            if (string.IsNullOrWhiteSpace(text)) continue;
            sb.Append("  text=\"").Append(Trim(text)).Append("\"")
              .Append("  name=\"").Append(t.name ?? "").Append("\"")
              .Append("  ancestors=").Append(Ancestry(t, 5)).Append('\n');
        }
        sb.Append("\nRun ?op=props for the block's bound property names.\n");
        return sb.ToString();
    }

    // Every property on the nearest binding node above the block: the names the game itself uses.
    // This is the evidence a label rests on — a property called ContractExpiryDate proves the date;
    // anything vaguer means the value stays unlabelled.
    private static string Props(VisualElement block)
    {
        var sb = new StringBuilder();
        sb.Append("========== OFFER DETAIL BINDING PROPERTIES ==========\n");
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return sb.Append("  binding subsystem unavailable.\n").ToString();

        // Walk up: the wages/contract frames carry no remapper of their own, so the node that names
        // these values sits on an ancestor. Print every one on the way to the panel root — the
        // nearest is usually the answer, but the enclosing card's node is what names the record.
        var cur = block;
        int level = 0;
        bool any = false;
        while (cur != null && level < 20)
        {
            var remapper = cur.TryCast<SI.Bindable.BindingRemapper>();
            if (remapper != null)
            {
                var key = remapper.Key;
                sb.Append("--- remapper ").Append(level).Append(" (")
                  .Append(string.IsNullOrEmpty(cur.name) ? "unnamed" : cur.name).Append(") ---\n");
                if (bindings.TryGetNode(ref key, out var node))
                {
                    var reference = bindings.Get(ref key)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                    if (reference != null) { AppendProperties(sb, reference, "  "); any = true; }
                    else
                        // The node is a container: each child is its own binding. Resolve each into
                        // a reference so the record's real property names surface.
                        foreach (var child in node.Children)
                        {
                            sb.Append("    child=").Append(child.Name).Append('\n');
                            var childKey = child.Key;
                            var childRef = bindings.Get(ref childKey)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                            if (childRef == null) continue;
                            AppendProperties(sb, childRef, "      ");
                            any = true;
                        }
                }
                else sb.Append("    key resolves to no node.\n");
            }
            cur = cur.hierarchy.parent;
            level++;
        }
        if (!any) sb.Append("  no DynamicReference found on any ancestor remapper.\n");
        return sb.ToString();
    }

    private static void AppendProperties(StringBuilder sb, SI.Bindable.DynamicReference reference, string indent)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var keys = reference.Keys.GetEnumerator();
        while (keys.MoveNext())
        {
            var key = keys.Current;
            string name;
            try { name = reg.GetInfo((uint)key).Name; }
            catch { continue; }
            string value;
            try { value = reference[key]?.AsString() ?? "(null)"; }
            catch (Exception ex) { value = $"(unreadable: {ex.GetType().Name})"; }
            sb.Append(indent).Append(name).Append(" = ").Append(Trim(value)).Append('\n');
        }
    }

    private static void CollectTexts(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > 12) return;
        if (el.TryCast<SI.Bindable.SIText>() != null) { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectTexts(el.hierarchy[i], into, depth + 1);
    }

    private static string Ancestry(VisualElement el, int levels)
    {
        var parts = new List<string>();
        var cur = el.hierarchy.parent;
        for (int i = 0; i < levels && cur != null; i++)
        {
            string n = cur.name;
            parts.Add(string.IsNullOrEmpty(n) ? cur.GetIl2CppType().Name : $"{cur.GetIl2CppType().Name}(\"{n}\")");
            cur = cur.hierarchy.parent;
        }
        return string.Join(" < ", parts);
    }

    private static VisualElement FindNamed(VisualElement el, string name, int depth)
    {
        if (depth > 80) return null;
        if (string.Equals(el.name, name, StringComparison.Ordinal)) return el;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindNamed(el.hierarchy[i], name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static SI.Bindable.BindingRemapper FindRemapper(VisualElement el, int depth)
    {
        if (depth > 10) return null;
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

    private static string Trim(string s) =>
        s.Length <= 90 ? s.Replace("\n", " ") : s.Substring(0, 90).Replace("\n", " ") + "…";
}

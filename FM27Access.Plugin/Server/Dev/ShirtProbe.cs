using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the tactics pitch shirts' binding model. The collapsed Team Selection
/// pitch binds shirt numbers but leaves the name text blank, so this dumps each occupied
/// FMPitchIndicator's BindingRoot node — child names, typed values, and any DynamicReference
/// properties — to establish where an accessible player identity can be read from.
/// GET /shirtprobe. Read-only.
/// </summary>
internal static class ShirtProbe
{
    internal static string Run()
    {
        var pitch = FindPitch();
        if (pitch == null) return "no FMTacticsWithAnalysis on the active panel\n";

        var sb = new StringBuilder();
        Collect(pitch, sb);

        // Set For segmented buttons: element name + USS classes, to learn what flips on click.
        sb.Append("========== SET FOR ==========\n");
        var seg = FindByAncestorName(pitch);
        if (seg != null)
        {
            DumpSegment(seg, sb, 0);
        }
        else sb.Append("no setforContainer found\n");

        // Second section: the squad-table rows' demoted name buttons and their row binding
        // node, to confirm each row exposes the same team-selection Index as the pitch slots.
        sb.Append("========== NAME BUTTONS ==========\n");
        var root = pitch;
        while (root.hierarchy.parent != null) root = root.hierarchy.parent;
        int budget = 0;
        CollectNameButtons(root, sb, ref budget);
        return sb.Length > 0 ? sb.ToString() : "no FMPitchIndicator with a BindingRoot found\n";
    }

    private static void CollectNameButtons(VisualElement el, StringBuilder sb, ref int budget)
    {
        if (budget++ > 20000) return;
        string type = null;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "SIButton" && el.name == "SIButton")
        {
            var texts = new StringBuilder();
            CollectTexts(el, texts);
            bool demoted = ElementBridge.HasSiblingSubtreeWithDropdown(el);
            sb.Append("--- button demoted=").Append(demoted)
              .Append(" texts=\"").Append(texts).Append("\"\n");
            var rowRoot = FindAncestorBindingRoot(el);
            if (rowRoot == null) { sb.Append("  no ancestor BindingRoot\n"); return; }
            DumpNode(rowRoot, sb);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectNameButtons(el.hierarchy[i], sb, ref budget);
    }

    private static VisualElement FindByAncestorName(VisualElement any)
    {
        var root = any;
        while (root.hierarchy.parent != null) root = root.hierarchy.parent;
        return PitchGridFind(root, "setforContainer", 0);
    }

    private static VisualElement PitchGridFind(VisualElement el, string name, int depth)
    {
        if (depth > 90) return null;
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = PitchGridFind(el.hierarchy[i], name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static void DumpSegment(VisualElement el, StringBuilder sb, int depth)
    {
        if (depth > 8) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        var texts = new StringBuilder();
        CollectTexts(el, texts);
        sb.Append(new string(' ', depth * 2)).Append(type).Append(" name=\"").Append(el.name)
          .Append("\" enabled=").Append(el.enabledSelf);
        if (type == "SIButton" || type == "SIStyleSetter")
        {
            try { sb.Append(" bg=").Append(el.resolvedStyle.backgroundColor.ToString()); }
            catch { sb.Append(" bg=err"); }
            foreach (var cls in new[] { "selected", "active", "checked", "toggled",
                "buttons-segmented-button-row-active-regular", "buttons-segmented-button-row-inactive-regular" })
            {
                try { if (el.ClassListContains(cls)) sb.Append(" cls:").Append(cls); }
                catch { }
            }
        }
        if (type == "SIButton") sb.Append(" texts=\"").Append(texts).Append('"');
        sb.Append('\n');
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            DumpSegment(el.hierarchy[i], sb, depth + 1);
    }

    private static void CollectTexts(VisualElement el, StringBuilder sb)
    {
        var textEl = el.TryCast<TextElement>();
        if (textEl != null && !string.IsNullOrEmpty(textEl.text)) sb.Append(textEl.text).Append('|');
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], sb);
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

    private static void Collect(VisualElement el, StringBuilder sb)
    {
        if (el.GetIl2CppType().Name == "FMPitchIndicator")
        {
            sb.Append("=== slot ").Append(el.name).Append(" ===\n");
            var root = FindBindingRoot(el, 0);
            if (root == null) { sb.Append("  no BindingRoot\n"); return; }
            DumpNode(root, sb);
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            Collect(el.hierarchy[i], sb);
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
            DumpReference(value, sb);
        }
    }

    // For a ReferenceTypedValue wrapping a DynamicReference, list its properties (name = value).
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
                string val;
                try { val = dynRef[key]?.ToString() ?? "null"; }
                catch (System.Exception e) { val = "err:" + e.GetType().Name; }
                sb.Append("    ref ").Append(name).Append(" = ").Append(val).Append('\n');
            }
        }
        catch (System.Exception e)
        {
            sb.Append("    ref dump failed: ").Append(e.GetType().Name).Append('\n');
        }
    }

    private static VisualElement FindPitch()
    {
        try
        {
            // Search every interactive panel, not just the highest layer: on Team Selection the
            // title bar shares the layer with the content panel that holds the pitch.
            var panels = InteractivePanels.CurrentOrNull();
            if (panels == null) return null;
            foreach (var pv in panels)
            {
                var found = Find(pv.Panel as VisualElement, 0);
                if (found != null) return found;
            }
            return null;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[ShirtProbe] pitch lookup failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static VisualElement Find(VisualElement el, int depth)
    {
        if (el == null || depth > 80) return null;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return null; }
        if (type == "FMTacticsWithAnalysis") return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = Find(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}

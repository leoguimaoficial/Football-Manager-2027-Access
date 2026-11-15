using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-driver probe for ANY <c>SIFloatingPopupWidget</c> in an open panel — the game's generic
/// trigger-plus-floating-content widget (table Filter popups, the bookmark editor, ...). The
/// floating part attaches outside the owning panel, so neither mod surface sees it until a
/// feature bridges it; this endpoint is how an agent scouts a popup before that bridge exists.
/// BookmarkPopupProbe is the bookmark-specific ancestor of this probe.
///
///   GET /popupprobe                        list widgets, indexed (panel, name, label text)
///   GET /popupprobe?op=open&amp;index=N        widget's own click entry (init + toggle)
///   GET /popupprobe?op=state&amp;index=N       floating element state + attachment ancestry
///   GET /popupprobe?op=dump&amp;index=N        raw tree of the floating root
///   GET /popupprobe?op=rows&amp;index=N        clickable candidates inside the popup, indexed
///   GET /popupprobe?op=click&amp;index=N&amp;row=M activate candidate M (the game's own flow)
///   GET /popupprobe?op=close&amp;index=N       hide the floating part
/// </summary>
internal static class FloatingPopupProbe
{
    internal static string Drive(string op, string indexStr, string rowStr)
    {
        try
        {
            if (string.IsNullOrEmpty(op) || op == "list") return List();
            if (!int.TryParse(indexStr, out int index))
                return "ops need &index=N (from op=list): open | state | dump | rows | click&row=M | close";
            var widget = FindWidgets()[index].Widget;
            switch (op)
            {
                case "open": widget.HandleFloatingPart(); return "HandleFloatingPart called. Poll op=state, then op=dump.";
                case "close": widget.HideFloatingPart(); return "HideFloatingPart called.";
                case "state": return State(widget);
                case "dump": return Dump(widget);
                case "rows": return Rows(widget);
                case "click": return Click(widget, rowStr);
                default: return "ops: list | open | state | dump | rows | click&row=M | close (all but list need &index=N)";
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return "index out of range; re-run op=list (widget indexes shift when panels change).";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static string List()
    {
        var widgets = FindWidgets();
        if (widgets.Count == 0) return "No SIFloatingPopupWidget in any open panel.";
        var sb = new StringBuilder();
        for (int i = 0; i < widgets.Count; i++)
        {
            var (panelName, widget) = widgets[i];
            sb.AppendLine($"[{i}] panel={panelName} name=\"{widget.name}\" visible={ElementBridge.IsVisible(widget)}"
                + $" label=\"{LabelText(widget)}\" open={IsOpen(widget)}");
        }
        return sb.ToString();
    }

    private static bool IsOpen(SI.Bindable.SIFloatingPopupWidget widget)
        => widget.m_floatingElement is { } fe && fe.IsVisible;

    // The trigger's visible caption (e.g. "Filter"): the first non-empty SIText under the widget
    // itself. The floating part lives elsewhere, so this walk stays tiny.
    private static string LabelText(VisualElement widget)
    {
        var texts = new List<string>();
        CollectTexts(widget, texts, 0);
        return texts.Count > 0 ? texts[0] : "";
    }

    private static void CollectTexts(VisualElement el, List<string> texts, int depth)
    {
        if (depth > 10 || texts.Count > 0) return;
        var text = el.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text)) { texts.Add(text.Trim()); return; }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            CollectTexts(el.hierarchy[i], texts, depth + 1);
    }

    private static string State(SI.Bindable.SIFloatingPopupWidget widget)
    {
        var sb = new StringBuilder();
        var fe = widget.m_floatingElement;
        sb.AppendLine($"widget: name={widget.name} visible={ElementBridge.IsVisible(widget)}");
        sb.AppendLine($"floatingElement: {(fe == null ? "null" : $"IsVisible={fe.IsVisible}")}");
        var content = widget.m_floatingPartContent;
        sb.AppendLine($"floatingPartContent: {(content == null ? "null" : $"name={content.name} children={content.hierarchy.childCount} inPanel={content.panel != null}")}");

        var root = fe?.Root;
        if (root == null) { sb.AppendLine("floating root: null"); return sb.ToString(); }
        sb.AppendLine($"floating root: name={root.name} type={root.GetIl2CppType().Name} inPanel={root.panel != null}");
        var cur = root.hierarchy.parent;
        int depth = 0;
        while (cur != null && depth < 40)
        {
            sb.AppendLine($"  ancestor[{depth}]: type={cur.GetIl2CppType().Name} name=\"{cur.name}\"");
            cur = cur.hierarchy.parent;
            depth++;
        }
        return sb.ToString();
    }

    private static string Dump(SI.Bindable.SIFloatingPopupWidget widget)
    {
        var root = widget.m_floatingElement?.Root;
        if (root == null) return "Floating root is null (popup never opened this session?).";
        var sb = new StringBuilder();
        Diagnostics.TreeDump.Write(root, sb, new Diagnostics.TreeDump.Options());
        return sb.ToString();
    }

    private static string Rows(SI.Bindable.SIFloatingPopupWidget widget)
    {
        var candidates = CollectCandidates(widget);
        if (candidates == null) return "Floating root is null (open the popup first).";
        var sb = new StringBuilder();
        for (int i = 0; i < candidates.Count; i++)
        {
            var el = candidates[i];
            string label, state;
            try
            {
                var adapter = ElementBridge.CreateAdapter(el, Classify(el));
                label = adapter.GetLabel();
                state = adapter.GetState();
            }
            catch (Exception e) { label = $"<label failed: {e.GetType().Name}>"; state = ""; }
            sb.AppendLine($"[{i}] type={el.GetIl2CppType().Name} name=\"{el.name}\" label=\"{label}\" state=\"{state}\" visible={ElementBridge.IsVisible(el)}");
        }
        return sb.Length == 0 ? "No clickable candidates found." : sb.ToString();
    }

    private static string Click(SI.Bindable.SIFloatingPopupWidget widget, string rowStr)
    {
        if (!int.TryParse(rowStr, out int row)) return "click needs &row=M";
        var candidates = CollectCandidates(widget);
        if (candidates == null) return "Floating root is null (open the popup first).";
        if (row < 0 || row >= candidates.Count) return $"row out of range, {candidates.Count} candidates.";
        var el = candidates[row];
        var adapter = ElementBridge.CreateAdapter(el, Classify(el));
        var label = adapter.GetLabel();
        adapter.Activate();
        return $"Clicked [{row}] \"{label}\".";
    }

    private static FM27Access.Navigation.ElementClassification Classify(VisualElement el)
    {
        return el.GetIl2CppType().Name switch
        {
            "SICheckBox" => FM27Access.Navigation.ElementClassification.Checkbox,
            "SIDropdown" or "SIMenu" => FM27Access.Navigation.ElementClassification.Dropdown,
            _ => FM27Access.Navigation.ElementClassification.Button,
        };
    }

    private static List<VisualElement> CollectCandidates(SI.Bindable.SIFloatingPopupWidget widget)
    {
        var root = widget.m_floatingElement?.Root;
        if (root == null) return null;
        var list = new List<VisualElement>();
        Collect(root, list);
        return list;
    }

    private static readonly string[] ClickableTypes = { "SIButton", "SICheckBox", "SIRadioButton", "SIDropdown", "SIMenu", "SISlider", "SIStepper" };

    private static void Collect(VisualElement el, List<VisualElement> list)
    {
        var typeName = el.GetIl2CppType().Name;
        foreach (var t in ClickableTypes)
        {
            if (typeName == t) { list.Add(el); break; }
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            Collect(el.hierarchy[i], list);
    }

    private static List<(string PanelName, SI.Bindable.SIFloatingPopupWidget Widget)> FindWidgets()
    {
        var result = new List<(string, SI.Bindable.SIFloatingPopupWidget)>();
        if (FM27Access.Navigation.NavigationManager.GetAllOpenPanels() is not { } openPanels) return result;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            CollectWidgets(panel.name, panel, result);
        }
        return result;
    }

    private static void CollectWidgets(string panelName, VisualElement el,
        List<(string, SI.Bindable.SIFloatingPopupWidget)> result)
    {
        var widget = el.TryCast<SI.Bindable.SIFloatingPopupWidget>();
        if (widget != null && el.GetIl2CppType().Name == "SIFloatingPopupWidget")
        {
            result.Add((panelName, widget));
            return;
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            CollectWidgets(panelName, el.hierarchy[i], result);
    }
}

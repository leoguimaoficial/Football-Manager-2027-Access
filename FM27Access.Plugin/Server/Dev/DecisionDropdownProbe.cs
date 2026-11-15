using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-driver probe for the contract-decision split-button dropdown — an SIDropdown whose closed
/// face carries its own SIButton ("GenericContractDecisionButton") instead of a plain label. The
/// contract-expiry messages ("US Orléans player/youth contracts set to expire") build their
/// Recommended Action column out of these, and the mod's ordinary confirm path
/// (SIDropdown.TriggerSelectionEvent) moves nothing on them: SelectedIndex is unchanged after
/// every attempt while the surface reports success.
///
/// This probe exists to settle WHY before any confirm logic is written for them. It lists every
/// SIDropdown in the open panels so an ordinary one can be compared field-for-field against a
/// decision one, and it can drive the two candidate commit paths separately — clicking a popup
/// row (op=click) versus clicking the face button (op=face).
///
///   GET /decisionprobe                          list all SIDropdowns, decision ones flagged
///   GET /decisionprobe?op=state&amp;index=N         SelectedIndex, event lists, bindings, face text
///   GET /decisionprobe?op=open&amp;index=N          ShowDropdown (the game's own entry)
///   GET /decisionprobe?op=dump&amp;index=N          raw tree of the open popup's content
///   GET /decisionprobe?op=rows&amp;index=N          clickable candidates inside the popup, indexed
///   GET /decisionprobe?op=click&amp;index=N&amp;row=M   activate popup candidate M
///   GET /decisionprobe?op=face&amp;index=N          activate the face's own button
///   GET /decisionprobe?op=close&amp;index=N         HideDropdown
///
/// Read-only except for the deliberate op=click / op=face, which commit real game decisions.
/// </summary>
internal static class DecisionDropdownProbe
{
    private const string FaceButtonName = "GenericContractDecisionButton";

    internal static string Drive(string op, string indexStr, string rowStr)
    {
        try
        {
            if (string.IsNullOrEmpty(op) || op == "list") return List();
            if (!int.TryParse(indexStr, out int index))
                return "ops need &index=N (from the bare listing): state | open | dump | rows | click&row=M | face | close";
            var found = Find();
            if (index < 0 || index >= found.Count) return $"index out of range, {found.Count} dropdowns.";
            var el = found[index].Element;
            var dropdown = el.TryCast<SI.Bindable.SIDropdown>();
            if (dropdown == null) return "That element is not an SIDropdown any more; re-run the listing.";

            switch (op)
            {
                case "state": return State(el, dropdown);
                case "open": dropdown.ShowDropdown(); return "ShowDropdown called. Now op=dump or op=rows.";
                case "close": dropdown.HideDropdown(); return "HideDropdown called.";
                case "dump": return Dump(dropdown);
                case "rows": return Rows(dropdown);
                case "click": return Click(dropdown, rowStr);
                case "face": return Face(el);
                default: return "ops: list | state | open | dump | rows | click&row=M | face | close";
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static string List()
    {
        var found = Find();
        if (found.Count == 0) return "No SIDropdown in any open panel.";
        var sb = new StringBuilder();
        for (int i = 0; i < found.Count; i++)
        {
            var (panelName, el) = found[i];
            var face = FindDescendantNamed(el, FaceButtonName);
            sb.AppendLine($"[{i}] {(face != null ? "DECISION" : "plain   ")} panel={panelName}"
                + $" name=\"{el.name}\" face=\"{FirstText(el)}\" visible={ElementBridge.IsVisible(el)}");
        }
        return sb.ToString();
    }

    // Every field that could plausibly decide whether a selection commits, printed for a decision
    // dropdown and an ordinary one side by side. Each read is guarded on its own: one unreadable
    // field must not cost the rest of the report.
    private static string State(VisualElement el, SI.Bindable.SIDropdown dropdown)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name=\"{el.name}\" face=\"{FirstText(el)}\" enabledInHierarchy={el.enabledInHierarchy}");
        Line(sb, "SelectedIndex", () => dropdown.SelectedIndex.ToString());
        Line(sb, "SelectedValue", () => Describe(dropdown.SelectedValue));
        Line(sb, "SelectionEvents", () => DescribeEvents(dropdown.SelectionEvents));
        Line(sb, "LeftClickEvents", () => DescribeEvents(dropdown.LeftClickEvents));
        Line(sb, "SelectionValueBinding", () => dropdown.SelectionValueBinding.ToString());
        Line(sb, "HideOnSelectionChange", () => dropdown.HideOnSelectionChange.ToString());
        Line(sb, "AlwaysUpdateInternalSelection", () => dropdown.AlwaysUpdateInternalSelection.ToString());
        Line(sb, "TriggerSelectionEventOnInit", () => dropdown.TriggerSelectionEventOnInit.ToString());

        var face = FindDescendantNamed(el, FaceButtonName);
        sb.AppendLine(face == null
            ? "face button: none (ordinary dropdown)"
            : $"face button: name=\"{face.name}\" text=\"{FirstText(face)}\" enabled={face.enabledInHierarchy}");
        if (face?.TryCast<SI.Bindable.SIButton>() is { } faceButton)
            Line(sb, "  face Events", () => DescribeEvents(faceButton.Events));
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string label, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = $"<unreadable: {e.GetType().Name}>"; }
        sb.AppendLine($"{label}: {value}");
    }

    private static string Describe(SI.Core.TypedValue value)
        => value == null ? "null" : $"{value.GetIl2CppType().Name} \"{value}\"";

    // An EventList's length is the signal that matters here: a dropdown whose SelectionEvents are
    // empty has nothing for TriggerSelectionEvent to fire, which would explain a silent no-op.
    private static string DescribeEvents(SI.Bindable.EventList events)
    {
        if (events == null) return "null";
        var items = events.Events;
        if (items == null) return "EventList with null Events";
        var sb = new StringBuilder($"{items.Count} event(s)");
        for (int i = 0; i < items.Count; i++)
            sb.Append($" [{i}]={items[i]?.GetIl2CppType().Name ?? "null"}");
        return sb.ToString();
    }

    private static string Dump(SI.Bindable.SIDropdown dropdown)
    {
        var content = PopupContent(dropdown);
        if (content == null) return "Popup content not found (open it first with op=open).";
        var sb = new StringBuilder();
        Diagnostics.TreeDump.Write(content, sb, new Diagnostics.TreeDump.Options());
        return sb.ToString();
    }

    private static string Rows(SI.Bindable.SIDropdown dropdown)
    {
        var candidates = Candidates(dropdown);
        if (candidates == null) return "Popup content not found (open it first with op=open).";
        var sb = new StringBuilder();
        for (int i = 0; i < candidates.Count; i++)
        {
            var el = candidates[i];
            sb.AppendLine($"[{i}] type={el.GetIl2CppType().Name} name=\"{el.name}\" text=\"{FirstText(el)}\""
                + $" visible={ElementBridge.IsVisible(el)} enabled={el.enabledInHierarchy}");
        }
        return sb.Length == 0 ? "No clickable candidates inside the popup." : sb.ToString();
    }

    private static string Click(SI.Bindable.SIDropdown dropdown, string rowStr)
    {
        if (!int.TryParse(rowStr, out int row)) return "click needs &row=M";
        var candidates = Candidates(dropdown);
        if (candidates == null) return "Popup content not found (open it first with op=open).";
        if (row < 0 || row >= candidates.Count) return $"row out of range, {candidates.Count} candidates.";
        var el = candidates[row];
        string label = FirstText(el);
        int before = dropdown.SelectedIndex;
        ElementBridge.CreateAdapter(el, FM27Access.Navigation.ElementClassification.Button).Activate();
        return $"Clicked popup row [{row}] \"{label}\". SelectedIndex {before} -> {dropdown.SelectedIndex}"
            + " (re-read op=state after a frame; the game may apply it asynchronously).";
    }

    private static string Face(VisualElement el)
    {
        var face = FindDescendantNamed(el, FaceButtonName);
        if (face == null) return "This dropdown has no face button.";
        string label = FirstText(face);
        ElementBridge.CreateAdapter(face, FM27Access.Navigation.ElementClassification.Button).Activate();
        return $"Clicked face button \"{label}\".";
    }

    // The popup's rows live under the dropdown's streamed list, which is a VisualElement subtree
    // rather than a floating element — the same route DropdownAdapter.ReadChoices walks.
    private static VisualElement PopupContent(SI.Bindable.SIDropdown dropdown)
    {
        var list = dropdown.m_streamedDropdownList;
        if (list == null) return null;
        return FindDescendantNamed(list, "unity-content-container") ?? list.TryCast<VisualElement>();
    }

    private static List<VisualElement> Candidates(SI.Bindable.SIDropdown dropdown)
    {
        var content = PopupContent(dropdown);
        if (content == null) return null;
        var list = new List<VisualElement>();
        Collect(content, list);
        return list;
    }

    private static readonly string[] ClickableTypes =
        { "SIButton", "SICheckBox", "SIRadioButton", "DropdownRowNavigatable" };

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

    private static VisualElement FindDescendantNamed(VisualElement el, string name)
    {
        if (el.name == name) return el;
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var hit = FindDescendantNamed(el.hierarchy[i], name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string FirstText(VisualElement el)
    {
        var text = el.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var hit = FirstText(el.hierarchy[i]);
            if (hit.Length > 0) return hit;
        }
        return "";
    }

    private static List<(string PanelName, VisualElement Element)> Find()
    {
        var result = new List<(string, VisualElement)>();
        if (FM27Access.Navigation.NavigationManager.GetAllOpenPanels() is not { } openPanels) return result;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            CollectDropdowns(panel.name, panel, result);
        }
        return result;
    }

    private static void CollectDropdowns(string panelName, VisualElement el,
        List<(string, VisualElement)> result)
    {
        if (el.GetIl2CppType().Name == "SIDropdown")
        {
            result.Add((panelName, el));
            return;
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            CollectDropdowns(panelName, el.hierarchy[i], result);
    }
}

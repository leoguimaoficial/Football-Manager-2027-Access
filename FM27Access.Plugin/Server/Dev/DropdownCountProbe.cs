using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only: every <c>SIDropdown</c> on the active panel with the game's own count of its choices
/// and its selected index, read through the mod's typed reflection.
///
/// The question this answers is whether a dropdown the surface renders with few options actually
/// HAS few options. The popup's row list is virtualised like any other, so a harvest that only sees
/// the realised window would silently under-report the menu — and an option the player never learns
/// exists is indistinguishable from one the game does not offer. Comparing ChoicesCount against what
/// /dropdown/open yields separates the two.
///
/// GET /ddprobe. Read-only: no popups opened, no selection changed.
/// </summary>
internal static class DropdownCountProbe
{
    internal static string Run()
    {
        if (!SI.Bindable.PanelManager.Exists()) return "PanelManager not available\n";

        var sb = new StringBuilder();
        int seen = 0;

        // Every layer, not just the active panel: an open dropdown's popup — and a submenu's popup
        // in particular — lives outside the panel that owns the control, so an active-panel-only
        // walk cannot see the very lists most likely to be truncated.
        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        for (int l = 0; l < layers.Count; l++)
        {
            var open = layers[l].Open;
            if (open == null) continue;
            for (int p = 0; p < open.Count; p++)
            {
                var panel = open[p];
                if (panel is not VisualElement root) continue;

                var found = new List<VisualElement>();
                Collect(root, found, 0);
                if (found.Count == 0) continue;

                sb.Append("========== ").Append(layers[l].Layer).Append(" / ").Append(panel.name)
                  .Append(" (").Append(found.Count).Append(") ==========\n");
                foreach (var el in found) { Describe(el, sb); seen++; }
            }
        }

        return seen == 0 ? "no SIDropdown in any open panel\n" : sb.ToString();
    }

    private static void Describe(VisualElement el, StringBuilder sb)
    {
        sb.Append("  name=\"").Append(el.name).Append('"')
          .Append(" value=\"").Append(FirstText(el, 0) ?? "").Append('"');
        try
        {
            var dropdown = el.Cast<SI.Bindable.SIDropdown>();
            sb.Append(" selectedIndex=").Append(Adapters.SIDropdownReflection.SelectedIndex.GetValue(dropdown))
              .Append(" choicesCount=").Append(Adapters.SIDropdownReflection.ChoicesCount.GetValue(dropdown));
        }
        catch (Exception e)
        {
            sb.Append(" <counts unreadable: ").Append(e.GetType().Name).Append('>');
        }

        sb.Append('\n');
    }

    // DELIBERATELY NOT READ: the dropdown's own popup list (m_streamedDropdownList). Reading it
    // across every dropdown on every layer wedged the game's main thread hard enough to take the
    // whole dev server with it (the request thread blocks on a main-thread call that never
    // returns, so even /health dies). That matches the known "some property reads wedge the main
    // thread" hazard, and a closed dropdown has no popup worth poking anyway.
    //
    // The truncation question does not need it. ChoicesCount above is the game's own total; compare
    // it against what /dropdown/open harvests (DropdownAdapter.ReadChoices walks only the REALISED
    // rows). Fewer harvested than ChoicesCount is truncation, proven without touching the popup.

    private static void Collect(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > 90) return;
        if (el.GetIl2CppType().Name == "SIDropdown") { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], into, depth + 1);
    }

    // The dropdown's displayed value, for matching a row here to the select on the page.
    private static string FirstText(VisualElement el, int depth)
    {
        if (depth > 8) return null;
        if (el.GetIl2CppType().Name == "SIText")
        {
            try
            {
                var t = el.TryCast<TextElement>()?.text;
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch { return null; }
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FirstText(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}

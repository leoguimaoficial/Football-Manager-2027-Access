using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Diagnostics;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// The dev driver's structural views, returned inline over HTTP. Both are the RAW
/// VisualElement tree (see <see cref="TreeDump"/>) so an agent can see what the cleaned /body
/// surface hides and reverse-engineer a screen; /eval then gives depth on whatever it reveals.
///   /gui    - the active panel only (what the player is on); when the cursor's active panel is
///             transiently null, falls back to the topmost interactive panel so it never goes blank.
///   /panels - every open panel across all PanelManager layers (the ribbon, tooltips, overlays,
///             modals), including the blocked/non-interactive ones that /gui (active panel only) and
///             /body (the cleaned interactive set) never show.
/// Main-thread only.
/// </summary>
internal static class Introspect
{
    // The active panel only: lean per-node detail, matching what an agent usually wants first.
    // textMaxLen raises the per-node text cap for the calls where the truncated tail is the point
    // (a bound prose sentence the panel draws in one text node); 0 keeps TreeDump's own default.
    public static string DumpTree(int textMaxLen = 0)
    {
        var sb = new StringBuilder();
        _textMaxLen = textMaxLen;

        if (NavigationManager.GetActivePanel() is VisualElement active)
        {
            AppendTree(sb, active);
            return sb.ToString();
        }

        // The cursor's active panel is momentarily null — e.g. just after a panel closes with an
        // empty history stack, before the next panel's load event lands, or in the tick right after a
        // /reload. The raw tree is still there, so this diagnostic must not go blank while PanelManager
        // has panels open. Fall back to the topmost interactive panel (the same ordered set /body
        // renders; last = highest layer = what the player is on). Best-effort read so a dev endpoint
        // never triggers a Kill the way the render-path Current() would.
        var panels = InteractivePanels.CurrentOrNull();
        if (panels == null || panels.Count == 0)
            return "no active panel\n";

        var fallback = panels[panels.Count - 1];
        sb.Append("(no cursor-active panel; showing topmost interactive panel: \"")
          .Append(fallback.Title).Append("\")\n");
        if (fallback.Panel is VisualElement ve)
            AppendTree(sb, ve);
        return sb.ToString();
    }

    // Set by the current DumpTree call; read by AppendTree on the same main-thread call, so the two
    // never interleave.
    private static int _textMaxLen;

    private static void AppendTree(StringBuilder sb, VisualElement panel)
    {
        var opts = new TreeDump.Options();
        if (_textMaxLen > 0) opts.TextMaxLen = _textMaxLen;
        bool truncated = TreeDump.Write(panel, sb, opts);
        if (truncated) sb.Append("... (truncated at node cap)\n");
    }

    // The mod's classification of every interactive panel: the navigation stops (with the
    // announcement the player would hear), containers, wrappers and unknowns, one section per panel.
    // The most actionable view - "what can the player reach on this screen, and what is being
    // missed" - returned inline instead of the file F9 writes. The interactive set is exactly what
    // /body and /read render, so this classifies precisely what the player perceives, not just the
    // single active panel.
    public static string DumpClassification()
    {
        var panels = InteractivePanels.Current();
        if (panels.Count == 0)
            return "no interactive panels\n";

        var active = NavigationManager.GetActivePanel();
        var sb = new StringBuilder();
        foreach (var pv in panels)
        {
            // Classify under each panel's own profile (hide rules differ), restoring the speech
            // panel's profile after, exactly as the multi-panel body build does.
            AccessibilityServer.ActivateProfileFor(pv.Panel);
            sb.Append("========== PANEL: \"").Append(pv.Title).Append("\" ==========\n");
            ClassificationDump.RenderPanel(sb, ClassificationDump.Classify(pv.Panel));
        }
        AccessibilityServer.ActivateProfileFor(active);
        return sb.ToString();
    }

    // Every open panel across all layers. Each panel is capped independently so one huge panel
    // cannot starve the rest, and the whole response stays bounded.
    public static string DumpAllPanels(int textMaxLen = 0)
    {
        if (!SI.Bindable.PanelManager.Exists())
            return "PanelManager not available\n";

        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        var sb = new StringBuilder();

        for (int i = 0; i < layers.Count; i++)
        {
            var layerInfo = layers[i];
            var open = layerInfo.Open;
            if (open == null || open.Count == 0) continue;

            sb.Append("========== LAYER: ").Append(layerInfo.Layer).Append(" (").Append(i).Append(") ==========\n");
            for (int p = 0; p < open.Count; p++)
            {
                var panel = open[p];
                if (panel == null) continue;
                sb.Append("---------- PANEL: ").Append(panel.name).Append(" (").Append(panel.GetIl2CppType().Name).Append(") ----------\n");
                if (panel is VisualElement ve)
                {
                    var opts = new TreeDump.Options { MaxNodes = 2000 };
                    if (textMaxLen > 0) opts.TextMaxLen = textMaxLen;
                    bool truncated = TreeDump.Write(ve, sb, opts);
                    if (truncated) sb.Append("... (panel truncated at node cap)\n");
                }
            }
        }

        return sb.Length == 0 ? "no open panels\n" : sb.ToString();
    }
}

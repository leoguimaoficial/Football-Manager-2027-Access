using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Tactics Planner phase-summary "Select" buttons. The instructions summary column lists one
/// section per phase (Overview / Final Third / Progression / Buildup in possession; Overview /
/// High Press / Mid Block / Low Block out of possession), each headed by a PhaseText label with a
/// bare "Select" button beside it that repoints the tile canvas at that phase. Rendered
/// generically the page shows three identical "Select" buttons with no accessible clue which
/// phase each selects — and a profile rename cannot fix it because the phase names differ per
/// tab. This renderer composes the button's own label with the sibling PhaseText, read live at
/// render time: "Select Final Third".
///
/// HTML surface only: the speech cursor still reads "Select, button", contextualised by the
/// PhaseText heading immediately before it (heading rule in TacticalPlannerTool.json).
/// </summary>
internal sealed class TacticsPhaseSelectRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "button") return false;
        if (ServerText.IsDisabled(el)) return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        return PhaseLabel(a.BackingElement) != null;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        string phase = PhaseLabel(((Adapters.PluginAdapterBase)el).BackingElement);
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<p><button type=\"button\" id=\"").Append(id)
          .Append("\" onclick=\"act('").Append(id).Append("')\">")
          .Append(ServerText.Escape(DisplayText.Of(el))).Append(' ')
          .Append(ServerText.EscapeText(phase)).Append("</button></p>");
    }

    // The button sits at PhaseHeader > SelectedSwitch > SelectButton > button; the phase name is
    // the PhaseHeader's direct PhaseText child. Null when this is not a phase Select button
    // (which routes the stop back to the generic button rendering).
    private static string PhaseLabel(VisualElement button)
    {
        var parent = button.hierarchy.parent;
        if (parent == null || parent.name != "SelectButton") return null;

        var header = parent;
        for (int depth = 0; header != null && header.name != "PhaseHeader" && depth < 3; depth++)
            header = header.hierarchy.parent;
        if (header == null || header.name != "PhaseHeader") return null;

        for (int i = 0; i < header.hierarchy.childCount; i++)
        {
            var child = header.hierarchy[i];
            if (child.name == "PhaseText" && child.GetIl2CppType().Name == "SIText")
            {
                var text = child.Cast<TextElement>().text;
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        return null;
    }
}

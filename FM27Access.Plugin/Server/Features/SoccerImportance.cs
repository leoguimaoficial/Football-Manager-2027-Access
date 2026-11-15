using System;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Quick Start "Team Selection" Nation tab shows a continent's Soccer Importance as an
/// SIStepper "step bar" (filled segments) with no text value, so the HTML surface reads
/// "Soccer Importance" with nothing after it while the sibling "Population / 880M" reads in
/// full. This renderer emits the label followed by the stepper's filled-of-total count
/// ("Soccer Importance" / "4 of 5"), mirroring the label-above-value pattern of the Population
/// pair beside it — the same filled count the sighted player reads off the bar.
///
/// Recognition is structural: an SIText under the continent banner's FootballImportance
/// container (locale-proof, unlike the visible "Soccer Importance" text). HTML surface only.
/// </summary>
internal sealed class SoccerImportanceRenderer : IElementRenderer
{
    private const string ImportanceContainer = "FootballImportance";
    private const int MaxAncestorDepth = 4;

    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "text") return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        try
        {
            return AncestorNamed(a.BackingElement, ImportanceContainer) != null;
        }
        catch (Exception ex)
        {
            Log.Warn($"[SoccerImportance] Structure read failed, using generic rendering: {ex.Message}");
            return false;
        }
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        sb.Append("<p>").Append(ServerText.Escape(DisplayText.Of(el))).Append("</p>");
        sb.Append("<p>").Append(ServerText.Escape(ReadImportance(el))).Append("</p>");
    }

    // The stepper's filled-of-total count, or an honest degrade marker when the step bar the
    // label sits beside can't be read (never a fabricated or silently-omitted value).
    private static string ReadImportance(IAccessibleElement el)
    {
        try
        {
            var backing = ((Adapters.PluginAdapterBase)el).BackingElement;
            var container = AncestorNamed(backing, ImportanceContainer);
            var stepper = Adapters.ButtonAdapter.FindDescendant(container, "SIStepper");
            if (stepper == null)
                return Degrade.Value("Soccer Importance: SIStepper not present");

            Adapters.PrivateFieldCache.EnsureInitialized();
            var cast = stepper.Cast<SI.Bindable.SIStepper>();
            var steps = (Il2CppSystem.Collections.Generic.List<VisualElement>)
                Adapters.PrivateFieldCache.StepperSteps.GetValue(cast);
            int filled = Adapters.ButtonAdapter.CountFilledSteps(steps);
            return $"{filled} of {steps.Count}";
        }
        catch (Exception ex)
        {
            return Degrade.Value($"Soccer Importance read failed: {ex.Message}");
        }
    }

    private static VisualElement AncestorNamed(VisualElement el, string name)
    {
        var p = el.hierarchy.parent;
        for (int up = 0; p != null && up < MaxAncestorDepth; up++)
        {
            if (p.name == name) return p;
            p = p.hierarchy.parent;
        }
        return null;
    }
}

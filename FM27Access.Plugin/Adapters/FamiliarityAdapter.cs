using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// The Tactics Planner toolbar's Familiarity indicator: a button-shaped container wrapping an
// SIStepper whose bound value is the squad's familiarity with the PRIMARY TRAINED tactic (not
// the tactic slot being edited — that's why the value doesn't move when switching slots).
// Activation is a no-op in-game, so it presents as a read-only value, never a button. The value
// is SIStepper.m_currentValue in basis points (8811 = 88%); the visual step fill only shows
// which of the 0/15/30/45/60/90/100% thresholds were crossed and would lose the real precision.
// See docs/reference/tactics-familiarity-and-proficiency.md.
internal class FamiliarityAdapter : PluginAdapterBase
{
    public FamiliarityAdapter(VisualElement element) : base(element) { }

    public override string Role => "text";
    public override bool CanActivate => false;

    // m_currentValue's 0-10000 basis-point scale, confirmed live (its m_stepLimits run to 10000).
    private const float BasisPointScale = 10000f;

    // The widget's template container is named "familiarity" (live-probed 2026-07-01: the
    // classified SIButton "button" sits under BindingRemapper "remapper" > BindingExpect
    // "expect" > TemplateContainer "familiarity"; the Intensity twin is under "intensity").
    // A template id is locale-proof where the visible label text is not. The stepper child
    // requirement keeps this from ever matching a restyled plain button.
    internal static bool IsFamiliarityIndicator(VisualElement element)
    {
        if (ButtonAdapter.FindDescendant(element, "SIStepper") == null) return false;
        var current = element.hierarchy.parent;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (current.name == "familiarity") return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    public override string GetLabel()
    {
        var label = ButtonAdapter.FindDescendant(Element, "SIText");
        if (label == null)
            return Speech.Degrade.Read("Familiarity indicator: label SIText no longer present");
        return label.Cast<SI.Bindable.SIText>().text;
    }

    public override string GetState()
    {
        var stepper = ButtonAdapter.FindDescendant(Element, "SIStepper");
        if (stepper == null)
            return Speech.Degrade.Value("Familiarity indicator: SIStepper no longer present");

        PrivateFieldCache.EnsureInitialized();
        var castStepper = stepper.Cast<SI.Bindable.SIStepper>();
        float value = (float)PrivateFieldCache.StepperCurrentValue.GetValue(castStepper);
        var limits = (Il2CppSystem.Collections.Generic.List<float>)PrivateFieldCache.StepperStepLimits.GetValue(castStepper);

        // The percentage claim rests on the basis-point scale; verify it against the live step
        // limits every read so a game update that rebases the stepper degrades instead of lying.
        if (limits == null || limits.Count == 0 || limits[limits.Count - 1] != BasisPointScale)
            return Speech.Degrade.Value(
                $"Familiarity stepper scale changed (max limit {(limits == null || limits.Count == 0 ? "none" : limits[limits.Count - 1].ToString())})");

        int percent = (int)System.Math.Round(value / (BasisPointScale / 100f));
        return $"{percent}%";
    }

    // Label + value only; the "text" role and a positional step count add nothing a
    // read-only percentage doesn't already convey.
    public override string GetAnnouncement()
    {
        var label = GetLabel();
        var state = GetState();
        return string.IsNullOrEmpty(state) ? label : $"{label}, {state}";
    }

    public override void Activate() =>
        throw new NotSupportedException("The Familiarity indicator is read-only.");
}

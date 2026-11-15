using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class StepIndicatorAdapter : PluginAdapterBase
{
    public StepIndicatorAdapter(VisualElement element) : base(element) { }

    public override string Role => "step";
    public override bool CanActivate => false;

    public override string GetLabel() => Element.Cast<TextElement>().text;

    public override string GetState() => "";

    public override void Activate() =>
        throw new NotSupportedException("Step indicator elements cannot be activated.");

    public override string GetAnnouncement()
    {
        ElementBridge.CollectStepIndicatorInfo(Element, out int stepIndex, out int totalSteps);

        // The step text is sometimes still unbound the instant the panel loads, so the position
        // legitimately can't be resolved yet. Rather than kill the mod, drop the "Step X of Y"
        // prefix and announce just the label (which may itself be empty until binding completes);
        // a live re-read produces the full position once the text resolves. This never speaks a
        // wrong step number — only a less complete one.
        if (stepIndex == 0 || totalSteps == 0)
        {
            Speech.Log.Warn($"[StepIndicator] Could not determine step position for \"{GetLabel()}\"; announcing label only.");
            return GetLabel();
        }

        return $"Step {stepIndex} of {totalSteps}, {GetLabel()}";
    }
}

using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class StepperAdapter : PluginAdapterBase
{
    public StepperAdapter(VisualElement element) : base(element) { }

    private bool IsButton => Element.GetIl2CppType().Name == "SIButton";
    // The summary opens native details; inside those details the same gauge
    // template disables navigation and represents a read-only value.
    private bool IsActionable => IsButton && Element.enabledInHierarchy;
    public override string Role => IsActionable ? "button" : "stepper";
    public override bool CanActivate => IsActionable;

    internal static bool IsTacticalGaugeName(string name) =>
        name?.StartsWith("te-gauge-stepper-", System.StringComparison.Ordinal) == true
        || name?.StartsWith("projected-te-gauge-stepper-", System.StringComparison.Ordinal) == true;

    internal static bool IsTacticalGaugeButton(VisualElement element) =>
        IsTacticalGaugeName(ButtonAdapter.FindDescendant(element, "SIStepper")?.name);

    public override string GetLabel()
    {
        var label = Caption();
        return IsActionable ? label + ", " + GetState() : label;
    }

    private string Caption()
    {
        if (Gauge() == null) return FindChildText() ?? Element.name;
        return new FieldLabelReader(Element).GetLabel()
            ?? PairedCaption()?.TryCast<TextElement>()?.text
            ?? FindChildText()
            ?? Contexts.TableContext.ReadColumnLabel(Element)
            ?? Speech.Degrade.Value("Tactical effectiveness gauge caption is not ready");
    }

    // The overall detail gauge places its caption BELOW a chain of unary
    // binding wrappers. Accept only its first actual two-child text/value pair;
    // do not make ordinary field labels adopt following controls' captions.
    private VisualElement PairedCaption()
    {
        var current = Element;
        for (int depth = 0; depth < 16; depth++)
        {
            var parent = current.hierarchy.parent;
            if (parent == null || parent.GetIl2CppType().Name == "Panel") return null;
            VisualElement sibling = null;
            var children = new List<VisualElement>();
            int visible = 0;
            for (int i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                if (!ElementBridge.IsVisible(child)) continue;
                visible++;
                children.Add(child);
                if (child.Pointer != current.Pointer) sibling = child;
            }
            if (visible == 1) { current = parent; continue; }
            // FM27 adds a separate provisional-score explanation after the
            // gauge/caption pair. It remains its own native text; it must not
            // prevent the preceding gauge from owning its actual caption.
            if (visible == 3 && parent.GetIl2CppType().Name == "BindingRemapper"
                && children[0].Pointer == current.Pointer
                && children[1].GetIl2CppType().Name == "SIText"
                && children[2].GetIl2CppType().Name == "SIVisible")
            {
                var help = children[2];
                if (help.hierarchy.childCount == 1 && help.hierarchy[0].GetIl2CppType().Name == "VisualElement"
                    && help.hierarchy[0].hierarchy.childCount == 2
                    && help.hierarchy[0].hierarchy[0].GetIl2CppType().Name == "VisualElement"
                    && help.hierarchy[0].hierarchy[1].GetIl2CppType().Name == "SIText"
                    && !string.IsNullOrWhiteSpace(children[1].TryCast<TextElement>()?.text)) return children[1];
            }
            return visible == 2 && sibling?.GetIl2CppType().Name == "SIText"
                && !string.IsNullOrWhiteSpace(sibling.TryCast<TextElement>()?.text) ? sibling : null;
        }
        return null;
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts() =>
        Gauge() == null ? base.FoldedTexts() : new[] { Server.ServerText.Clean(Caption()) };

    public override string GetState()
    {
        PrivateFieldCache.EnsureInitialized();
        var gauge = Gauge();
        if (gauge != null)
        {
            var typed = gauge.Cast<SI.Bindable.SIStepper>();
            float value = (float)PrivateFieldCache.StepperCurrentValue.GetValue(typed);
            var limits = (Il2CppSystem.Collections.Generic.List<float>)PrivateFieldCache.StepperStepLimits.GetValue(typed);
            // FM27's te-gauge uses a 0..100 model with nonuniform visual
            // thresholds. Counted pips lose that value; keep the bound scale.
            if (limits == null || limits.Count == 0 || limits[limits.Count - 1] != 100f)
                return Speech.Degrade.Value("Tactical effectiveness gauge scale/value is not ready");
            float min = (float)PrivateFieldCache.StepperRangeMin.GetValue(typed);
            float max = (float)PrivateFieldCache.StepperRangeMax.GetValue(typed);
            if (typed.CurrentFillMode == SI.Bindable.SIStepper.FillMode.Range)
            {
                if (!float.IsFinite(min) || !float.IsFinite(max) || min < 0f || max > 100f || min > max)
                    return Speech.Degrade.Value("Projected tactical effectiveness range is not ready");
                return min.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " to "
                    + max.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " of 100";
            }
            // The unscouted template selects its neutral first pip; it is not
            // a measured zero. Its actual tooltip requests scouting before a
            // value can be known. Keep that native no-data state, not a hidden
            // player metric or a fictitious 0/100. GameID1900 is the game's
            // generic Unknown caption, resolved in the current game language.
            if (!IsButton && typed.CurrentFillMode == SI.Bindable.SIStepper.FillMode.SelectedOnly
                && value == 0f && min == -1f && max == -1f)
            {
                var unknown = new SI.Translation.TranslationID(1900, string.Empty).TranslatedString;
                return !string.IsNullOrWhiteSpace(unknown) ? unknown
                    : Speech.Degrade.Value("Native unknown-value caption is not ready");
            }
            if (!float.IsFinite(value) || value < 0f || value > 100f)
                return Speech.Degrade.Value("Tactical effectiveness gauge scale/value is not ready");
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " of 100";
        }
        if (PrivateFieldCache.StepperSteps == null)
            return Speech.Degrade.Value($"[StepperAdapter] SIStepper.m_steps contract missing, cannot read \"{Element.name}\"");

        var castStepper = Element.Cast<SI.Bindable.SIStepper>();
        var steps = (Il2CppSystem.Collections.Generic.List<VisualElement>)PrivateFieldCache.StepperSteps.GetValue(castStepper);
        // Same "built after the element exists" window as ButtonAdapter.TryGetStepperValue.
        if (steps == null)
            return Speech.Degrade.Value($"[StepperAdapter] stepper \"{Element.name}\" has no steps yet");

        int totalSteps = steps.Count;
        int filledSteps = ButtonAdapter.CountFilledSteps(steps);

        return $"{filledSteps} of {totalSteps}";
    }

    private VisualElement Gauge()
    {
        var stepper = IsButton ? ButtonAdapter.FindDescendant(Element, "SIStepper") : Element;
        return IsTacticalGaugeName(stepper?.name) ? stepper : null;
    }

    public override string GetAnnouncement() => IsActionable ? GetLabel() + ", button"
        : Gauge() != null ? Caption() + ": " + GetState() : base.GetAnnouncement();

    public override void Activate()
    {
        if (!IsActionable) throw new NotSupportedException("Read-only steppers cannot be activated.");
        new ButtonAdapter(Element).Activate();
    }
}

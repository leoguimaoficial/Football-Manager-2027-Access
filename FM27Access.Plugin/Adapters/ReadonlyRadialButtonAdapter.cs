using UnityEngine.UIElements;
namespace FM27Access.Plugin.Adapters;

// Some native meter headers use SIButton solely as a visual/tooltip wrapper.
// They cannot submit and have no event/custom command; keep the actual value
// as readonly information instead of acknowledging an ineffective activation.
internal sealed class ReadonlyRadialButtonAdapter : ButtonAdapter
{
    internal ReadonlyRadialButtonAdapter(VisualElement element) : base(element) { }
    public override string Role => "text";
    public override bool CanActivate => false;
    public override string GetLabel()
    {
        var label = base.GetLabel();
        var gauge = OwnRadialGauge(BackingElement);
        var value = gauge == null ? null : new RadialGaugeAdapter(gauge).GetState();
        return string.IsNullOrEmpty(value) ? label : label + ", " + value;
    }
    public override string GetAnnouncement() => GetLabel();
    public override void Activate() => throw new System.NotSupportedException("This native meter is readonly.");
}

using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class StarRatingGaugeAdapter : PluginAdapterBase
{
    public StarRatingGaugeAdapter(VisualElement element) : base(element) { }

    public override string Role => "star rating";
    public override bool CanActivate => false;

    public override string GetLabel() => StarRatingHelper.ReadStarLabel(Element);

    public override string GetState() => "";

    public override void Activate() =>
        throw new System.NotSupportedException("Star ratings cannot be activated.");
}

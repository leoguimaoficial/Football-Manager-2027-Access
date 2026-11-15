using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class FloatFieldAdapter : PluginAdapterBase
{
    public FloatFieldAdapter(VisualElement element) : base(element) { }

    public override string Role => "number field";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        var label = Element.Cast<FloatField>().label;
        if (!string.IsNullOrEmpty(label))
            return label;

        return FindChildText() ?? Element.name;
    }

    public override string GetState()
    {
        var value = Element.Cast<FloatField>().value;
        return $"{value:F1}";
    }

    public override void Activate() => Element.Focus();
}

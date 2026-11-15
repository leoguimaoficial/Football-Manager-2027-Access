using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class TacticsShirtAdapter : PluginAdapterBase
{
    public TacticsShirtAdapter(VisualElement element) : base(element) { }

    public override string Role => "button";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        var position = ReadPosition();
        if (position != null)
            return $"{position}, Player instructions";
        return "Player instructions";
    }

    public override string GetState() => Element.enabledInHierarchy ? "" : "disabled";

    public override void Activate()
    {
        Element.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
    }

    private string ReadPosition()
    {
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "FMPitchIndicator")
            {
                var name = current.name;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            current = current.hierarchy.parent;
        }
        return null;
    }


}

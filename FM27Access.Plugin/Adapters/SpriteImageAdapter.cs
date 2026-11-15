using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class SpriteImageAdapter : PluginAdapterBase
{
    private readonly string _label;

    public SpriteImageAdapter(VisualElement element, string label) : base(element)
    {
        _label = label;
    }

    public override string Role => "image";
    public override bool CanActivate => false;
    public override string GetLabel() => _label;
    public override string GetState() => "";
    public override string GetAnnouncement() => _label;
    public override void Activate() { }
}

using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class DiggableAdapter : PluginAdapterBase
{
    public DiggableAdapter(VisualElement element) : base(element) { }

    public override string Role => "expandable";
    public override bool CanActivate => true;

    public override string GetLabel() => FindBestLabel() ?? "unlabeled expandable";

    public override string GetState() => Element.enabledInHierarchy ? "" : "disabled";

    public override void Activate()
    {
        var evt = new NavigationSubmitEvent();
        Element.SendEvent(evt);
    }
}

using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal sealed class EmptyCellAdapter : PluginAdapterBase
{
    public EmptyCellAdapter(VisualElement cell) : base(cell) { }

    public override string Role => "cell";
    public override bool CanActivate => false;
    public override string GetLabel() => "empty";
    public override string GetState() => null;
    public override string GetAnnouncement() => "empty";
    public override void Activate() { }
}

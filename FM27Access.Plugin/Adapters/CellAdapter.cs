using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class CellAdapter : PluginAdapterBase
{
    private readonly VisualElement _cell;
    private readonly ClassificationCache _cache;
    private readonly IAccessibleElement _firstStop;

    internal IAccessibleElement FirstStop => _firstStop;

    public CellAdapter(VisualElement cell, ClassificationCache cache, IAccessibleElement firstStop)
        : base(((PluginAdapterBase)firstStop).BackingElement)
    {
        _cell = cell;
        _cache = cache;
        _firstStop = firstStop;
    }

    public override string Role => _firstStop.Role;
    public override bool CanActivate => _firstStop.CanActivate;
    public override string GetLabel() => _firstStop.GetLabel();
    public override string GetState() => _firstStop.GetState();

    public override string GetAnnouncement()
    {
        var stops = TreeWalker.FindAllStopsInContainer(_cell, _cache);
        RadialGaugeAdapter.FoldPairedCaptions(stops);
        if (stops.Count <= 1)
            return _firstStop.GetAnnouncement();

        var parts = new List<string>(stops.Count);
        foreach (var stop in stops)
        {
            var text = stop.GetAnnouncement();
            if (!string.IsNullOrEmpty(text))
                parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    public override void Activate() => _firstStop.Activate();
}

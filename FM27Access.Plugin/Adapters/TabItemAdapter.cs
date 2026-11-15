using System.Reflection;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Wraps a tab element with positional metadata and selection state.
/// Activates via Clickable.Invoke to match real mouse-click behavior.
/// Must be freshly constructed per navigation step (via TabContext.WrapForContext)
/// — the cached _isSelected is safe because instances are never held across tab changes.
/// </summary>
internal sealed class TabItemAdapter : PluginAdapterBase
{
    private static readonly MethodInfo ClickableInvokeMethod;

    static TabItemAdapter()
    {
        var clickableType = typeof(Clickable);
        ClickableInvokeMethod = clickableType.GetMethod("Invoke",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(EventBase) }, null);
        if (ClickableInvokeMethod == null)
        {
            Speech.Log.Error("[TabItemAdapter] Clickable.Invoke method not found.");
            throw new MissingMethodException("Clickable", "Invoke");
        }
    }

    private readonly IAccessibleElement _inner;
    private readonly int _position;
    private readonly int _total;
    private readonly bool _isSelected;

    internal TabItemAdapter(IAccessibleElement inner, int position, int total, bool isSelected)
        : base(((PluginAdapterBase)inner).BackingElement)
    {
        _inner = inner;
        _position = position;
        _total = total;
        _isSelected = isSelected;
    }

    public override string Role => "tab";
    public override bool CanActivate => true;
    public bool IsSelected => _isSelected;
    public override string GetLabel() => _inner.GetLabel();
    public override string GetState() => _isSelected ? $"{_position} of {_total}, selected" : $"{_position} of {_total}";

    public override void Activate()
    {
        var button = Element.Cast<Button>();
        var clickable = button.clickable;
        if (clickable == null)
        {
            Speech.Log.Error("[TabItemAdapter] Tab button has no clickable.");
            throw new InvalidOperationException("Tab button has no clickable.");
        }
        var evt = ClickEvent.GetPooled();
        ClickableInvokeMethod.Invoke(clickable, new object[] { evt });
    }
}

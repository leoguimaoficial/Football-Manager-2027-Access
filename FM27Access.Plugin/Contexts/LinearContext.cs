using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

internal sealed class LinearContext : IContainerContext
{
    private readonly VisualElement _container;
    private readonly ClassificationCache _cache;

    public object Scope => _container;

    internal LinearContext(VisualElement container, ClassificationCache cache)
    {
        _container = container;
        _cache = cache;
    }

    public bool HandlesDirection(NavigationDirection direction)
    {
        return direction == NavigationDirection.Up || direction == NavigationDirection.Down;
    }

    public IAccessibleElement GetFirstChild()
    {
        return TreeWalker.FindFirstStopInContainer(_container, _cache);
    }

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        var backing = ((PluginAdapterBase)current).BackingElement;
        return direction == NavigationDirection.Down
            ? TreeWalker.FindNextStopBounded(backing, _container, _cache)
            : TreeWalker.FindPreviousStopBounded(backing, _container, _cache);
    }
}

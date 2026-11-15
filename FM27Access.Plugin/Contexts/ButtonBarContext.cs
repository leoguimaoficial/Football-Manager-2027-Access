using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

internal sealed class ButtonBarContext : IContainerContext
{
    private readonly VisualElement _container;
    private readonly ClassificationCache _cache;

    public object Scope => _container;

    internal ButtonBarContext(VisualElement container, ClassificationCache cache)
    {
        _container = container;
        _cache = cache;
    }

    public bool HandlesDirection(NavigationDirection direction) => true;

    public bool ExitsOnBoundary(NavigationDirection direction) =>
        direction == NavigationDirection.Up || direction == NavigationDirection.Down;

    public IAccessibleElement GetFirstChild()
    {
        return TreeWalker.FindFirstStopInContainer(_container, _cache);
    }

    public IAccessibleElement GetEntryElement(NavigationDirection direction)
    {
        return direction == NavigationDirection.Up
            ? TreeWalker.FindLastStopInContainer(_container, _cache)
            : TreeWalker.FindFirstStopInContainer(_container, _cache);
    }

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        if (direction == NavigationDirection.Up || direction == NavigationDirection.Down)
            return null;

        var backing = ((PluginAdapterBase)current).BackingElement;
        int currentIndex = TreeWalker.FindDirectChildContaining(_container, backing);
        int count = _container.hierarchy.childCount;

        int step = direction == NavigationDirection.Right ? 1 : -1;
        for (int i = currentIndex + step; i >= 0 && i < count; i += step)
        {
            var stop = TreeWalker.FindDirectChildStop(_container, i, _cache);
            if (stop != null)
                return stop;
        }

        return null;
    }
}

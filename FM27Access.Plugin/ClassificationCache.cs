using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

internal class ClassificationCache
{
    // Hierarchy reads can return different managed wrappers for one native element.
    // Managed Dictionary equality does not use Il2CppSystem.Object's native overloads.
    private readonly Dictionary<IntPtr, ElementClassification> _cache = new();
    private readonly Dictionary<IntPtr, IContainerContext> _contexts = new();

    public ElementClassification GetOrClassify(VisualElement element, ElementClassification? parentClassification)
    {
        if (_cache.TryGetValue(element.Pointer, out var cached))
            return cached;

        var classification = ElementBridge.ClassifyElement(element, parentClassification);
        // Do not cache the provisional verdict for an empty text element. A streamed table cell's
        // SIText binds its value a frame or two after the element appears, and an empty SIText
        // classifies as a non-stop (LayoutWrapper). Caching that would drop the value permanently once
        // it lands, because the cache never re-evaluates a stored entry. Re-classify each walk until
        // the element has text, then cache normally.
        if (!ElementBridge.IsEmptyTextElement(element))
            _cache[element.Pointer] = classification;
        return classification;
    }

    public ElementClassification? GetClassification(VisualElement element)
    {
        return _cache.TryGetValue(element.Pointer, out var classification) ? classification : null;
    }

    public void AttachContext(VisualElement container, IContainerContext context)
    {
        _contexts[container.Pointer] = context;
    }

    public IContainerContext GetContext(VisualElement container)
    {
        _contexts.TryGetValue(container.Pointer, out var context);
        return context;
    }

    public IContainerContext FindContextForElement(VisualElement element)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            if (_contexts.TryGetValue(current.Pointer, out var context))
                return context;
            current = current.hierarchy.parent;
        }
        return null;
    }

    public void Remove(VisualElement element)
    {
        _cache.Remove(element.Pointer);
        _contexts.Remove(element.Pointer);
    }

    public void Clear()
    {
        _cache.Clear();
        _contexts.Clear();
    }
}

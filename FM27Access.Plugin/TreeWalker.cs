using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

internal static class TreeWalker
{
    internal static readonly HashSet<ElementClassification> NavigationStops = new()
    {
        ElementClassification.Button,
        ElementClassification.Checkbox,
        ElementClassification.Slider,
        ElementClassification.Dropdown,
        ElementClassification.Text,
        ElementClassification.ProgressBar,
        ElementClassification.Gauge,

        ElementClassification.Image,
        ElementClassification.TextField,
        ElementClassification.SearchBox,
        ElementClassification.NumberField,
        ElementClassification.RadioButton,
        ElementClassification.ColumnHeader,
        ElementClassification.DateSelector,
        ElementClassification.Toggle,
        ElementClassification.Diggable,
        ElementClassification.StarRating,
        ElementClassification.Combobox,
        ElementClassification.StepIndicator,
        ElementClassification.SegmentedToggle,
        // No type classifies as Stepper on its own (SIStepper is a layout wrapper, since most
        // step bars sit inside a button that already folds their count in). Only the bare
        // step bars ElementBridge.ClassifyElement promotes reach here.
        ElementClassification.Stepper,
    };

    internal static readonly HashSet<ElementClassification> ContainerClassifications = new()
    {
        ElementClassification.Section,
        ElementClassification.ButtonBar,
        ElementClassification.RadioGroup,
        ElementClassification.Table,
        ElementClassification.SpeakToPanel,
        ElementClassification.Calendar,
    };

    public static IAccessibleElement FindFirstStop(VisualElement root, ClassificationCache cache)
    {
        return FindFirstStopInSubtree(root, null, cache);
    }

    public static IAccessibleElement FindStopByAnnouncement(VisualElement root, ClassificationCache cache, string announcement, int occurrenceIndex = 1)
    {
        int count = 0;
        return FindStopByAnnouncementRecursive(root, cache, announcement, occurrenceIndex, ref count);
    }

    public static int GetOccurrenceIndex(VisualElement root, ClassificationCache cache, VisualElement target, string announcement)
    {
        int count = 0;
        int result = GetOccurrenceIndexRecursive(root, cache, target, announcement, ref count);
        return result > 0 ? result : 1;
    }

    private static int GetOccurrenceIndexRecursive(VisualElement element, ClassificationCache cache, VisualElement target, string announcement, ref int count, ElementClassification? parentClassification = null)
    {
        if (!ElementBridge.IsVisible(element)) return 0;

        var classification = cache.GetOrClassify(element, parentClassification);

        if (classification == ElementClassification.InternalChild)
            return 0;

        if (NavigationStops.Contains(classification))
        {
            var adapter = ElementBridge.CreateAdapter(element, classification);
            if (adapter.GetAnnouncement() == announcement)
            {
                count++;
                if (target != null && element.Pointer == target.Pointer)
                    return count;
            }
        }

        int childCount = element.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            int found = GetOccurrenceIndexRecursive(element.hierarchy[i], cache, target, announcement, ref count, classification);
            if (found > 0) return found;
        }

        return 0;
    }

    private static IAccessibleElement FindStopByAnnouncementRecursive(VisualElement element, ClassificationCache cache, string announcement, int occurrenceIndex, ref int count, ElementClassification? parentClassification = null)
    {
        if (!ElementBridge.IsVisible(element)) return null;

        var classification = cache.GetOrClassify(element, parentClassification);

        if (classification == ElementClassification.InternalChild)
            return null;

        if (NavigationStops.Contains(classification))
        {
            var adapter = ElementBridge.CreateAdapter(element, classification);
            if (adapter.GetAnnouncement() == announcement)
            {
                count++;
                if (count == occurrenceIndex)
                    return adapter;
            }
        }

        int childCount = element.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var found = FindStopByAnnouncementRecursive(element.hierarchy[i], cache, announcement, occurrenceIndex, ref count, classification);
            if (found != null) return found;
        }

        return null;
    }

    public static IAccessibleElement FindNextStop(VisualElement current, ClassificationCache cache)
    {
        return FindNextStopBounded(current, null, cache);
    }

    public static IAccessibleElement FindPreviousStop(VisualElement current, ClassificationCache cache)
    {
        return FindPreviousStopBounded(current, null, cache);
    }

    public static IAccessibleElement FindNextStopBounded(VisualElement current, VisualElement boundary, ClassificationCache cache)
    {
        // RadioButtons may contain child stops (e.g. edit/delete buttons) — check inside first
        var currentClassification = cache.GetOrClassify(current, null);
        if (currentClassification == ElementClassification.RadioButton)
        {
            var inner = FindStopInSubtree(current, currentClassification, cache, forward: true);
            if (inner != null)
                return inner;
        }

        var element = current;

        while (true)
        {
            var parent = element.hierarchy.parent;
            if (parent == null || (boundary != null && parent.Pointer == boundary.Pointer))
                return null;

            int index = IndexInParent(element);
            int siblingCount = parent.hierarchy.childCount;

            for (int i = index + 1; i < siblingCount; i++)
            {
                var sibling = parent.hierarchy[i];
                var found = TryNavigateInto(sibling, null, cache, forward: true);
                if (found != null)
                    return found;
            }

            element = parent;
        }
    }

    public static IAccessibleElement FindPreviousStopBounded(VisualElement current, VisualElement boundary, ClassificationCache cache)
    {
        var element = current;

        while (true)
        {
            var parent = element.hierarchy.parent;
            if (parent == null || (boundary != null && parent.Pointer == boundary.Pointer))
                return null;

            int index = IndexInParent(element);

            for (int i = index - 1; i >= 0; i--)
            {
                var sibling = parent.hierarchy[i];
                var found = TryNavigateInto(sibling, null, cache, forward: false);
                if (found != null)
                    return found;
            }

            // When navigating backward out of a RadioButton's child stops, land on the RadioButton
            var parentClassification = cache.GetOrClassify(parent, null);
            if (parentClassification == ElementClassification.RadioButton)
                return ElementBridge.CreateAdapter(parent, parentClassification);

            element = parent;
        }
    }

    public static IAccessibleElement FindFirstStopInContainer(VisualElement container, ClassificationCache cache)
    {
        return FindFirstStopInSubtree(container, null, cache);
    }

    public static IAccessibleElement FindLastStopInContainer(VisualElement container, ClassificationCache cache)
    {
        return FindLastStopInSubtree(container, null, cache);
    }

    public static List<IAccessibleElement> FindAllStopsInContainer(VisualElement container, ClassificationCache cache)
    {
        var results = new List<IAccessibleElement>();
        CollectStops(container, null, cache, results);
        return results;
    }

    private static void CollectStops(
        VisualElement root, ElementClassification? parentClassification,
        ClassificationCache cache, List<IAccessibleElement> results)
    {
        int childCount = root.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child))
                continue;

            var classification = cache.GetOrClassify(child, parentClassification);

            if (classification == ElementClassification.InternalChild)
                continue;

            // Same leaf-vs-container rule as TryNavigateInto: recurse into an unknown's subtree so
            // its real stops are still collected; only a leaf unknown is added as an "unknown control".
            if (classification == ElementClassification.Unknown)
            {
                int before = results.Count;
                CollectStops(child, classification, cache, results);
                if (results.Count == before)
                    results.Add(ElementBridge.CreateAdapter(child, classification));
                continue;
            }

            if (NavigationStops.Contains(classification))
            {
                results.Add(ElementBridge.CreateAdapter(child, classification));
                // RadioButtons may contain independently-actionable children (e.g. edit/delete buttons)
                if (classification == ElementClassification.RadioButton)
                    CollectStops(child, classification, cache, results);
                continue;
            }

            CollectStops(child, classification, cache, results);
        }
    }

    public static IAccessibleElement FindDirectChildStop(VisualElement container, int childIndex, ClassificationCache cache)
    {
        int count = container.hierarchy.childCount;
        if (childIndex < 0 || childIndex >= count)
            return null;

        var child = container.hierarchy[childIndex];
        return TryNavigateInto(child, null, cache, forward: true);
    }

    public static void EnsureAncestorContexts(VisualElement element, ClassificationCache cache)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            var classification = cache.GetOrClassify(current, null);
            if (ContainerClassifications.Contains(classification))
                AttachContextIfNeeded(current, classification, cache);
            current = current.hierarchy.parent;
        }
    }

    public static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            if (ancestor != null && current.Pointer == ancestor.Pointer)
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    public static int FindDirectChildContaining(VisualElement container, VisualElement descendant)
    {
        var current = descendant;
        while (current != null)
        {
            var parent = current.hierarchy.parent;
            if (parent != null && container != null && parent.Pointer == container.Pointer)
                return IndexInParent(current);
            current = parent;
        }
        throw new InvalidOperationException("Element is not a descendant of the container");
    }

    private static IAccessibleElement FindFirstStopInSubtree(
        VisualElement root, ElementClassification? parentClassification, ClassificationCache cache)
    {
        return FindStopInSubtree(root, parentClassification, cache, forward: true);
    }

    private static IAccessibleElement FindLastStopInSubtree(
        VisualElement root, ElementClassification? parentClassification, ClassificationCache cache)
    {
        return FindStopInSubtree(root, parentClassification, cache, forward: false);
    }

    private static IAccessibleElement FindStopInSubtree(
        VisualElement root, ElementClassification? parentClassification, ClassificationCache cache, bool forward)
    {
        int childCount = root.hierarchy.childCount;
        int start = forward ? 0 : childCount - 1;
        int end = forward ? childCount : -1;
        int step = forward ? 1 : -1;

        for (int i = start; i != end; i += step)
        {
            var child = root.hierarchy[i];
            var found = TryNavigateInto(child, parentClassification, cache, forward);
            if (found != null)
                return found;
        }
        return null;
    }

    private static IAccessibleElement TryNavigateInto(
        VisualElement element, ElementClassification? parentClassification, ClassificationCache cache, bool forward)
    {
        if (!ElementBridge.IsVisible(element))
            return null;

        var classification = cache.GetOrClassify(element, parentClassification);

        if (classification == ElementClassification.InternalChild)
            return null;

        if (ContainerClassifications.Contains(classification))
        {
            AttachContextIfNeeded(element, classification, cache);
            return FindStopInSubtree(element, classification, cache, forward);
        }

        if (classification == ElementClassification.LayoutWrapper)
            return FindStopInSubtree(element, classification, cache, forward);

        // An unclassifiable bound widget. Never hide content: if it wraps real stops, descend into
        // them. Only a leaf unknown (no stops of its own inside) is landed on, as an "unknown
        // control", so a new leaf widget is surfaced to the player while a new container type still
        // exposes its children. Either way the type was tracked for /unclassified at classify time.
        if (classification == ElementClassification.Unknown)
        {
            var inner = FindStopInSubtree(element, classification, cache, forward);
            if (inner != null)
                return inner;
            var unknownAdapter = ElementBridge.CreateAdapter(element, classification);
            if (NavigationManager.IsHidden(unknownAdapter))
                return null;
            return unknownAdapter;
        }

        if (NavigationStops.Contains(classification))
        {
            if (classification == ElementClassification.RadioButton && !forward)
            {
                var lastChild = FindStopInSubtree(element, classification, cache, forward: false);
                if (lastChild != null)
                    return lastChild;
            }
            var adapter = ElementBridge.CreateAdapter(element, classification);
            if (NavigationManager.IsHidden(adapter))
                return null;
            return adapter;
        }

        return FindStopInSubtree(element, classification, cache, forward);
    }

    private static void AttachContextIfNeeded(
        VisualElement element, ElementClassification classification, ClassificationCache cache)
    {
        if (cache.GetContext(element) != null)
            return;

        var context = ElementBridge.CreateContainerContext(element, classification, cache);
        cache.AttachContext(element, context);
    }

    private static int IndexInParent(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            if (parent.hierarchy[i].Pointer == element.Pointer)
                return i;
        }
        throw new InvalidOperationException("Element not found in its parent's hierarchy");
    }
}

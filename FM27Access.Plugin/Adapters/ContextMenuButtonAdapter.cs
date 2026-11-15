using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A standalone context-menu trigger — the game's "Actions ▾" style menu button. In the tree it is a
/// <c>PluginContextMenuBindings</c> (a NavigatableVisualElement) carrying its own visible label plus a
/// chevron-down affordance; clicking it opens a cascading menu populated from bound ActionID data
/// (e.g. the squad-selection Actions menu, enabled once one or more players are ticked).
///
/// The classifier leaves the type as infrastructure by default because most PluginContextMenuBindings
/// are invisible right-click affordances layered over other content (named RightClickMenuBinding, or
/// nested inside an SIButton that already owns the click). <see cref="IsStandaloneMenuButton"/> isolates
/// the visible, self-labelled menu buttons so only those surface as activatable.
/// </summary>
internal sealed class ContextMenuButtonAdapter : ButtonAdapter
{
    public ContextMenuButtonAdapter(VisualElement element) : base(element) { }

    // The whole label ("Actions") is the trigger's name — there is no primary/description split.
    protected override bool SplitsLabel => false;

    public override void Activate()
    {
        // PluginContextMenuBindings opens its menu through an SI.UI.ContextMenuDescriptionManipulator
        // whose OnPointerDown/OnPointerUp open the menu on a LeftMouse press-release over the element,
        // validating the live pointer position (a default-position synthetic event is rejected).
        SyntheticPointer.LeftClick(Element);
    }

    /// <summary>
    /// True for the visible "Actions ▾" style menu trigger: a PluginContextMenuBindings that shows its
    /// its own left-click binding or chevron-down affordance, outside an SIButton.
    /// The invisible right-click
    /// bindings (RightClickMenuBinding, the per-row/position context bindings) have no chevron and must
    /// stay infrastructure so the content they wrap keeps surfacing.
    /// </summary>
    public static bool IsStandaloneMenuButton(VisualElement element)
    {
        if (IsInsideButton(element)) return false;
        // FM27's report-header Actions trigger has unnamed icon children.
        // Its native binding explicitly opens on LeftMouse, unlike the
        // invisible RightMouse binding over the adjacent person portrait.
        // Keep the original chevron pattern for inherited templates.
        var native = element.TryCast<FM.UI.PluginContextMenuBindings>();
        return (native != null && native.MouseButton == MouseButton.LeftMouse
                && HasVisibleCaption(element, 0))
            || HasChevronDownChild(element, 0, 2);
    }

    private static bool HasVisibleCaption(VisualElement element, int depth)
    {
        if (!ElementBridge.IsVisibleInHierarchy(element)) return false;
        var text = element.TryCast<TextElement>();
        if (text != null && !string.IsNullOrWhiteSpace(text.text)) return true;
        if (depth >= 3) return false;
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (HasVisibleCaption(element.hierarchy[i], depth + 1)) return true;
        return false;
    }

    private static bool HasChevronDownChild(VisualElement element, int depth, int maxDepth)
    {
        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = element.hierarchy[i];
            var name = child.name;
            if (!string.IsNullOrEmpty(name) && name.Contains("chevron-down"))
                return true;
            if (depth < maxDepth && HasChevronDownChild(child, depth + 1, maxDepth))
                return true;
        }
        return false;
    }

    private static bool IsInsideButton(VisualElement element)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "SIButton")
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }
}

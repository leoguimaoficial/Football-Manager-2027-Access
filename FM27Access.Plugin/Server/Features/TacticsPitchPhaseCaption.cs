using UnityEngine.UIElements;
namespace FM27Access.Plugin.Server;

internal static class TacticsPitchPhaseCaption
{
    internal static VisualElement Read(VisualElement pitch, out VisualElement scope)
    {
        scope = null;
        var pitches = NativeTreeQuery.All<VisualElement>(FM27PanelCatalog.GetForegroundPanel(), "interactable-pitch-expanded")
            .Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (pitches.Count != 2 || !pitches.Any(p => p.Pointer == pitch.Pointer)) return null;
        var other = pitches.Single(p => p.Pointer != pitch.Pointer);
        var ancestors = new HashSet<IntPtr>();
        for (var owner = other; owner != null; owner = owner.parent) ancestors.Add(owner.Pointer);
        scope = pitch;
        while (scope.parent != null && !ancestors.Contains(scope.parent.Pointer)) scope = scope.parent;
        var tags = NativeTreeQuery.All<VisualElement>(scope, "tag-visible").Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (tags.Count == 1)
        {
            var tag = tags[0];
            var texts = NativeTreeQuery.All<SI.Bindable.SIText>(tag)
                .Where(t => ElementBridge.IsVisibleInHierarchy(t) && !string.IsNullOrWhiteSpace(t.text)).ToList();
            return texts.Count == 1 ? texts[0] : null;
        }
        return null;
    }
}

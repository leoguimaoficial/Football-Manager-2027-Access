using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class BreadcrumbAdapter : PluginAdapterBase
{
    public BreadcrumbAdapter(VisualElement element) : base(element) { }

    public override string Role => "breadcrumb";
    public override bool CanActivate => true;

    public override string GetLabel() => Element.Cast<TextElement>().text;

    public override string GetState() => "";

    public override void Activate()
    {
        var label = Element.Cast<TextElement>().text;
        var dropTarget = ElementBridge.FindAncestorByTypeName(Element, "DropTarget");
        if (dropTarget == null)
        {
            Speech.Log.Error("[BreadcrumbAdapter] No DropTarget ancestor found.");
            throw new InvalidOperationException("Breadcrumb has no DropTarget ancestor");
        }

        // Attempt 1: ClickEvent on DropTarget
        Speech.Log.Info($"[BreadcrumbAdapter] Attempt 1: ClickEvent on DropTarget for \"{label}\"");
        var click1 = ClickEvent.GetPooled();
        dropTarget.SendEvent(click1);

        // Attempt 2: ClickEvent on TextElement itself
        Speech.Log.Info($"[BreadcrumbAdapter] Attempt 2: ClickEvent on TextElement for \"{label}\"");
        var click2 = ClickEvent.GetPooled();
        Element.SendEvent(click2);

        // Attempt 3: NavigationSubmitEvent on DropTarget
        Speech.Log.Info($"[BreadcrumbAdapter] Attempt 3: NavigationSubmitEvent on DropTarget for \"{label}\"");
        dropTarget.SendEvent(new NavigationSubmitEvent());

        // Attempt 4: NavigationSubmitEvent on TextElement
        Speech.Log.Info($"[BreadcrumbAdapter] Attempt 4: NavigationSubmitEvent on TextElement for \"{label}\"");
        Element.SendEvent(new NavigationSubmitEvent());

        Speech.Log.Info($"[BreadcrumbAdapter] All attempts fired for \"{label}\"");
    }
}

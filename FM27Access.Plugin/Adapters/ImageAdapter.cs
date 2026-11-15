using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ImageAdapter : PluginAdapterBase
{
    public ImageAdapter(VisualElement element) : base(element) { }

    private bool IsPortraitMenu => CalendarEventAdapter.IsCompactPortrait(Element) && HasContextMenu;
    public override string Role => IsPortraitMenu ? "button" : "image";
    public override bool CanActivate => IsPortraitMenu;

    public override string GetLabel()
    {
        // Status images carry their live value in the resolved sprite name (for example,
        // "sharpness=5"). Read that before the UXML element name so named images outside the
        // table-specific sprite path expose the same value as their table counterparts.
        var spriteLabel = ContentLabel();
        if (!string.IsNullOrEmpty(spriteLabel))
            return spriteLabel;

        return GetFallbackLabel();
    }

    private string GetFallbackLabel()
    {
        if (Element.name == "leading-image" && Element.parent?.parent?.parent?.name == "hierarchy")
            return "Squad hierarchy";
        var tooltip = Element.tooltip;
        if (!string.IsNullOrEmpty(tooltip))
            return tooltip;

        var name = Element.name;
        if (!string.IsNullOrEmpty(name))
            return name;

        return "image";
    }

    public override string GetState() => IsPortraitMenu ? new ButtonAdapter(Element).GetState() : "";

    private string ContentLabel()
        => CalendarEventAdapter.CompactEventLabel(Element) ?? SpriteHelper.GetSpriteLabel(Element);

    public override string GetAnnouncement()
    {
        if (IsPortraitMenu) return $"{GetLabel()}, button";
        // A decoded status is the image's content, not a description of decorative media. Match
        // SpriteImageAdapter so sharpness is announced as "5 of 7" rather than "5 of 7, image".
        var spriteLabel = ContentLabel();
        return !string.IsNullOrEmpty(spriteLabel) ? spriteLabel : $"{GetFallbackLabel()}, {Role}";
    }

    public override void Activate()
    {
        // The compact portrait has no independent SIButton click. When its
        // actual person context menu is available, use the inherited native
        // menu-only interaction rather than inventing a profile navigation.
        if (IsPortraitMenu)
        {
            if (ContextMenuOpener.Send(Element, out var error)) return;
            throw new InvalidOperationException(error ?? "The player menu did not open.");
        }
        throw new NotSupportedException("Image elements cannot be activated.");
    }
}

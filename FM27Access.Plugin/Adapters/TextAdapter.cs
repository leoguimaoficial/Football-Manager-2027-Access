using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class TextAdapter : PluginAdapterBase
{
    public TextAdapter(VisualElement element) : base(element) { }

    public override string Role => "text";
    public override bool CanActivate => false;

    public override string GetLabel()
    {
        var text = Element.Cast<TextElement>().text;
        // FM27 prints only (+N) beside its native bookmark strip. Keep the
        // actual count and explain its owner instead of reading an orphan token.
        return IsBookmarkOverflow ? "Additional native bookmarks: " + text : text;
    }

    private bool IsBookmarkOverflow => Element.name == "BookmarksText"
        && Element.hierarchy.parent?.hierarchy.parent?.name == "Bookmarks";

    public override string GetDescription() => IsBookmarkOverflow
        ? "The game has more selected bookmarks than fit in its shortcut bar. The game bookmarks control opens the native favorites and bookmark bar. Edit bookmarks manages the companion's separate Ctrl shortcuts."
        : base.GetDescription();
    public override string GetState() => "";

    public override void Activate() =>
        throw new NotSupportedException("Text elements cannot be activated.");

    public override string GetAnnouncement() => GetLabel();
}

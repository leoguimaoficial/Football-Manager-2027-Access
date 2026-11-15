using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A text leaf that speaks its parent's whole run of sibling texts as one value. The club staff
/// overview's role-count badge splits "9 / 13" across three SIText leaves (CurrentCount, Slash,
/// text-second); ClassifyElement demotes the trailing two to internal children and this adapter,
/// on CurrentCount, reads the full run live so the badge is a single stop.
/// </summary>
internal class TextRunAdapter : PluginAdapterBase
{
    public TextRunAdapter(VisualElement element) : base(element) { }

    public override string Role => "text";
    public override bool CanActivate => false;

    public override string GetLabel()
    {
        var parent = Element.hierarchy.parent;
        if (parent == null) return Element.Cast<TextElement>().text;

        var sb = new StringBuilder();
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            var textEl = child.TryCast<TextElement>();
            if (textEl == null || string.IsNullOrEmpty(textEl.text)) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(textEl.text);
        }
        return sb.Length > 0 ? sb.ToString() : Element.Cast<TextElement>().text;
    }

    public override string GetState() => "";

    public override void Activate() =>
        throw new NotSupportedException("Text elements cannot be activated.");

    public override string GetAnnouncement() => GetLabel();
}

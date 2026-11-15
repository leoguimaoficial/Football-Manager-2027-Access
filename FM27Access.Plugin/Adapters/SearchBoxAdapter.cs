using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// SearchBox is a BindableTextEditBox. Share its value/change lifecycle and
// text-field HTML while retaining the original native speech search mode.
internal class SearchBoxAdapter : TextFieldAdapter
{
    public SearchBoxAdapter(VisualElement element) : base(element) { }

    public override string Role => "search box";
    public override bool CanActivate => true;

    internal override string FieldLabel() => "Global search";

    public override string GetState() => "";

    public override string GetAnnouncement()
    {
        var label = GetLabel();
        return $"{label}, {Role}";
    }

    public string ReadCurrentText() => GetCurrentText();
}

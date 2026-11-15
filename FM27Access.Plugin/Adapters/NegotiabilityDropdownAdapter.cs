using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class NegotiabilityDropdownAdapter : DropdownAdapter
{
    public NegotiabilityDropdownAdapter(VisualElement element) : base(element)
    {
    }

    public override string GetState()
    {
        var baseState = ReadState(resolveNativeCaption: false);
        // The row's own enum/DataCollection binding supplies its localized
        // selected caption before popup rows are realized. Opening/hiding every
        // padlock during rendering reads stale/empty rows on FM27; disabled
        // native dropdowns must never be opened to obtain their current value.
        var choiceText = StaticButtonCaption.ReadDropdownValue(Element.TryCast<SI.Bindable.SIDropdown>());
        // A passive body/value read must never ShowPopup/HidePopup. FM27's
        // whole-offer control already exposes its own selected native text but
        // has no enum collection for the resolver above. The FM26 fallback
        // repeatedly opened an unbound popup on every values sweep, producing
        // empty-row warnings and unnecessary native UI/binding work.
        if (choiceText != null)
            return string.IsNullOrEmpty(baseState) || choiceText == baseState ? choiceText : $"{choiceText}, {baseState}";
        return baseState;
    }

    internal static bool IsNegotiabilityDropdown(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        while (parent != null)
        {
            if (parent.name == "lock-button-enabled" ||
                parent.name == "negotiable-dropdown-enabled" ||
                parent.name == "frames-contract negotiations-contract terms")
                return true;
            parent = parent.hierarchy.parent;
        }
        return false;
    }
}

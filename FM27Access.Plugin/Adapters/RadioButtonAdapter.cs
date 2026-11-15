using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class RadioButtonAdapter : PluginAdapterBase
{
    public RadioButtonAdapter(VisualElement element) : base(element) { }

    public override string Role => "radio button";
    public override bool CanActivate => true;

    // A radio can host a nested sub-option group inside its own subtree (the Player Interest
    // dialog shows Interested/Persuadable/Doubtful under the selected "Transfer only"). Those
    // texts are the sub-options' labels, not this radio's: without the exclusion the parent
    // read as "Transfer only, Interested, Persuadable, Doubtful". The subgroup is its own
    // control, rendered separately on both surfaces.
    private static readonly HashSet<string> NestedGroupTypes = new() { "SIRadioGroup" };
    protected override ISet<string> ExcludedSubtreeTypeNames => NestedGroupTypes;

    public override string GetLabel()
    {
        return FindAllChildText() ?? Element.name;
    }

    public override string GetState() =>
        Element.Cast<SI.Bindable.SIRadioButton>().value ? "selected" : "unselected";

    public override void Activate() =>
        Element.Cast<SI.Bindable.SIRadioButton>().OnClickedOrSubmitted(Element);
}

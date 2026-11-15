using UnityEngine.UIElements;
namespace FM27Access.Plugin.Adapters;

// Reuse the original field-label resolver at the boundary of new FM27 widget templates.
internal sealed class FieldLabelReader:PluginAdapterBase
{
    internal FieldLabelReader(VisualElement element):base(element){}
    protected override ISet<string> ExcludedSubtreeTypeNames{get;}=new HashSet<string>(StringComparer.Ordinal)
    {"SIDropdown","FMCombobox","FMSearchableCombobox","SISlider","SIStepper","SICheckBox","SIButton","SIRadioGroup","BindableTextEditBox","BindableIntEditBox","BindableFloatEditBox"};
    public override string GetLabel()=>FindFieldLabel();
    public override string Role=>"text";
    public override bool CanActivate=>false;
    public override string GetState()=>"";
    public override void Activate()=>throw new InvalidOperationException("Label reader is not a command.");
}

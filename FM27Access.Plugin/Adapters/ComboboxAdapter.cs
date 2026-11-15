using System.Reflection;
using HarmonyLib;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ComboboxAdapter : PluginAdapterBase
{
    private static bool _initialized;
    private static PropertyInfo _dropdown;
    private static PropertyInfo _value;
    private static PropertyInfo _positiveButton;
    private static PropertyInfo _negativeButton;
    private static PropertyInfo _positiveButtonEnabled;
    private static PropertyInfo _negativeButtonEnabled;

    public ComboboxAdapter(VisualElement element) : base(element)
    {
        EnsureReflection();
    }

    public override string Role => "combobox";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        // The row's field label is the name ("Place Of Birth"); the current value becomes the
        // state. Only a combobox with no row label names itself by its value, as before.
        var fieldLabel = FindFieldLabel();
        if (fieldLabel != null) return fieldLabel;

        var valueText = GetCurrentValue();
        if (!string.IsNullOrEmpty(valueText))
            return valueText;
        return FindAllChildText() ?? Element.name;
    }

    public override string GetState()
    {
        // The live value is the state when the combobox is named by something other than its value
        // — a row label or a profile rename — so it reads "Place Of Birth, combobox, London". Only
        // a combobox still named by its own value has nothing extra to add.
        if (FindFieldLabel() != null || FM27Access.Navigation.NavigationManager.GetRenameLabel(this) != null)
            return GetCurrentValue() ?? "";
        return "";
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        var fl = FindFieldLabel();
        return string.IsNullOrEmpty(fl) ? System.Array.Empty<string>() : new[] { fl };
    }

    public override void Activate()
    {
        // Activation is handled by ComboboxHandler
    }

    public string GetCurrentValue()
    {
        var combobox = GetCombobox();
        var siText = (SI.Bindable.SIText)_value.GetValue(combobox);
        if (siText == null) return null;
        var textElement = ((VisualElement)siText).Cast<TextElement>();
        return textElement.text;
    }

    public void Increment()
    {
        var combobox = GetCombobox();
        var button = (SI.Bindable.SIButton)_positiveButton.GetValue(combobox);
        new ButtonAdapter((VisualElement)button).Activate();
    }

    public void Decrement()
    {
        var combobox = GetCombobox();
        var button = (SI.Bindable.SIButton)_negativeButton.GetValue(combobox);
        new ButtonAdapter((VisualElement)button).Activate();
    }

    public bool IsIncrementEnabled()
    {
        var combobox = GetCombobox();
        var siEnabled = (VisualElement)_positiveButtonEnabled.GetValue(combobox);
        var button = (VisualElement)_positiveButton.GetValue(combobox);
        return siEnabled != null && siEnabled.enabledInHierarchy && button != null && button.enabledInHierarchy;
    }

    public bool IsDecrementEnabled()
    {
        var combobox = GetCombobox();
        var siEnabled = (VisualElement)_negativeButtonEnabled.GetValue(combobox);
        var button = (VisualElement)_negativeButton.GetValue(combobox);
        return siEnabled != null && siEnabled.enabledInHierarchy && button != null && button.enabledInHierarchy;
    }


    public bool HasDropdownChoices()
    {
        var combobox = GetCombobox();
        var dropdown = (SI.Bindable.SIDropdown)_dropdown.GetValue(combobox);
        if (dropdown == null) return false;
        SIDropdownReflection.EnsureInitialized();
        var count = (int)SIDropdownReflection.ChoicesCount.GetValue(dropdown);
        return count > 0;
    }

    public DropdownAdapter GetInternalDropdownAdapter()
    {
        var combobox = GetCombobox();
        var dropdown = (SI.Bindable.SIDropdown)_dropdown.GetValue(combobox);
        return new DropdownAdapter((VisualElement)dropdown);
    }

    private object GetCombobox()
    {
        return new FM.UI.Widgets.FMCombobox(Element.Pointer);
    }

    private static void EnsureReflection()
    {
        if (_initialized) return;
        _initialized = true;

        var type = typeof(FM.UI.Widgets.FMCombobox);

        _dropdown = Require(type, "m_dropdown");
        _value = Require(type, "m_value");
        _positiveButton = Require(type, "m_positiveButton");
        _negativeButton = Require(type, "m_negativeButton");
        _positiveButtonEnabled = Require(type, "m_positiveButtonEnabled");
        _negativeButtonEnabled = Require(type, "m_negativeButtonEnabled");
    }

    private static PropertyInfo Require(System.Type type, string name)
    {
        var prop = AccessTools.Property(type, name);
        if (prop == null)
        {
            Speech.Log.Error($"FMCombobox.{name} property not found.");
            throw new MissingMemberException("FMCombobox", name);
        }
        return prop;
    }
}

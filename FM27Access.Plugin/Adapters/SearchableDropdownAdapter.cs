using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class SearchableDropdownAdapter : PluginAdapterBase
{
    private static bool _initialized;
    private static PropertyInfo _textEditBoxProperty;
    private static PropertyInfo _textFieldProperty;

    public SearchableDropdownAdapter(VisualElement element) : base(element)
    {
        EnsureReflection();
    }

    public override string Role => "searchable dropdown";
    public override bool CanActivate => true;

    // FM27 has two real hit targets: editable search and a chevron opening its
    // explicitly bound full list. Preserve the original searchable input and
    // expose that native list through FM26's unchanged hierarchical dropdown.
    internal DropdownAdapter FullListChooser()
    {
        var widget = Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        return widget?.m_fullDropdown != null && widget.m_fullChoicesExplicit
            && widget.m_chevronButton != null && ElementBridge.IsVisibleInHierarchy(widget.m_chevronButton)
            ? new FullListAdapter(this, widget.m_fullDropdown) : null;
    }

    private sealed class FullListAdapter : DropdownAdapter
    {
        private readonly SearchableDropdownAdapter owner;
        internal FullListAdapter(SearchableDropdownAdapter owner, VisualElement dropdown) : base(dropdown)
        { this.owner = owner; }
        public override string GetLabel() => "Browse " + owner.GetLabel();
        public override string GetState() => owner.GetState();
        public override string GetDescription() => "Browse the game's full list. Type in the neighbouring field to search.";
        public override void ShowPopup() => owner.ShowPopup();
    }

    public override string GetLabel()
    {
        // The visible row label ("Place Of Birth", "Nationality") is the accessible name, not the
        // template element id ("searchable-dropdown") the child/name fallback would otherwise give.
        return FindFieldLabel() ?? FindChildText() ?? Element.name;
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        var fl = FindFieldLabel();
        return string.IsNullOrEmpty(fl) ? System.Array.Empty<string>() : new[] { fl };
    }

    public override string GetState()
    {
        // The box's own text holds the committed entry ("London") — SelectedIndex stays -1 on
        // these dropdowns even after a commit, so the text is the only live signal of the
        // selection. An unset box keeps its placeholder outside the TextField value, so this
        // reads empty and the box announces by its row label alone.
        return GetSearchText();
    }

    public override void Activate()
    {
    }

    public void ShowPopup()
    {
        var widget=Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        if(widget!=null)
        {
            widget.m_input.m_textField.Focus();
            if (widget.m_fullDropdown.m_floatingElement?.IsVisible == true
                || widget.m_filterDropdown.m_floatingElement?.IsVisible == true) return;
            var clickEvent=ClickEvent.GetPooled();try{widget.OnChevronClicked(clickEvent);}finally{clickEvent.Dispose();}
            return;
        }
        var dropdown = GetDropdown();
        var clickable = SIDropdownReflection.Clickable.GetValue(dropdown);
        var evt = ClickEvent.GetPooled();
        SIDropdownReflection.ClickableInvoke.Invoke(clickable, new object[] { evt });
    }

    public bool IsPopupVisible()
    {
        var widget = Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        if (widget != null) return widget.m_fullDropdown.m_floatingElement?.IsVisible == true
            || widget.m_filterDropdown.m_floatingElement?.IsVisible == true;
        var dropdown = GetDropdown();
        var floatingElement = SIDropdownReflection.FloatingElement.GetValue(dropdown);
        if (floatingElement == null) return false;
        return (bool)SIDropdownReflection.FloatingElementIsVisible.GetValue(floatingElement);
    }

    public void FocusSearchField()
    {
        var textInput = FindTextInput();
        textInput.Focus();
    }

    public void BlurSearchField()
    {
        var textInput = FindTextInput();
        textInput.Blur();
    }

    public string GetSearchText()
    {
        var textField = GetTextField();
        return textField.value ?? "";
    }

    /// <summary>
    /// Sets the search box text directly, standing in for the physical keystrokes the speech
    /// surface relies on. Writing the inner TextField's value fires the UI Toolkit change event,
    /// which the BindableTextEditBox forwards to the dropdown's search binding, re-filtering the
    /// choices (same mechanism as TextFieldAdapter.SetText). The HTML surface reads the filtered
    /// list back with ReadFilteredChoices after a settle.
    /// </summary>
    public void SetSearchText(string text)
    {
        var textField = GetTextField();
        var widget=Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        if(widget!=null)
        {
            // FM27 ignores user-edit state while its input has no native focus.
            // WebView focus alone does not focus the game's search field. Leave
            // the full hierarchy before typing, as a native input edit would.
            widget.m_fullDropdown.HideDropdown();
            textField.Focus();
            // FMComboboxSearchInput.OnNewValue also updates the input's editing
            // lifecycle; calling only the parent's OnUserEditedText skips it.
            if (textField.value == (text ?? "")) widget.m_input.OnNewValue(text ?? "");
            else textField.value = text ?? "";
            return;
        }
        textField.value = text ?? "";
    }

    public List<string> ReadFilteredChoices()
    {
        var choices = new List<string>();
        var dropdown = GetDropdown();
        var streamedListObj = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        if (streamedListObj == null)
            return choices;

        VisualElement listElement;
        try
        {
            listElement = new VisualElement(((Il2CppSystem.Object)streamedListObj).Pointer);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[SearchDropdown] Failed to cast StreamedDropdownList to VisualElement: {e.GetType().Name}");
            return choices;
        }

        CollectAllTexts(listElement, choices);
        Speech.Log.Info($"[SearchDropdown] ReadFilteredChoices: {choices.Count} rendered items");
        return choices;
    }

    public int GetChoicesCount()
    {
        var dropdown = GetDropdown();
        return (int)SIDropdownReflection.ChoicesCount.GetValue(dropdown);
    }

    public void ConfirmSelection(int index)
    {
        var dropdown = GetDropdown();
        Speech.Log.Info($"[SearchDropdown] ConfirmSelection: calling TriggerSelectionEvent({index})");
        SIDropdownReflection.TriggerSelectionEvent.Invoke(dropdown, new object[] { index });
        SIDropdownReflection.HideDropdown.Invoke(dropdown, null);
    }

    public void HideDropdown()
    {
        var dropdown = GetDropdown();
        SIDropdownReflection.HideDropdown.Invoke(dropdown, null);
    }

    public static bool IsSearchable(VisualElement element)
    {
        if(element.TryCast<FM.UI.Widgets.FMSearchableCombobox>()!=null)return true;
        EnsureReflection();
        var dropdown = element.TryCast<SI.Bindable.SIDropdown>();
        if (dropdown == null) return false;
        var textEditBox = _textEditBoxProperty.GetValue(dropdown);
        return textEditBox != null;
    }

    private TextField GetTextField()
    {
        var widget=Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        if(widget!=null)return widget.m_input.m_textField;
        var dropdown = GetDropdown();
        var textEditBox = _textEditBoxProperty.GetValue(dropdown);
        if (_textFieldProperty != null)
            return (TextField)_textFieldProperty.GetValue(textEditBox);

        // Fallback: find unity-text-input in the element tree
        var editBoxElement = new VisualElement(((Il2CppSystem.Object)textEditBox).Pointer);
        var textInput = FindByName(editBoxElement, "unity-text-input");
        if (textInput == null)
        {
            Speech.Log.Error("[SearchDropdown] Could not find text input in BindableTextEditBox.");
            throw new System.InvalidOperationException("Text input not found in searchable dropdown");
        }
        return editBoxElement.Q<TextField>();
    }

    private VisualElement FindTextInput()
    {
        var widget=Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        if(widget!=null)return widget.m_input;
        var dropdown = GetDropdown();
        var textEditBox = _textEditBoxProperty.GetValue(dropdown);
        var editBoxElement = new VisualElement(((Il2CppSystem.Object)textEditBox).Pointer);
        var textInput = FindByName(editBoxElement, "unity-text-input");
        if (textInput == null)
        {
            Speech.Log.Error("[SearchDropdown] Could not find unity-text-input in BindableTextEditBox.");
            throw new System.InvalidOperationException("unity-text-input not found in searchable dropdown");
        }
        return textInput;
    }

    private object GetDropdown()
    {
        var widget=Element.TryCast<FM.UI.Widgets.FMSearchableCombobox>();
        // The chevron opens the full hierarchy even when the input already has
        // search text. m_hasSearchText is not the popup/session identity.
        if(widget!=null)return widget.m_fullOpen ? widget.m_fullDropdown
            : widget.m_filterOpen || widget.m_hasSearchText ? widget.m_filterDropdown : widget.m_fullDropdown;
        return Element.TryCast<SI.Bindable.SIDropdown>()
            ?? throw new System.InvalidOperationException(
                $"The native {Element.GetIl2CppType().Name} control is not a searchable SIDropdown. Refresh the screen.");
    }

    private static void EnsureReflection()
    {
        if (_initialized) return;
        _initialized = true;

        SIDropdownReflection.EnsureInitialized();

        var dropdownType = typeof(SI.Bindable.SIDropdown);

        _textEditBoxProperty = dropdownType.GetProperty("m_textEditBox");
        if (_textEditBoxProperty == null)
        {
            Speech.Log.Error("SIDropdown.m_textEditBox property not found.");
            throw new System.MissingMemberException("SIDropdown", "m_textEditBox");
        }

        var editBoxType = typeof(SI.Bindable.BindableTextEditBox);
        _textFieldProperty = editBoxType.GetProperty("m_textField");
        if (_textFieldProperty == null)
            Speech.Log.Warn("[SearchDropdown] BindableTextEditBox.m_textField property not found. Will use DOM fallback.");
    }
}

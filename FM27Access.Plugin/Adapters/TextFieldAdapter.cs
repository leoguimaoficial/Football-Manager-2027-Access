using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class TextFieldAdapter : PluginAdapterBase
{
    public TextFieldAdapter(VisualElement element) : base(element)
    {
        PrivateFieldCache.EnsureInitialized();
    }

    public override string Role => "text field";
    public override bool CanActivate => true;

    internal bool IsPassword => Element.TryCast<SI.Bindable.BindableTextEditBox>()?.IsPassword == true
        || Element.TryCast<TextField>()?.isPasswordField == true;

    public override string GetLabel()
    {
        // The row's field label ("First Name") is the accessible name. Only a box with no row
        // label falls back to naming itself by its typed value, so the speech cursor still reads
        // the contents of an unlabelled box rather than the internal "Box" element name.
        var fieldLabel = FieldLabel();
        if (fieldLabel != null) return fieldLabel;
        if (IsPassword) return "Password";

        if (IsBindableTextEditBox() && PrivateFieldCache.TextEditBoxTextField != null)
        {
            var textField = (TextField)PrivateFieldCache.TextEditBoxTextField.GetValue(
                Element.Cast<SI.Bindable.BindableTextEditBox>());
            var value = textField.value;
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        var plainTextField = Element.TryCast<TextField>();
        if (plainTextField != null && !string.IsNullOrEmpty(plainTextField.value))
            return plainTextField.value;

        return FindChildText() ?? Element.name;
    }

    // The row's field label, or null when the box has none. The HTML surface adds it as the
    // input's aria-label only when present, so a label-less box stays unnamed rather than echoing
    // its typed value as a name. Two decorations complete the sighted view: the placeholder
    // ("Preferred First Name") distinguishes template-twin boxes that share one row label
    // (the creation flow's two "Address Me As" boxes), and a box inside the game's
    // "optional-fields" group is marked "(optional)" — the sighted cue is the group's own
    // "Optional:" caption, which reads poorly as a stray stop and is profile-hidden there.
    internal virtual string FieldLabel()
    {
        var label = FindFieldLabel();
        var placeholder = GetPlaceholder();
        // Some FM27 templates provide only a placeholder. The shared file-browser
        // template provides neither: its EditBox is the native filename/search field
        // in Save, Load and other browser panels, not a screen-specific command.
        if (string.IsNullOrWhiteSpace(label))
        {
            if (!string.IsNullOrWhiteSpace(placeholder)) return placeholder;
            return Element.name == "EditBox" && HasAncestorNamed("BrowseFilesSearchAndTypeElement") ? "File name" : null;
        }
        if (!string.IsNullOrEmpty(placeholder) &&
            !string.Equals(placeholder, label, StringComparison.OrdinalIgnoreCase))
            label = $"{label}, {placeholder}";

        if (HasAncestorNamed("optional-fields"))
            label += " (optional)";

        return label;
    }

    private string GetPlaceholder()
    {
        try
        {
            TextField textField = null;
            if (IsBindableTextEditBox() && PrivateFieldCache.TextEditBoxTextField != null)
                textField = (TextField)PrivateFieldCache.TextEditBoxTextField.GetValue(
                    Element.Cast<SI.Bindable.BindableTextEditBox>());
            else
                textField = Element.TryCast<TextField>();

            return textField?.textEdition.placeholder ?? "";
        }
        catch (Exception e)
        {
            // A placeholder is supplementary; a failed read keeps the plain row label.
            Speech.Log.Warn($"[TextField] placeholder read failed: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }

    private bool HasAncestorNamed(string name)
    {
        var current = Element.hierarchy.parent;
        for (int depth = 0; depth < 24 && current != null; depth++)
        {
            if (current.name == name) return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        var fl = FindFieldLabel();
        return string.IsNullOrEmpty(fl) ? System.Array.Empty<string>() : new[] { fl };
    }

    // The field's current typed text, or empty when the field is empty. Unlike GetLabel this
    // never falls back to the element's internal name ("Box"), so the HTML surface can render an
    // accurate value attribute that is genuinely blank for an empty box.
    public virtual string GetCurrentText()
    {
        if (IsBindableTextEditBox() && PrivateFieldCache.TextEditBoxTextField != null)
        {
            var textField = (TextField)PrivateFieldCache.TextEditBoxTextField.GetValue(
                Element.Cast<SI.Bindable.BindableTextEditBox>());
            return textField.value ?? "";
        }

        var plainTextField = Element.TryCast<TextField>();
        return plainTextField != null ? (plainTextField.value ?? "") : "";
    }

    public override string GetState()
    {
        return IsPassword ? "password field" : "";
    }

    public override string GetAnnouncement()
    {
        var label = GetLabel();
        var state = GetState();

        if (!string.IsNullOrEmpty(state))
            return $"{Role}, {label}, {state}";

        return $"{Role}, {label}";
    }

    public override void Activate() => FindTextInput().Focus();

    public void Deactivate() => FindTextInput().Blur();

    /// <summary>
    /// Sets the field's text directly. Writing the inner TextField's value fires the
    /// UI Toolkit change event, which the BindableTextEditBox forwards to its data binding.
    /// Returns false if no settable TextField was found (so the caller can avoid
    /// confirming a change that did not happen).
    /// </summary>
    public virtual bool SetText(string text)
    {
        if (IsBindableTextEditBox() && PrivateFieldCache.TextEditBoxTextField != null)
        {
            var textField = (TextField)PrivateFieldCache.TextEditBoxTextField.GetValue(
                Element.Cast<SI.Bindable.BindableTextEditBox>());
            textField.value = text;
            return true;
        }

        var plainTextField = Element.TryCast<TextField>();
        if (plainTextField != null)
        {
            plainTextField.value = text;
            return true;
        }

        Speech.Log.Warn("[TextField] No settable TextField found for SetText.");
        return false;
    }

    private VisualElement FindTextInput()
    {
        return FindByName(Element, "unity-text-input");
    }

    private bool IsBindableTextEditBox()
    {
        return Element.TryCast<SI.Bindable.BindableTextEditBox>() != null;
    }
}

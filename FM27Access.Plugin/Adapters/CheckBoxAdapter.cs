using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class CheckBoxAdapter : PluginAdapterBase
{
    public CheckBoxAdapter(VisualElement element) : base(element) { }

    public override string Role => "checkbox";
    public override bool CanActivate => true;

    // Sibling widgets are their own controls: their internals (a value stepper's "15, -, +", a
    // comparator dropdown's selection) must not fold into this checkbox's name. The player search
    // condition row is the canonical case — without this its enable-checkbox reads the whole row.
    protected override ISet<string> ExcludedSubtreeTypeNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "SIDropdown", "SIStepper", "SISlider", "FMCombobox",
        "BindableIntEditBox", "BindableFloatEditBox", "BindableTextEditBox",
        "SIButton", "SICheckBox",
    };

    public override string GetLabel()
    {
        // FM27's hybrid task card owns its title and explanation. The form's
        // question above the card group is not the name of every choice.
        if (IsLabelledCard())
        {
            var cardText = FindAllChildText();
            if (!string.IsNullOrWhiteSpace(cardText)) return cardText;
        }
        // FM27 moved the switch's On/Off text inside a new label wrapper. Start the
        // original field-label walk outside that wrapper, so the name remains the field.
        if(IsStateSwitch())
        {
            // FM27 also puts the On/Off SIText and the switch template in a
            // small Frame. Start outside that state-only row to reach the
            // setting's own caption, rather than naming every switch "Yes".
            var template = Element.hierarchy.parent;
            var stateRow = template?.hierarchy.parent;
            if (template?.name?.StartsWith(SwitchNamePrefix, StringComparison.Ordinal) == true
                && stateRow?.name == "Frame" && stateRow.hierarchy.childCount == 2
                && stateRow.hierarchy[0].GetIl2CppType().Name == "SIText"
                && stateRow.hierarchy[1].Pointer == template.Pointer)
            {
                var name = new FieldLabelReader(stateRow).GetLabel();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            for(var parent=Element.hierarchy.parent;parent!=null;parent=parent.hierarchy.parent)
                if(parent.name=="inputs-switch-toggle-label")
                {
                    // This template also carries real field captions (for
                    // example the advice group's all-topics switch). Only a
                    // constant native localized binding is a caption here;
                    // the conditional On/Off function remains a value, so it
                    // must still resolve its field outside this wrapper.
                    for (int i = 0; i < parent.hierarchy.childCount; i++)
                    {
                        var text = parent.hierarchy[i];
                        if (!ElementBridge.IsVisible(text)) continue;
                        var caption = StaticButtonCaption.ReadText(text);
                        if (!string.IsNullOrWhiteSpace(caption)) return caption;
                    }
                    var name=new FieldLabelReader(parent).GetLabel();
                    if(!string.IsNullOrWhiteSpace(name))return name;
                    break;
                }
        }
        // A plain native checkbox can own a localized SIText while Toggle.label
        // is empty. That caption names the choice before an enclosing group title
        // (Quick Start's unemployment choice is inside the team-selection group).
        var ownCaption = OwnLocalizedCaption();
        if (ownCaption != null) return ownCaption;

        // A text sharing the checkbox's own container is its row label — the game writes these
        // option rows as "[box] Use current match tactics", with the label AFTER the box. It has to
        // beat FindFieldLabel, which only ever accepts PRECEDING siblings and so climbs out of the
        // row and adopts the enclosing group's title: every box under "Tactics and Team Selection"
        // read back as that title, and the trailing label was folded away as consumed, so the real
        // option name reached the player nowhere at all.
        var rowLabel = FindParallelColumnHeaderLabel() ?? FindDirectSiblingText()
            ?? (IsStateSwitch() ? null : FindWrappedRowText());
        if (!string.IsNullOrEmpty(rowLabel))
            return rowLabel;

        // The row's field label is the accessible name. It is preferred over the toggle's own
        // label because a settings toggle binds its visible status ("On") to that label, which
        // names the row's value, not the row — so "Match Sounds" must win over "On".
        var fieldLabel = FindFieldLabel();
        if (fieldLabel != null)
            return fieldLabel;

        var label = Element.Cast<Toggle>().label;
        if (!string.IsNullOrEmpty(label))
            return label;

        // All visible child texts, not just the first: a card-style checkbox (the creation
        // flow's Style/Personality cards) carries its title and its description as separate
        // texts, and the description is real content — matching RadioButtonAdapter.
        var childText = FindAllChildText();
        if (childText != null)
            return childText;

        // The visible sibling label (e.g. "Use Auto Saves") is a better accessible name than
        // the checkbox's internal element name, which is often a template id like
        // "CheckboxDefault". Prefer it; fall back to the element name only when no sibling text.
        var siblingText = FindSiblingText();
        if (!string.IsNullOrEmpty(siblingText))
            return siblingText;

        if (!string.IsNullOrEmpty(Element.name) && !Element.name.Contains('-'))
            return Element.name;

        return null;
    }

    private string OwnLocalizedCaption()
    {
        // Switch captions can be On/Off values; cards retain their complete title
        // and explanation above. Only one direct, visible, constant caption qualifies.
        if (IsStateSwitch()) return null;
        string caption = null;
        for (int i = 0; i < Element.hierarchy.childCount; i++)
        {
            var child = Element.hierarchy[i];
            if (!ElementBridge.IsVisible(child) || child.GetIl2CppType().Name != "SIText") continue;
            if (caption != null) return null;
            caption = StaticButtonCaption.ReadText(child);
            if (string.IsNullOrWhiteSpace(caption)) return null;
        }
        return caption;
    }

    // An on/off switch (the game names them "inputs-switch-toggle-…": Preferences' "Match Sounds",
    // Match Day's "Dynamic Mode") shows its own state beside it, "On"/"Off". The text beside its
    // wrapper is that state, not its name, so the wrapped-row label is for checkboxes only (the Data
    // Hub's "Set Preferred" is "CheckboxDefault"); a switch keeps the field-label path, which names it
    // by its setting row. Without this every Preferences switch read "On, checkbox".
    private const string SwitchNamePrefix = "inputs-switch-toggle";

    private bool IsStateSwitch() => Element.name?.StartsWith(SwitchNamePrefix, StringComparison.Ordinal) == true;

    private bool IsLabelledCard() => FindByName(Element, "inputs-hybrid-task_switch_button-Large") != null;

    public override string GetState() =>
        (ManagerStyleSelection.TryRead(Element.TryCast<SI.Bindable.SICheckBox>(), out var selected)
            ? selected : Element.Cast<Toggle>().value) ? "checked" : "unchecked";

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        var list = new System.Collections.Generic.List<string>();
        void Add(string s) { if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s); }
        Add(GetLabel());                    // the row label, folded as the accessible name
        // A settings toggle's visible status ("On") sits beside it as a stray Text stop — bound to
        // the toggle's label on some, a plain sibling on others. Fold both forms so the status does
        // not read as a separate line above the checkbox.
        Add(Element.Cast<Toggle>().label);
        if (!IsLabelledCard() && FindParallelColumnHeaderLabel() == null) Add(FindSiblingText());
        return list;
    }

    public override void Activate()
    {
        var checkbox = Element.Cast<SI.Bindable.SICheckBox>();
        if (ManagerStyleSelection.TryRead(checkbox, out var selected) && checkbox.Value != selected)
            // Value synchronizes the widget without Checked/Unchecked events.
            // Its normal native click then removes an existing style instead
            // of adding the same index twice to the game's selection list.
            checkbox.Value = selected;
        checkbox.SimulateCheckboxClick();
    }
}

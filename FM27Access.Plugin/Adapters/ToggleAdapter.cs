using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ToggleAdapter : PluginAdapterBase
{
    public ToggleAdapter(VisualElement element) : base(element) { }

    // A row-selector in a SINGLE-select table (the staff-meeting advice pattern) is not a stateful
    // checkbox: clicking it selects the row and opens a disclosure (context + Implement buttons), and
    // only one row is "on" at a time. Presenting it as a checkbox made the companion's native
    // checkbox handling short-circuit the interaction — pressing Space announced "checked" locally and
    // the game's real disclosure never surfaced. So single-select selectors present as a button (in
    // both the companion render and the speech cursor); genuine multi-select tables (squad pickers,
    // SelectionMode Many/AtLeastOne) keep the real checkbox, where the on/off state is meaningful.
    public override string Role => IsSingleSelectDisclosure ? "button" : "checkbox";
    public override bool CanActivate => true;

    private SI.UI.VirtualisedList.SelectionModes? _selectionMode;
    private bool _selectionModeRead;
    internal bool IsSingleSelectDisclosure
    {
        get
        {
            var mode = SelectionMode();
            return mode != null
                && mode != SI.UI.VirtualisedList.SelectionModes.Many
                && mode != SI.UI.VirtualisedList.SelectionModes.AtLeastOne;
        }
    }

    internal bool IsManySelectRowSelector => SelectionMode() == SI.UI.VirtualisedList.SelectionModes.Many;

    // A SelectionCell toggle in a Many/AtLeastOne list is a genuine independently-toggleable row
    // selector (the team-selection / squad-picker pattern). Server CellHandle uses this distinction
    // to confirm that the game's selection actually changed before telling the companion to re-read
    // the table. Without that confirmation a synthetic click could be swallowed while the native
    // HTML checkbox looked checked briefly, then snap back on the optimistic body refresh.
    internal bool IsMultiSelectRowSelector => InSelectionCell() && !IsSingleSelectDisclosure;

    // Read live off the bound Toggle every time, never remembered: this is what the confirmation
    // wait polls, so a cached copy would confirm its own stale answer.
    internal bool IsChecked => Element.Cast<Toggle>().value;

    private SI.UI.VirtualisedList.SelectionModes? SelectionMode()
    {
        if (_selectionModeRead) return _selectionMode;
        _selectionModeRead = true;
        VisualElement selectionCell = null;
        VisualElement virtList = null;
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            var typeName = current.GetIl2CppType().Name;
            if (typeName == "SelectionCell") selectionCell = current;
            else if (typeName == "VirtualisedList") virtList = current;
            current = current.hierarchy.parent;
        }
        if (selectionCell == null || virtList == null) return null; // a settings toggle, not a row selector
        try
        {
            _selectionMode = new SI.UI.VirtualisedList(virtList.Pointer).SelectionMode;
        }
        catch (System.Exception ex)
        {
            // Degrade in place: an unreadable SelectionMode falls back to the checkbox presentation (a
            // visible, if less ideal, control) rather than breaking the whole panel render.
            Speech.Log.Warn($"[Toggle] SelectionMode read failed, presenting as checkbox: {ex.Message}");
        }
        return _selectionMode;
    }

    public override string GetLabel()
    {
        // A Select checkbox inside a table SelectionCell identifies the ROW it toggles, not a
        // settings field. Scope its name to its own row's text (the role name in the role table)
        // so each row's checkbox reads its own role, instead of the field-label resolver
        // over-reaching across rows and naming every checkbox after the first row.
        var rowLabel = FindSelectionRowLabel();
        if (rowLabel != null)
            return rowLabel;

        // The row's field label is the accessible name, ahead of the toggle's own label (which a
        // settings toggle binds to its visible status text, naming the value rather than the row).
        var fieldLabel = FindFieldLabel();
        if (fieldLabel != null)
            return fieldLabel;

        var toggle = Element.Cast<Toggle>();
        if (!string.IsNullOrEmpty(toggle.label))
            return toggle.label;

        var childText = FindChildText();
        if (childText != null)
            return childText;

        if (!string.IsNullOrEmpty(Element.name))
            return Element.name;

        return FindSiblingText();
    }

    // A single-select disclosure selector reports "selected" only on the row the game currently has
    // on (SelectionCell syncs its toggle from the list's single selection), and stays silent on the
    // rest — the radio-dot state a sighted player sees, without the checked/unchecked binary a
    // checkbox would wrongly imply is independently togglable.
    public override string GetState() =>
        IsSingleSelectDisclosure ? (IsChecked ? "selected" : null)
                                 : (IsChecked ? "checked" : "unchecked");

    // When this toggle is a table row-selector (inside a SelectionCell), its accessible name is the
    // entity the row selects, not the row's whole stat line. The SelectionCell holds only the
    // checkmark; the row's name cell carries the entity link ("NameText"/"NameButton") and the next
    // text cell its position/detail, so the checkbox reads "Maxim De Cuyper, D/WB/M (L)" — what it
    // selects, concisely. Falls back to joining the row's sibling text when the row has no name cell
    // (e.g. a file table). Returns null when not inside a SelectionCell (a settings toggle), or when
    // the row carries no text, leaving the field-label path to name it.
    private string FindSelectionRowLabel()
    {
        VisualElement selectionCell = null;
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "SelectionCell") { selectionCell = current; break; }
            current = current.hierarchy.parent;
        }
        if (selectionCell == null) return null;

        var row = selectionCell.hierarchy.parent;
        if (row == null) return null;

        int count = row.hierarchy.childCount;
        int nameIdx = -1;
        for (int i = 0; i < count; i++)
        {
            if (row.hierarchy[i].Pointer == selectionCell.Pointer) continue;
            if (!ElementBridge.IsVisible(row.hierarchy[i])) continue;
            if (ElementBridge.HasNameDescendant(row.hierarchy[i])) { nameIdx = i; break; }
        }

        if (nameIdx < 0)
        {
            var texts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++)
            {
                var cell = row.hierarchy[i];
                if (cell.Pointer == selectionCell.Pointer || !ElementBridge.IsVisible(cell)) continue;
                CollectAllTexts(cell, texts);
            }
            return texts.Count > 0 ? string.Join(", ", texts) : null;
        }

        // The entity link's own text, not the cell's leading text: a squad kit cell renders the
        // shirt number before the name, and "14" is no identity.
        var nameEl = ElementBridge.FindNameDescendant(row.hierarchy[nameIdx]);
        string name = nameEl != null ? FirstCellText(nameEl) : null;
        if (string.IsNullOrEmpty(name)) name = FirstCellText(row.hierarchy[nameIdx]);
        if (string.IsNullOrEmpty(name)) return null;
        // Detail comes only from plain-text cells (the squad picker's position column). A cell
        // holding a control would bake that control's LIVE value into the checkbox name — the
        // opposition instructions row read "Omar Pierce, Always" (its Tight Marking dropdown's
        // value), an identity label that goes stale the moment the player edits the row.
        for (int i = nameIdx + 1; i < count; i++)
        {
            if (!ElementBridge.IsVisible(row.hierarchy[i])) continue;
            if (CellContainsControl(row.hierarchy[i])) continue;
            var detail = FirstCellText(row.hierarchy[i]);
            if (!string.IsNullOrEmpty(detail))
                return name + ", " + detail;
        }
        return name;
    }

    private static bool CellContainsControl(VisualElement cell)
    {
        var typeName = cell.GetIl2CppType().Name;
        if (typeName is "SIDropdown" or "FMCombobox" or "SIButton" or "SICheckBox" or "ExpandedToggle" or "SISlider")
            return true;
        int n = cell.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (CellContainsControl(cell.hierarchy[i])) return true;
        return false;
    }

    private static string FirstCellText(VisualElement cell)
    {
        var texts = new System.Collections.Generic.List<string>();
        CollectAllTexts(cell, texts);
        return texts.Count > 0 ? texts[0] : null;
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        var list = new System.Collections.Generic.List<string>();
        void Add(string s) { if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s); }
        Add(GetLabel());
        Add(Element.Cast<Toggle>().label);
        Add(FindSiblingText());
        return list;
    }

    public override void Activate()
    {
        // A toggle inside a table SelectionCell is an ExpandedToggle row-selector: activating it selects
        // the row and, on advice screens (staff meetings), opens the row's disclosure (the coach's
        // context plus its Implement / Apply-all-advice actions).
        //
        // For a single-select disclosure row, drive the game's own index-addressed selection API rather
        // than a synthesised pointer. A pointer click fires the ExpandedToggle's Clickable, which
        // VALIDATES the pointer position; but the CellHandle resolves this element by re-scrolling to a
        // data index, and under virtualisation the resolved row's worldBound is often stale (a recycled
        // slot, or off the table after the shared scroll shifts), so the click lands off the element and
        // the game silently drops it — the row never selects. VirtualisedList.Controller.UpdateSelection
        // is the same selection entry the click ultimately reaches, keyed by data index — so it is
        // immune to worldBound staleness, occlusion, and shared-scroll drift (the PitchMoveEngine
        // pattern: drive the game's own subsystem, not faked input). The row element carries both its
        // data Index and its owning ParentList, so no scroll or hit-test is needed.
        if (InSelectionCell())
        {
            bool single = IsSingleSelectDisclosure;
            if (single || IsManySelectRowSelector)
            {
                var rowEl = TableRowTextAdapter.FindTableRow(Element);
                if (rowEl != null)
                {
                    for(var ancestor=rowEl.hierarchy.parent;ancestor!=null;ancestor=ancestor.hierarchy.parent)
                    {
                        var table=ancestor.TryCast<SI.Bindable.StreamedTable>();
                        if(table==null)continue;
                        var list=table.ListElement;
                        int index=list.Controller.GetActiveElementIndex(rowEl);
                        if(index>=0){list.Controller.UpdateSelection(!single,false,index);return;}
                        break;
                    }
                }
            }
            // AtLeastOne pickers, or an unexpected template with no row/list: keep
            // the synthetic pointer, which honours the list's multi-select mode natively.
            SyntheticPointer.LeftClick(Element);
            return;
        }

        // Fallback for non-table toggles (e.g. a settings switch): flip the bound value.
        var toggle = Element.Cast<Toggle>();
        toggle.value = !toggle.value;
    }

    private bool InSelectionCell()
    {
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "SelectionCell") return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

}

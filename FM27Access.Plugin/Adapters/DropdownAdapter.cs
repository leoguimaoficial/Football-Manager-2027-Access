using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class DropdownAdapter : PluginAdapterBase
{
    private static bool _initialized;
    private static PropertyInfo _choicesListProperty;
    private static MethodInfo _doesChoiceHaveSublistMethod;
    private static PropertyInfo _childrenDropdownsProperty;
    private static MethodInfo _showDropdownMethod;
    private static MethodInfo _onSelectionChangedInternalMethod;

    public DropdownAdapter(VisualElement element) : base(element)
    {
        EnsureReflection();
    }

    public override string Role => "dropdown";
    public override bool CanActivate => true;
    internal bool IsCardOptions => Element.name == "swap-tile-dropdown";

    public override string GetLabel()
    {
        // FM27's dashboard grid exposes a menu for each tile, beside the tile
        // rather than inside its title. Name it from that same live grid item.
        if (IsCardOptions)
            for (var parent = Element.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetIl2CppType().Name == "Panel") break;
                if (parent.GetIl2CppType().Name != "BindableVariableSizedGridElement") continue;
                var title = ElementBridge.FindVisibleTileTitle(parent)?.TryCast<TextElement>()?.text;
                if (!string.IsNullOrWhiteSpace(title)) return "Customize card: " + title;
                break;
            }
        // A button-styled menu dropdown carries its own name as a dedicated "label" text inside its
        // SelectionField (the main menu's "More" menu), unlike a settings-row dropdown whose
        // SelectionField holds the current VALUE (name="text"). That self-label IS the name and must
        // win over field-label resolution, which would otherwise walk up into the screen chrome and
        // adopt the page title or version string ("FM26", "26.3.2").
        var selfLabel = FindSelectionFieldLabel();
        if (selfLabel != null) return selfLabel;

        // A divided control names itself by its paired button (the Tactical Planner "Quick Pick"
        // box). The field-label walk cannot resolve it — the paired button is a control, so the
        // walk skips it and over-reaches to a distant sibling — so this must run before it.
        var dividedLabel = FindDividedControlLabel();
        if (dividedLabel != null) return dividedLabel;

        // The squad-table player cell pairs a name-link button with the swap dropdown; the
        // demoted button's text ("G. Gustave") is the dropdown's name. Must run before the
        // field-label walk, which would otherwise over-reach to the shirt-number leaf and
        // read the whole cell as a bare "1".
        var pairedLabel = FindPairedNameButtonLabel();
        if (pairedLabel != null) return pairedLabel;

        // A settings/form row's field label is the accessible name ("Currency"); the live
        // selection becomes the value (see GetState). Only when the dropdown has no row label
        // does it fall back to naming itself by its current value, as a bare toolbar dropdown does.
        var fieldLabel = FindFieldLabel();
        if (fieldLabel != null) return fieldLabel;

        var text = FindAllChildText();
        if (text != null) return text;

        var nativeValue = StaticButtonCaption.ReadDropdownValue(Element.TryCast<SI.Bindable.SIDropdown>());
        if(nativeValue!=null)return nativeValue;

        var siblingText = FindSiblingText();
        if (siblingText != null) return siblingText;

        var tooltip = Element.tooltip;
        if (!string.IsNullOrEmpty(tooltip)) return tooltip;

        var splitButtonText = FindSplitButtonSiblingText();
        if (splitButtonText != null) return splitButtonText;

        var name = Element.name;
        if (!string.IsNullOrEmpty(name) && !name.Contains('-') && !LooksLikeTemplateId(name))
            return name;

        return Speech.Degrade.UnknownControl;
    }

    public override string GetDescription() => IsCardOptions
        ? "Choose the content shown in this card from the alternatives supplied by the game."
        : base.GetDescription();

    // True when GetLabel fell back to naming this dropdown by its own displayed value (the child
    // text of its SelectionField) — the bare-toolbar pattern, with no self label, paired button,
    // divided control, or field label to name it. A table cell dropdown in this state reads
    // uselessly ("-, 1 of 3" on the opposition-instructions grid), so the table renderer relabels
    // it by its column header and shows the borrowed value as the value.
    internal bool IsValueNamed() =>
        FindSelectionFieldLabel() == null && FindDividedControlLabel() == null
        && FindPairedNameButtonLabel() == null && FindFieldLabel() == null
        && (FindAllChildText() != null || FindSplitButtonSiblingText() != null);

    // FM27 also draws a table value OUTSIDE its arrow-only SelectionField.
    // It can be a localized constant (the unselected position placeholder), so
    // translation binding alone does not make it a field caption. The actual
    // native Cell owns this face; the table's column owns its accessible name.
    // Explicit menu/divided/player-name captions and ordinary forms keep their
    // existing naming contract. No caption, player or position allowlist.
    internal bool HasExternalTableValue() => ExternalTableValueContainer() != null;

    private VisualElement ExternalTableValueContainer()
    {
        if (FindSelectionFieldLabel() != null || FindDividedControlLabel() != null
            || FindPairedNameButtonLabel() != null || FindAllChildText() != null) return null;
        var face = FindByName(Element, "SelectionField");
        if (face == null || face.hierarchy.childCount != 1
            || face.hierarchy[0].name != "DropdownArrow") return null;
        if (FindFieldLabel() == null || FieldLabelContainer == null) return null;
        var values = new List<string>();
        CollectAllTexts(FieldLabelContainer, values);
        if (values.Count != 1) return null;
        var cell = Element.hierarchy.parent;
        for (int depth = 0; depth < 12 && cell != null; depth++, cell = cell.hierarchy.parent)
        {
            var kind = cell.GetIl2CppType().Name;
            if (kind == "Panel") return null;
            if (kind != "Cell") continue;
            for (var label = FieldLabelContainer; label != null; label = label.hierarchy.parent)
            {
                if (label.Pointer == cell.Pointer) return FieldLabelContainer;
                if (label.GetIl2CppType().Name is "Cell" or "Panel") return null;
            }
            return null;
        }
        return null;
    }

    internal static void FoldExternalTableValues(List<Navigation.IAccessibleElement> stops)
    {
        DropdownAdapter dropdown = null;
        foreach (var stop in stops)
        {
            if (!stop.CanActivate) continue;
            if (dropdown != null || stop is not DropdownAdapter candidate) return;
            dropdown = candidate;
        }
        if (dropdown == null || !string.IsNullOrEmpty(Navigation.NavigationManager.GetRenameLabel(dropdown))
            || string.IsNullOrWhiteSpace(Contexts.TableContext.ReadColumnLabel(dropdown.BackingElement))) return;
        var container = dropdown.ExternalTableValueContainer();
        if (container == null) return;
        // Fold by actual native ancestry, never by equal text. Unrelated values
        // and every other control remain. Both speech and HTML share this list.
        stops.RemoveAll(stop =>
        {
            if (stop.CanActivate || stop is not PluginAdapterBase adapter
                || adapter.BackingElement.TryCast<TextElement>() == null) return false;
            for (var element = adapter.BackingElement; element != null; element = element.hierarchy.parent)
            {
                if (element.Pointer == container.Pointer) return true;
                if (element.GetIl2CppType().Name is "Cell" or "Panel") return false;
            }
            return false;
        });
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        // A self-labelled menu dropdown folds nothing: its name comes from inside itself, so there is
        // no stray sibling label to suppress — and the chrome text the field-label walk would return
        // (the page title/version) is real screen content that must stay.
        if (FindSelectionFieldLabel() != null) return System.Array.Empty<string>();
        // A divided control names itself from its paired button (a real, separate stop), so there is
        // no stray sibling label to suppress — and the distant text the field-label walk would return
        // is unrelated screen content (the next fixture) that must stay on screen.
        if (FindDividedControlLabel() != null) return System.Array.Empty<string>();
        // A paired-button-named dropdown folds nothing: the texts it drew on (the demoted name
        // button, the shirt badge) are internal children that never render as stops anyway.
        if (FindPairedNameButtonLabel() != null) return System.Array.Empty<string>();
        var fl = FindFieldLabel();
        return string.IsNullOrEmpty(fl) ? System.Array.Empty<string>() : new[] { fl };
    }

    // A button-styled dropdown's own name: an SIText named "label" inside its SelectionField (the
    // main menu "More" menu reads "More"). A settings-row dropdown instead holds its VALUE there
    // (name="text"), so this returns null for it and field-label resolution proceeds. Verified on
    // Preferences: every settings dropdown's SelectionField uses name="text", only menu dropdowns
    // use name="label".
    // A named menu button has no separate selected-value field. The database
    // selector has a secondary value and remains a value-changing dropdown.
    public bool IsActionMenu => FindSelectionFieldLabel()!=null && FindSelectionFieldSecondary()==null;

    private string FindSelectionFieldLabel()
    {
        // FM27's leading-icon value template binds the selected choice into
        // "label" (roles/focus in training's shared table), rather than "text".
        // It is not a static menu title. Let the existing value-name path and
        // column-header relabeling handle it, including value confirmation.
        if (Element.name == "dropdown-base-regular-leading-icon-label") return null;
        var selectionField = FindByName(Element, "SelectionField");
        if (selectionField == null) return null;
        var labelEl = FindByName(selectionField, "label");
        // FM27 also leaves a menu's fixed localized SIText unnamed. Verify
        // its own constant TranslationID binding, so an ordinary bound value
        // never becomes the field name and a nearby table header is not borrowed.
        if (labelEl == null)
        {
            string staticCaption = null;
            for (int i = 0; i < selectionField.hierarchy.childCount; i++)
            {
                var child = selectionField.hierarchy[i];
                if (!string.IsNullOrEmpty(child.name) || !ElementBridge.IsVisible(child)) continue;
                var caption = StaticButtonCaption.ReadText(child);
                if (caption == null) continue;
                if (staticCaption != null) return null;
                staticCaption = caption;
            }
            if (staticCaption != null) return staticCaption;
        }
        // FM27's contextual navigation template uses an unnamed caption.
        // It is a menu title, not the value of its last chosen destination.
        if (labelEl == null && Element.name == "SecondaryTabDropdownButton") labelEl = selectionField;
        if (labelEl == null) return null;
        var texts = new System.Collections.Generic.List<string>();
        CollectAllTexts(labelEl, texts);
        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    // A self-labelled dropdown's live VALUE: an SIText named "secondary" sitting beside the "label"
    // inside the SelectionField. The database-size box (template
    // "dropdown-base-regular-trailing-icon-label-secondary") names the field in "label" ("Total
    // Player Count") and shows the live count in "secondary" ("44690"); the plain menu dropdowns
    // (the main-menu "More") carry no "secondary", so this returns null for them.
    private string FindSelectionFieldSecondary()
    {
        var selectionField = FindByName(Element, "SelectionField");
        if (selectionField == null) return null;
        var secondaryEl = FindByName(selectionField, "secondary");
        if (secondaryEl == null) return null;
        var texts = new System.Collections.Generic.List<string>();
        CollectAllTexts(secondaryEl, texts);
        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    // A "divided control" pairs a labelled SIButton with a bare dropdown-arrow as separate children
    // of a shared ContextShortcutsElement — the Tactical Planner "Quick Pick" box, where the button
    // runs the default quick pick and the arrow opens its alternatives. The dropdown's own
    // SelectionField holds only the arrow (no value, no label), so its accessible name is the paired
    // button's text ("Quick Pick"). Without this the field-label walk skips the button — a control,
    // which LabelSiblingText rejects — and over-reaches to a distant sibling, mislabelling the
    // dropdown with the next fixture ("York United (A)"). Returns null for every dropdown not sitting
    // directly inside a ContextShortcutsElement, so ordinary dropdowns are untouched.
    private string FindDividedControlLabel()
    {
        var current = Element;
        for (int depth = 0; depth < 4; depth++)
        {
            var parent = current.hierarchy.parent;
            if (parent == null) return null;

            string type;
            try { type = parent.GetIl2CppType().Name; }
            catch { type = parent.name; }
            if (type == "ContextShortcutsElement")
            {
                int count = parent.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    var sibling = parent.hierarchy[i];
                    if (sibling.Pointer == current.Pointer) continue;
                    if (!ElementBridge.IsVisible(sibling)) continue;
                    var texts = new System.Collections.Generic.List<string>();
                    CollectAllTexts(sibling, texts);
                    if (texts.Count > 0) return string.Join(", ", texts);
                }
                return null;
            }
            current = parent;
        }
        return null;
    }

    // The squad-table player cell pairs a name-link SIButton with the player-swap dropdown as
    // sibling subtrees; ElementBridge demotes that button (HasSiblingSubtreeWithDropdown) so the
    // pair reads as one control — which makes the button's text ("G. Gustave") this dropdown's
    // accessible name. The cell's shirt-number badge is deliberately not included (the player
    // does not want it). Returns null when no demoted paired button is in reach, so every other
    // dropdown is untouched.
    private string FindPairedNameButtonLabel()
    {
        var current = Element;
        for (int depth = 0; depth < 5; depth++)
        {
            var parent = current.hierarchy.parent;
            if (parent == null) return null;
            int count = parent.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var sibling = parent.hierarchy[i];
                if (sibling.Pointer == current.Pointer) continue;
                if (!ElementBridge.IsVisible(sibling)) continue;
                var button = FindDemotedPairedButton(sibling, 0, 2);
                if (button == null) continue;

                var texts = new List<string>();
                CollectAllTexts(button, texts);
                if (texts.Count == 0) continue;
                return Speech.TextFilters.CleanForDisplay(string.Join(", ", texts));
            }
            current = parent;
        }
        return null;
    }

    // A visible SIButton (UXML name "SIButton") in this subtree that ElementBridge demoted to an
    // internal child because a dropdown sits in its sibling subtree. Re-checking the demotion
    // predicate keeps the scan from adopting a button that is a real, separate stop (the ribbon's
    // secondary tabs sit near the "More" dropdown but are never demoted).
    private static VisualElement FindDemotedPairedButton(VisualElement el, int depth, int maxDepth)
    {
        string type;
        try { type = el.GetIl2CppType().Name; }
        catch { type = null; }
        if (type == "SIButton" && el.name == "SIButton"
            && ElementBridge.HasSiblingSubtreeWithDropdown(el))
            return el;
        if (depth >= maxDepth) return null;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDemotedPairedButton(el.hierarchy[i], depth + 1, maxDepth);
            if (found != null) return found;
        }
        return null;
    }

    private string FindSplitButtonSiblingText()
    {
        var current = Element;
        for (int depth = 0; depth < 10; depth++)
        {
            var parent = current.hierarchy.parent;
            if (parent == null) return null;

            if (parent.name == "buttons-hybrid-split-button")
            {
                var contents = parent;
                if (contents.hierarchy.childCount == 1 && contents.hierarchy[0].name == "parent-anchor")
                    contents = contents.hierarchy[0];
                var branch = Element;
                while (branch.hierarchy.parent != null && branch.hierarchy.parent.Pointer != contents.Pointer)
                    branch = branch.hierarchy.parent;
                var texts = new System.Collections.Generic.List<string>();
                int count = contents.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    var sibling = contents.hierarchy[i];
                    if (sibling.Pointer == branch.Pointer) continue;
                    if (!ElementBridge.IsVisible(sibling)) continue;
                    CollectAllTexts(sibling, texts);
                }
                return texts.Count > 0 ? string.Join(", ", texts) : null;
            }

            current = parent;
        }
        return null;
    }

    public override string GetState() => ReadState(resolveNativeCaption: true);

    // Specialized adapters retain their original positional base state while
    // ordinary labelled icon-only fields can expose their real selected value.
    protected string ReadState(bool resolveNativeCaption)
    {
        if (Element.name == "SecondaryTabDropdownButton") return "";
        // When the dropdown is named by its row label, the trailing slot carries the live
        // selection so it reads "Currency, dropdown, £ - UK Pound Sterling". A value-named
        // dropdown (no row label) has no distinct value to add, so it keeps the choice index
        // and count, the orientation a user browsing a bare toolbar dropdown relies on.
        var selfLabel = FindSelectionFieldLabel();
        // Reuse ComboboxAdapter's labelled-value contract for a profile rename
        // as well. Otherwise an officially displayed selection becomes only
        // "1 of 1" after the profile supplies its accessible field name.
        // Specialized positional adapters explicitly opt out of this extension.
        if (selfLabel == null && (FindFieldLabel() != null
            || resolveNativeCaption && FM27Access.Navigation.NavigationManager.GetRenameLabel(this) != null))
        {
            var value = FindAllChildText();
            if (string.IsNullOrEmpty(value) && resolveNativeCaption)
                value = StaticButtonCaption.ReadDropdownValue(Element.TryCast<SI.Bindable.SIDropdown>());
            if (!string.IsNullOrEmpty(value)) return value;
        }

        // The positional read is only meaningful for a real SIDropdown. GetDropdown reinterprets
        // the element's pointer as one, and the Youth Setup cards' team switcher is an SIMenu —
        // a plain VisualElement subclass with none of those fields — so the index and count came
        // back as whatever the memory at those offsets held ("1 of -849773376", "1 of
        // 1591244480"). That is a fabricated orientation, so it is dropped rather than degraded
        // to a marker: the control still announces by name, role and value, which is the whole
        // truth for a menu with no choice list to count.
        var typed = Element.TryCast<SI.Bindable.SIDropdown>();
        string indexOfCount = null;
        if (typed != null)
        {
            var dropdown = GetDropdown();
            var selectedIndex = GetSelectedIndex();
            var count = (int)SIDropdownReflection.ChoicesCount.GetValue(dropdown);

            // A SelectedIndex of -1 is a menu with no current value (Set All, More, the squad
            // sub-tabs): there is nothing to orient against, so the positional "index of count"
            // would read as a meaningless "0 of N". Drop it and let the control announce by name
            // and role alone ("Set All, dropdown"). The orientation is kept only for a dropdown
            // that actually holds a selected value.
            if (selectedIndex >= 0 && selectedIndex < count)
                indexOfCount = $"{selectedIndex + 1} of {count}";
        }

        // A self-labelled dropdown whose SelectionField also carries a "secondary" value surfaces
        // that value before the index, so the database-size box reads "Total Player Count,
        // dropdown, 44690, 3 of 4" rather than dropping the count the SelectionField label split off.
        if (selfLabel != null)
        {
            var secondary = FindSelectionFieldSecondary();
            if (!string.IsNullOrEmpty(secondary))
                return indexOfCount != null ? $"{secondary}, {indexOfCount}" : secondary;
        }

        return indexOfCount ?? "";
    }

    public override void Activate()
    {
        // Activation is handled by AccessibilityBehaviour entering dropdown mode
    }

    public int GetSelectedIndex()
    {
        var native = Element.TryCast<SI.Bindable.SIDropdown>();
        var canonical = Server.NativeClauseSelection.CurrentIndex(native);
        if (canonical.HasValue) return canonical.Value;
        var dropdown = GetDropdown();
        return (int)SIDropdownReflection.SelectedIndex.GetValue(dropdown);
    }

    // Split-timed because this is where a substitution-time freeze has been measured landing, and the
    // two halves mean opposite things: the wrap builds an IL2CPP handle from the element's raw
    // pointer (slow or hung => the dropdown this session holds is no longer a live object), while the
    // read is a reflected property call into the game (slow => the game itself is busy behind it).
    // A budget cannot bound either — there is nothing to charge between, it is one call.
    private const long SlowReadMs = 250;

    public int GetChoicesCount()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var dropdown = GetDropdown();
        long wrap = watch.ElapsedMilliseconds;
        int count = (int)SIDropdownReflection.ChoicesCount.GetValue(dropdown);
        long read = watch.ElapsedMilliseconds - wrap;
        if (wrap + read >= SlowReadMs)
            Speech.Log.Warn($"[Dropdown] GetChoicesCount SLOW: wrap={wrap}ms read={read}ms");
        return count;
    }

    public virtual void ShowPopup()
    {
        Server.NativeClauseSelection.PrepareOpen(Element.TryCast<SI.Bindable.SIDropdown>());
        var dropdown = GetDropdown();
        _showDropdownMethod.Invoke(dropdown, null);
    }

    public void HidePopup()
    {
        var dropdown = GetDropdown();
        var floatingElement = SIDropdownReflection.FloatingElement.GetValue(dropdown);
        SIDropdownReflection.FloatingElementHide.Invoke(floatingElement, null);
    }

    // True when the game has greyed this dropdown out (a disabled SIEnabled wrapper propagates to
    // enabledInHierarchy). A disabled dropdown must never be opened: the game binds nothing, so
    // showing it NREs in BindChildRoot.
    public bool IsDisabled() => !Element.enabledInHierarchy;

    public bool IsPopupVisible()
    {
        var dropdown = GetDropdown();
        var floatingElement = SIDropdownReflection.FloatingElement.GetValue(dropdown);
        if (floatingElement == null) return false;
        return (bool)SIDropdownReflection.FloatingElementIsVisible.GetValue(floatingElement);
    }

    /// <summary>
    /// Commits the choice at <paramref name="index"/>. False means nothing was committed, and the
    /// caller must say so rather than announce the label it asked for.
    /// </summary>
    public bool ConfirmSelection(int index)
    {
        var dropdown = GetDropdown();
        var indexBefore = (int)SIDropdownReflection.SelectedIndex.GetValue(dropdown);
        Speech.Log.Info($"[Dropdown] ConfirmSelection: requesting index {index}, current SelectedIndex={indexBefore}");

        if (IsContractDecisionDropdown() || HasExplicitPanelCommand(dropdown, index))
            return ConfirmByRowCommand(dropdown, index);

        SIDropdownReflection.TriggerSelectionEvent.Invoke(dropdown, new object[] { index });
        Speech.Log.Info($"[Dropdown] ConfirmSelection: called TriggerSelectionEvent for index {index}");

        SIDropdownReflection.HideDropdown.Invoke(dropdown, null);
        return true;
    }

    // The names the game gives the contract-decision widget's own buttons: the closed face carries
    // one ("Delay Decision" beside its chevron), and each popup row carries one per action. No
    // other SIDropdown contains a button with either name.
    private static readonly string[] ContractDecisionButtonNames =
        { "GenericContractDecisionButton", "OfferContractButton" };

    /// <summary>
    /// True only for the contract-decision split-button widget — the contract-expiry messages'
    /// Recommended Action column — recognised by the named button the game builds into the closed
    /// face itself. On this widget the popup ROWS carry the commands and the game commits by
    /// clicking one, exactly as a mouse would; TriggerSelectionEvent moves nothing.
    ///
    /// The gate is this NAME check on the closed element, never a shape heuristic. An earlier
    /// attempt gated on "empty SelectionEvents + any SIButton in a realized popup row", and that
    /// over-matched: ordinary dropdowns that commit through their SelectionValueBinding write-back
    /// (the Contract Negotiation comboboxes among them) were hijacked into the row-command path,
    /// where there is no command to click, and every pick came back refused. A widget either is
    /// this named game control or it is not; nothing else can match.
    /// </summary>
    private bool IsContractDecisionDropdown()
    {
        foreach (var name in ContractDecisionButtonNames)
        {
            if (FindDescendantNamed(Element, name) != null) return true;
        }
        return false;
    }

    private bool HasExplicitPanelCommand(object dropdown, int index)
    {
        // Some FM27 choices are commands, not values: the custom database choice
        // has an OpenPanel event on its own row. Ordinary choices have no such
        // event and retain the original SelectionValueBinding write-back path.
        // Inspect native events, never a translated label or the mere presence
        // of a button (which also occurs in ordinary form dropdowns).
        var row = FindRowCommand(dropdown, index)?.TryCast<SI.Bindable.SIButton>();
        var events = row?.Events;
        if (events == null) return false;
        for (int i = 0; i < events.Count; i++)
            if (events[i]?.TryCast<SI.Bindable.Events.OpenPanelEventContext>() != null)
                return true;
        return false;
    }

    private static VisualElement FindDescendantNamed(VisualElement el, string name)
    {
        if (el.name == name) return el;
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var hit = FindDescendantNamed(el.hierarchy[i], name);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Commits by activating the popup row's own command button. The row must be realized to be
    /// clicked, so a virtualized list is scrolled to the choice first; if the row still cannot be
    /// found, this reports failure instead of leaving the caller to claim a commit that never
    /// happened.
    /// </summary>
    private bool ConfirmByRowCommand(object dropdown, int index)
    {
        var row = FindRowCommand(dropdown, index);
        if (row == null)
        {
            ScrollToChoice(index);
            row = FindRowCommand(dropdown, index);
        }
        if (row == null)
        {
            Speech.Log.Warn($"[Dropdown] ConfirmByRowCommand: no command button for choice {index}; nothing committed.");
            return false;
        }

        Speech.Log.Info($"[Dropdown] ConfirmByRowCommand: activating row command for choice {index}");
        new ButtonAdapter(row).Activate();
        // The game closes the popup itself on a row command (HideOnSelectionChange), and the widget
        // re-binds immediately afterwards — so no hide is issued here, where it would land on a
        // control that is mid-rebind.
        return true;
    }

    // The row for an absolute choice index, if the virtualized list currently has it realized.
    // Searches the ACTIVE (realized) items, not just the in-viewport visible ones: the row's
    // command button is invoked programmatically, so it does not need to be on screen — and the
    // popup's scroll clamp can keep tail rows permanently off screen (see ReadRealizedWindow).
    private VisualElement FindRowCommand(object dropdown, int index)
        => FindButton(FindRealizedRow(dropdown, index));

    private VisualElement FindRealizedRow(object dropdown, int index)
    {
        var streamedList = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        if (streamedList == null) return null;
        var virtualisedList = SIDropdownReflection.ListElement.GetValue(streamedList);
        if (virtualisedList == null) return null;
        var activeCount = (int)SIDropdownReflection.VirtualisedListGetActiveCount.Invoke(virtualisedList, null);

        for (int i = 0; i < activeCount; i++)
        {
            var item = SIDropdownReflection.VirtualisedListGetActiveItem.Invoke(virtualisedList, new object[] { i });
            if (item == null) continue;
            if ((int)SIDropdownReflection.VirtualisedElementIndex.GetValue(item) != index) continue;

            var contentObj = SIDropdownReflection.VirtualisedElementContent.GetValue(item);
            if (contentObj == null) return null;
            return new VisualElement(((Il2CppSystem.Object)contentObj).Pointer);
        }
        return null;
    }

    private static VisualElement FindButton(VisualElement el)
    {
        if (el == null) return null;
        if (el.GetIl2CppType().Name == "SIButton") return el;
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var hit = FindButton(el.hierarchy[i]);
            if (hit != null) return hit;
        }
        return null;
    }

    public void ConfirmLeafSelection(int index)
    {
        var dropdown = GetDropdown();
        Speech.Log.Info($"[Dropdown] ConfirmLeafSelection: calling TriggerSelectionEvent({index})");
        SIDropdownReflection.TriggerSelectionEvent.Invoke(dropdown, new object[] { index });
    }

    public void ScrollToChoice(int index)
    {
        var dropdown = GetDropdown();
        var streamedList = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        var virtualisedList = SIDropdownReflection.ListElement.GetValue(streamedList);
        SIDropdownReflection.VirtualisedListScrollTo.Invoke(virtualisedList, new object[] { index });
    }

    public string ReadChoiceAtIndex(int index)
    {
        var window = ReadRealizedWindow();
        if (window.TryGetValue(index, out var text))
            return text;

        Speech.Log.Warn($"[Dropdown] No realized item with index {index} found (window size={window.Count}).");
        return null;
    }

    /// <summary>
    /// Reads every row the virtualized list currently has REALIZED, keyed by its absolute
    /// choice index. The window moves as the list is scrolled, so a harvest of a large list
    /// scrolls and calls this per window, stitching the results contiguously
    /// (see <see cref="Navigation.DropdownHarvest"/>). Reading the whole window per scroll —
    /// rather than one index at a time — collapses a 100+ item harvest from one scroll-settle
    /// per row to one per window.
    ///
    /// The read walks the ACTIVE items, not the in-viewport visible subset: the popup's scroll
    /// range is clamped by a content-height estimate that lags row realization, so ScrollTo can
    /// leave the target row realized just below the viewport edge, permanently outside the
    /// visible set (the Captains card's 30-player pickers stall exactly there). Active items
    /// include those rows.
    /// </summary>
    public Dictionary<int, string> ReadRealizedWindow()
        => ReadRealizedWindow(out _);

    public Dictionary<int, string> ReadRealizedWindow(out Dictionary<int, bool> disabled)
    {
        var window = new Dictionary<int, string>();
        disabled = new Dictionary<int, bool>();
        var dropdown = GetDropdown();
        var streamedList = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        var virtualisedList = SIDropdownReflection.ListElement.GetValue(streamedList);

        var activeCount = (int)SIDropdownReflection.VirtualisedListGetActiveCount.Invoke(virtualisedList, null);

        for (int i = 0; i < activeCount; i++)
        {
            var visibleItem = SIDropdownReflection.VirtualisedListGetActiveItem.Invoke(virtualisedList, new object[] { i });
            if (visibleItem == null) continue;

            var itemIndex = (int)SIDropdownReflection.VirtualisedElementIndex.GetValue(visibleItem);
            var contentObj = SIDropdownReflection.VirtualisedElementContent.GetValue(visibleItem);
            if (contentObj == null) continue;

            var contentElement = new VisualElement(((Il2CppSystem.Object)contentObj).Pointer);
            // A row still binding is simply left out of the window: the harvest keys by absolute
            // index, so a gap costs a re-scroll, not a shifted list.
            if (TryReadRowLabel(contentElement, out var label) == RowRead.Ok)
            {
                window[itemIndex] = label;
                disabled[itemIndex] = AnyDisabledInSubtree(contentElement);
            }
        }

        return window;
    }

    /// <summary>
    /// Dev diagnostic: every ACTIVE (realized) row of the open popup's virtualised list — its
    /// absolute index, layout height/position, and label read — to expose rows that exist in
    /// the model but never gain height or content. Main thread only.
    /// </summary>
    public string DescribeActiveItems()
    {
        var sb = new System.Text.StringBuilder();
        var dropdown = GetDropdown();
        var streamedList = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        var virtualisedList = SIDropdownReflection.ListElement.GetValue(streamedList);
        int active = (int)SIDropdownReflection.VirtualisedListGetActiveCount.Invoke(virtualisedList, null);
        sb.Append("active=").Append(active)
          .Append(" choicesCount=").Append(SIDropdownReflection.ChoicesCount.GetValue(dropdown))
          .Append('\n');
        var children = (Il2CppSystem.Collections.Generic.List<SI.Bindable.SIDropdown>)_childrenDropdownsProperty.GetValue(dropdown);
        if (children != null)
            for (int i = 0; i < children.Count; i++)
                sb.Append("child[").Append(i).Append("] choice=").Append(children[i].m_childIndex)
                  .Append(" visible=").Append(new DropdownAdapter(children[i]).IsPopupVisible()).Append('\n');

        for (int i = 0; i < active; i++)
        {
            var item = SIDropdownReflection.VirtualisedListGetActiveItem.Invoke(virtualisedList, new object[] { i });
            if (item == null) { sb.Append('[').Append(i).Append("] null\n"); continue; }

            int idx = (int)SIDropdownReflection.VirtualisedElementIndex.GetValue(item);
            var contentObj = SIDropdownReflection.VirtualisedElementContent.GetValue(item);
            if (contentObj == null) { sb.Append('[').Append(i).Append("] #").Append(idx).Append(" content=null\n"); continue; }

            var el = new VisualElement(((Il2CppSystem.Object)contentObj).Pointer);
            var r = el.layout;
            var read = TryReadRowLabel(el, out var label);
            sb.Append('[').Append(i).Append("] #").Append(idx)
              .Append(" h=").Append(r.height.ToString("F1")).Append(" y=").Append(r.y.ToString("F1"))
              .Append(" visible=").Append(el.visible)
              .Append(' ').Append(read).Append(":\"").Append(label).Append("\"\n");
        }
        return sb.ToString();
    }

    public List<string> ReadChoices()
    {
        var choices = new List<string>();
        var dropdown = GetDropdown();

        var streamedListObj = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        if (streamedListObj == null)
        {
            Speech.Log.Warn("[Dropdown] m_streamedDropdownList is null.");
            return choices;
        }

        VisualElement listElement;
        try
        {
            listElement = new VisualElement(((Il2CppSystem.Object)streamedListObj).Pointer);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Dropdown] Failed to cast StreamedDropdownList to VisualElement: {e.GetType().Name}");
            return choices;
        }

        // The StreamedDropdownList contains ScrollView infrastructure.
        // Structure: StreamedDropdownList > VirtualisedList > SIScrollView >
        //   unity-content-and-vertical-scroll-container > unity-content-viewport >
        //   unity-content-container > DropTarget("View") > [per-choice AsyncBindingRoot...]
        var contentContainer = FindByName(listElement, "unity-content-container");
        if (contentContainer == null)
        {
            Speech.Log.Warn("[Dropdown] unity-content-container not found, falling back to flat text collection.");
            CollectAllTexts(listElement, choices);
            return choices;
        }

        // content-container typically has a single wrapper child (DropTarget "View")
        // whose children are the actual per-choice elements.
        var itemsParent = contentContainer;
        if (contentContainer.hierarchy.childCount == 1)
        {
            var onlyChild = contentContainer.hierarchy[0];
            if (onlyChild.hierarchy.childCount > 1)
                itemsParent = onlyChild;
        }

        int itemCount = itemsParent.hierarchy.childCount;
        int empty = 0, notReady = 0;
        for (int i = 0; i < itemCount; i++)
        {
            switch (TryReadRowLabel(itemsParent.hierarchy[i], out var label))
            {
                case RowRead.Ok: choices.Add(label); break;
                case RowRead.Empty: empty++; break;
                // A rating-only row that has not bound yet makes the WHOLE read unusable: keeping
                // the rows around it would shift every index the caller commits by. Report nothing
                // and let the session's retry loop come back a frame later.
                case RowRead.NotReady: notReady++; break;
            }
        }

        if (notReady > 0)
        {
            Speech.Log.Warn($"[Dropdown] {notReady} of {itemCount} rows have an unbound star rating; "
                          + "reporting the list as not ready.");
            choices.Clear();
        }
        else if (choices.Count == 0 && itemCount > 0)
        {
            Speech.Log.Warn($"[Dropdown] none of {itemCount} rendered rows carried a label "
                          + $"(empty={empty}).");
        }

        return choices;
    }

    /// <summary>How a popup row read: usable label, nothing there at all, or a rating still binding.</summary>
    private enum RowRead { Ok, Empty, NotReady }

    /// <summary>
    /// One popup row's label — the single rule every row reader shares, so the rows they count stay
    /// index-aligned with the choices list the player selects from.
    ///
    /// A row can carry a star widget beside its text (the tactics IP/OOP role pickers rate the player
    /// for every role) or INSTEAD of any text at all (the search filters' star-range pickers, whose
    /// every option is a rating). The stars are drawn from sprites, so the text collection misses
    /// them; the rating is read off the FMStarRatingTemp and read in words ("2 and a half stars").
    /// </summary>
    private static RowRead TryReadRowLabel(VisualElement row, out string label)
    {
        label = null;
        var texts = new List<string>();
        CollectAllTexts(row, texts);

        var star = StarRatingHelper.FindDescendantByType(row, "FMStarRatingTemp");
        if (star != null)
        {
            // m_minValue/m_maxValue sit at -1 until the binding resolves, so an unread rating is
            // never reported as "0 stars". A row whose rating is its ONLY content has nothing left
            // to show, and dropping it would shift every index after it — so the whole read defers
            // and the session retries. A row that also has text keeps the text: the rating is worth
            // waiting a frame for, but not at the price of an unusable role picker.
            if (StarRatingHelper.TryReadStarLabel(star, out var starLabel)) texts.Add(starLabel);
            else if (texts.Count == 0) return RowRead.NotReady;
            else Speech.Degrade.Note($"dropdown row \"{string.Join(", ", texts)}\": star rating unbound");
        }

        if (texts.Count == 0) return RowRead.Empty;
        label = string.Join(", ", texts);
        return RowRead.Ok;
    }

    /// <summary>
    /// Per root choice (aligned with <see cref="ReadChoices"/>), that choice's checkbox state — or
    /// null when the list is not a toggle list at all.
    ///
    /// A few dropdowns are multi-select filters rather than select-one combo boxes: the staff
    /// search's "Staff Interest (N)" exclude rules and the player search's "Quick Filters". The game
    /// draws them with a real <c>SICheckBox</c> in every row's leading frame and leaves
    /// <see cref="SI.Bindable.SIDropdown.SelectedIndex"/> at -1, because there is no single selected
    /// item; the field shows a fixed caption (with the active count) instead of a value. Reading the
    /// checkbox is what tells the player WHICH options are on — the caption only ever says how many.
    ///
    /// Null (no row has a checkbox) and all-false (a toggle list with nothing on) are deliberately
    /// distinct: the first means "state does not apply", the second "state applies and is off".
    /// Non-virtualized lists only, where every row is rendered and row order matches the choices.
    /// </summary>
    public bool[] BuildIsCheckedArray()
    {
        var result = new List<bool>();
        foreach (var row in GetRowRoots())
        {
            // Mirror ReadChoices' skip of label-less rows so indices line up with the choices.
            if (TryReadRowLabel(row, out _) == RowRead.Empty) continue;

            var box = FindDescendantByTypeName(row, "SICheckBox");
            // A toggle list draws a checkbox in EVERY row's leading frame (see summary), so the
            // first text-bearing row decides: no checkbox there means an ordinary select-one
            // dropdown, and the remaining rows are spared a full per-subtree type search each.
            if (box == null && result.Count == 0) return null;
            result.Add(box != null && box.Cast<Toggle>().value);
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    private static VisualElement FindDescendantByTypeName(VisualElement el, string typeName)
    {
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (c.GetIl2CppType().Name == typeName) return c;
            var found = FindDescendantByTypeName(c, typeName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// The selected choice index inferred from the rendered rows, for dropdowns whose current value
    /// <see cref="SI.Bindable.SIDropdown.SelectedIndex"/> does not track — it stays -1 for the tactics
    /// role pickers, which mark the active role with a tick sprite in its row instead. Mirrors
    /// <see cref="ReadChoices"/>' skip of label-less rows so the returned index aligns with the choices
    /// list. Returns -1 when no row is ticked. Non-virtualized lists only (every row must be rendered),
    /// which is exactly where SelectedIndex is unreliable.
    /// </summary>
    public int GetSelectedIndexFromRows()
    {
        int choiceIndex = -1;
        foreach (var row in GetRowRoots())
        {
            if (TryReadRowLabel(row, out _) == RowRead.Empty) continue;
            choiceIndex++;
            if (RowHasSelectionTick(row)) return choiceIndex;
        }
        return -1;
    }

    // The active row fills its "tick" slot (the RightIconIndicator's tick) with a visible check
    // sprite; unselected rows leave that slot empty, and the non-role action rows carry a
    // "tick-spacer" under a different name. So a "tick" element holding a visible child is unique to
    // the current row.
    private static bool RowHasSelectionTick(VisualElement row)
    {
        var tick = FindByName(row, "tick");
        if (tick == null) return false;
        int count = tick.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (ElementBridge.IsVisible(tick.hierarchy[i])) return true;
        return false;
    }

    // Status-icon slot names every player-card menu row carries; the row's type is the one
    // slot it populates. Body language and condition are read-only indicators, not commands.
    private static readonly string[] InfoIndicatorIcons = { "BodyLanguageIcon", "ConditionIcon" };

    /// <summary>
    /// Per root choice (aligned with <see cref="ReadChoices"/>), true when the row is a
    /// read-only status indicator (body language, condition) rather than an actionable
    /// command. Classifies by which named status-icon slot the row populates, so it is
    /// independent of the level-dependent row text. Player-card menu use only (non-virtualized).
    /// </summary>
    public bool[] BuildIsInfoIndicatorArray()
    {
        var result = new List<bool>();
        foreach (var row in GetRowRoots())
        {
            // Mirror ReadChoices' skip of text-less rows so indices line up with the choices.
            var texts = new List<string>();
            CollectAllTexts(row, texts);
            if (texts.Count == 0) continue;
            result.Add(RowPopulatesAnyIcon(row, InfoIndicatorIcons));
        }
        return result.ToArray();
    }

    /// <summary>
    /// Per root choice (aligned with <see cref="ReadChoices"/>), true when the choice's menu row is
    /// greyed/disabled by the game (e.g. an individual touchline shout on cooldown after a team
    /// shout). Opening a disabled choice's submenu must be refused: the game binds nothing, so its
    /// first-show BindChildRoot dereferences a null scope and crashes. Detected by an SIEnabled
    /// wrapper (or any element) in the row subtree reporting enabledSelf == false, which is exactly
    /// what the F9 tree dump prints as "DISABLED". Player-card menu use only (non-virtualized).
    /// </summary>
    public bool[] BuildIsDisabledArray()
    {
        var result = new List<bool>();
        foreach (var row in GetRowRoots())
        {
            // Mirror ReadChoices' skip of text-less rows so indices line up with the choices.
            if (TryReadRowLabel(row, out _) != RowRead.Ok) continue;
            result.Add(AnyDisabledInSubtree(row));
        }
        return result.ToArray();
    }

    private static bool AnyDisabledInSubtree(VisualElement el)
    {
        WalkBudget.Charge();
        if (!el.enabledSelf) return true;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (AnyDisabledInSubtree(el.hierarchy[i])) return true;
        return false;
    }

    private static bool RowPopulatesAnyIcon(VisualElement row, string[] iconNames)
    {
        foreach (var name in iconNames)
        {
            var icon = WalkBudget.FindByName(row, name);
            if (icon != null && icon.hierarchy.childCount > 0)
                return true;
        }
        return false;
    }

    // Budgeted mirror of PluginAdapterBase.CollectAllTexts. The base version is unbounded and stays
    // that way for callers reading a single known-small element; this path walks whatever the game
    // hands back, which after a substitution is not something the mod gets to assume the size of.
    private static void CollectAllTexts(VisualElement root, List<string> texts)
    {
        WalkBudget.Charge();
        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
            {
                texts.Add(textEl.text);
                return;
            }
        }
        catch (Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in CollectAllTexts: {e.GetType().Name}"); }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectAllTexts(root.hierarchy[i], texts);
    }

    // The per-choice row roots, in choice order (non-virtualized lists only). Shares the
    // list-walk shape with ReadChoices.
    private List<VisualElement> GetRowRoots()
    {
        var roots = new List<VisualElement>();
        var dropdown = GetDropdown();
        var streamedListObj = SIDropdownReflection.StreamedDropdownList.GetValue(dropdown);
        if (streamedListObj == null) return roots;

        var listElement = new VisualElement(((Il2CppSystem.Object)streamedListObj).Pointer);
        var contentContainer = FindByName(listElement, "unity-content-container");
        if (contentContainer == null) return roots;

        var itemsParent = contentContainer;
        if (contentContainer.hierarchy.childCount == 1)
        {
            var onlyChild = contentContainer.hierarchy[0];
            if (onlyChild.hierarchy.childCount > 1)
                itemsParent = onlyChild;
        }

        int itemCount = itemsParent.hierarchy.childCount;
        for (int i = 0; i < itemCount; i++)
            roots.Add(itemsParent.hierarchy[i]);
        return roots;
    }

    public bool DoesChoiceHaveSublist(int index)
    {
        var dropdown = GetDropdown();
        return (bool)_doesChoiceHaveSublistMethod.Invoke(dropdown, new object[] { index });
    }

    public bool IsHierarchical()
    {
        int count = GetChoicesCount();
        // One IL2CPP wrapper for the whole loop: DoesChoiceHaveSublist would construct a fresh
        // handle per choice, which on a long list is N wrapper allocations on the main thread.
        var dropdown = GetDropdown();
        var args = new object[1];
        for (int i = 0; i < count; i++)
        {
            WalkBudget.Charge();
            args[0] = i;
            if ((bool)_doesChoiceHaveSublistMethod.Invoke(dropdown, args))
                return true;
        }
        return false;
    }

    public bool[] BuildHasChildrenArray()
    {
        int count = GetChoicesCount();
        var result = new bool[count];
        // One IL2CPP wrapper for the whole loop (see IsHierarchical).
        var dropdown = GetDropdown();
        var args = new object[1];
        for (int i = 0; i < count; i++)
        {
            // Charged per choice: this loop is not a tree walk but a reflection call into the game
            // for every entry, which is the same unbounded main-thread cost wearing a different shape.
            WalkBudget.Charge();
            args[0] = i;
            result[i] = (bool)_doesChoiceHaveSublistMethod.Invoke(dropdown, args);
        }
        return result;
    }

    public DropdownAdapter GetVisibleChildAdapter(int index)
    {
        // A recycled controller's pool position is not its absolute choice
        // index. Other controllers can also remain visible after harvesting.
        // Adopt only the child belonging to the requested realized row.
        var child = FindChoiceChild(GetDropdown(), index);
        if (child == null) return null;
        var adapter = new DropdownAdapter(child);
        return adapter.IsPopupVisible() ? adapter : null;
    }

    private VisualElement FindChoiceChild(object dropdown, int index)
    {
        var row = FindRealizedRow(dropdown, index);
        return row == null ? null : StarRatingHelper.FindDescendantByType(row, "SIDropdown");
    }

    public void TriggerChildOpen(int index)
    {
        var dropdown = GetDropdown();
        var childElement = FindChoiceChild(dropdown, index);
        if (childElement == null)
            throw new System.InvalidOperationException($"The submenu row for option {index + 1} is not ready.");
        Speech.Log.Info($"[Dropdown] TriggerChildOpen({index}): calling OnSelectionChangedInternal on SIDropdown.");
        _onSelectionChangedInternalMethod.Invoke(dropdown, new object[] { index });
        var children = (Il2CppSystem.Collections.Generic.List<SI.Bindable.SIDropdown>)_childrenDropdownsProperty.GetValue(dropdown);
        if (children != null)
            for (int i = 0; i < children.Count; i++)
            {
                WalkBudget.Charge();
                if (children[i].Pointer != childElement.Pointer)
                    new DropdownAdapter(children[i]).HideEntireHierarchy();
            }
        // Child controllers belong to recycled rows, not absolute choice indices.
        // Harvesting a large list leaves a different window realized. Resolve the
        // child's actual row after scrolling, rather than indexing the controller pool.
        new DropdownAdapter(childElement).ShowDropdownDirect();
    }

    internal bool PrepareChoice(int index)
    {
        var row = FindRealizedRow(GetDropdown(), index);
        if (row != null && TryReadRowLabel(row, out _) == RowRead.Ok) return true;
        ScrollToChoice(index);
        return false;
    }

    // Read the actual absolute row again immediately before invoking a native
    // command. A harvested flag is presentation state, not permission to act.
    internal bool TryReadChoiceDisabled(int index, out bool disabled)
    {
        disabled = false;
        var row = FindRealizedRow(GetDropdown(), index);
        if (row == null || TryReadRowLabel(row, out _) != RowRead.Ok) return false;
        disabled = AnyDisabledInSubtree(row);
        return true;
    }

    public void ShowDropdownDirect()
    {
        var dropdown = GetDropdown();
        _showDropdownMethod.Invoke(dropdown, null);
    }

    public void HideEntireHierarchy()
    {
        var dropdown = GetDropdown();
        // Close children we opened first (deepest-first), then self. The game's HideDropdown does not
        // cascade to children opened by reflection (they were never registered via the normal
        // OnChildDropdownOpened path), so without this they linger visible-but-unbound and corrupt
        // the next open. CloseSelfAndAllChildren also unbinds each child root, giving a clean reopen.
        var children = (Il2CppSystem.Collections.Generic.List<SI.Bindable.SIDropdown>)_childrenDropdownsProperty.GetValue(dropdown);
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
                new DropdownAdapter(children[i]).HideEntireHierarchy();
        }
        SIDropdownReflection.CloseSelfAndAllChildren.Invoke(dropdown, null);
    }

    protected object GetDropdown()
    {
        return Element.TryCast<SI.Bindable.SIDropdown>()
            ?? throw new System.InvalidOperationException(
                $"The native {Element.GetIl2CppType().Name} control is not an SIDropdown. Refresh the screen.");
    }

    private static void EnsureReflection()
    {
        if (_initialized) return;
        _initialized = true;

        SIDropdownReflection.EnsureInitialized();

        var dropdownType = typeof(SI.Bindable.SIDropdown);

        _choicesListProperty = dropdownType.GetProperty("m_choices");
        if (_choicesListProperty == null)
        {
            Speech.Log.Error("SIDropdown.m_choices property not found.");
            throw new System.MissingMemberException("SIDropdown", "m_choices");
        }

        _doesChoiceHaveSublistMethod = dropdownType.GetMethod("DoesChoiceAtIndexHaveASublist",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new System.Type[] { typeof(int) }, null);
        if (_doesChoiceHaveSublistMethod == null)
        {
            Speech.Log.Error("SIDropdown.DoesChoiceAtIndexHaveASublist(int) method not found.");
            throw new System.MissingMethodException("SIDropdown", "DoesChoiceAtIndexHaveASublist");
        }

        _childrenDropdownsProperty = dropdownType.GetProperty("m_childrenDropdowns");
        if (_childrenDropdownsProperty == null)
        {
            Speech.Log.Error("SIDropdown.m_childrenDropdowns property not found.");
            throw new System.MissingMemberException("SIDropdown", "m_childrenDropdowns");
        }

        _showDropdownMethod = AccessTools.Method(dropdownType, "ShowDropdown");
        if (_showDropdownMethod == null)
        {
            Speech.Log.Error("SIDropdown.ShowDropdown() method not found.");
            throw new System.MissingMethodException("SIDropdown", "ShowDropdown");
        }

        _onSelectionChangedInternalMethod = dropdownType.GetMethod("OnSelectionChangedInternal",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new System.Type[] { typeof(int) }, null);
        if (_onSelectionChangedInternalMethod == null)
        {
            Speech.Log.Error("SIDropdown.OnSelectionChangedInternal(int) method not found.");
            throw new System.MissingMethodException("SIDropdown", "OnSelectionChangedInternal");
        }
    }
}


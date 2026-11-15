using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Structural reader for the Player Search condition list ("conditions-list" / "attributes-list"
/// StreamedObjectList of "Frame-condition" rows). Each row is a fixed template of named slot
/// cells: the enable checkbox, reorder buttons, the "If" cell or the And/Or switch, the condition
/// name, the comparator dropdown, a family of value slots (exactly the visible ones apply — rows
/// recycle, so a hidden slot can hold a previous occupant's stale content and must be skipped by
/// visibility, never by content), and the delete button. The list virtualises, so rows are read
/// into plain strings via the StreamedTableReader protocol (scroll each row in, wait for its
/// binders) and every interaction re-realises its control by data index and slot key through
/// <see cref="RealizeControl"/>. Mirrors LeagueTableContext / FixtureScheduleContext.
/// </summary>
internal sealed class ConditionTableContext
{
    private static PropertyInfo _indexProperty;
    private static PropertyInfo _contentProperty;
    private static MethodInfo _scrollToMethod;
    private static PropertyInfo _siTextBinderProperty;
    private static PropertyInfo _binderIsReadyProperty;
    private static bool _reflectionInitialized;

    private readonly VisualElement _list; // the StreamedObjectList of condition rows

    public ConditionTableContext(VisualElement list)
    {
        EnsureReflection();
        _list = list;
    }

    private static void EnsureReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        var virtElemType = typeof(SI.UI.VirtualisedList.VirtualisedElement);
        _indexProperty = virtElemType.GetProperty("Index", BindingFlags.Public | BindingFlags.Instance);
        _contentProperty = virtElemType.GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
        if (_indexProperty == null || _contentProperty == null)
            throw new System.MissingMemberException("VirtualisedElement", "Index/Content");

        _scrollToMethod = typeof(SI.UI.VirtualisedList).GetMethod("ScrollTo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new System.Type[] { typeof(int) }, null);
        if (_scrollToMethod == null)
            throw new System.MissingMethodException("VirtualisedList", "ScrollTo");

        _siTextBinderProperty = typeof(SI.Bindable.SIText).GetProperty("m_binder",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_siTextBinderProperty == null)
            throw new System.MissingMemberException("SIText", "m_binder");
        _binderIsReadyProperty = typeof(SI.Bindable.Binder).GetProperty("IsReady",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_binderIsReadyProperty == null)
            throw new System.MissingMemberException("Binder", "IsReady");
    }

    // --- Row model (plain data; virtualised rows recycle, so no element references) ---

    internal sealed class Row
    {
        public int DataIndex;
        public string NativeIdentity;
        public string CommandContext;
        public string FocusContext;
        public bool? Enabled;          // enable checkbox; null when unreadable (rendered as a marker)
        public string Connector;       // "If" (plain text) or the And/Or switch's current word
        public bool ConnectorIsToggle; // true when the connector is the interactive And/Or switch
        public string Name;            // "Passing", "Transfer Value", ...
        public string Comparator;      // "Is At Least", ... (null when the row has no comparator)
        public bool ComparatorEnabled; // the comparator dropdown is openable in-game
        public List<ValuePart> Values = new();
        public bool CanMoveUp;
        public bool CanMoveDown;
        public bool HasReorder;        // the reorder cell is present at all (conditions yes, attributes view no)
        public bool HasDelete;
    }

    // One element of the Value cell. A row usually has one; "Is Between" rows have min + "and" + max.
    internal sealed class ValuePart
    {
        public string SlotKey;  // the slot's element name — doubles as the handle's column key
        public string Kind;     // "dropdown" | "combobox" | "text"
        public string Value;    // current display value (a degrade marker when unreadable)
        public bool Enabled;    // the control is operable in-game (text parts never are)
        public bool CanIncrement, CanDecrement;
    }

    // The value slot families in template order. Which are populated AND visible varies per
    // condition type; a hidden slot is recycled leftover and never read.
    private static readonly string[] ValueSlots =
    {
        "dropdown-value-visible",
        "cash-value-visible",
        "text-field-visible",
        "date-value-visible",
        "dropdown-value-min-visible",
        "cash-value-min-visible",
        "attribute-combobox-value-min-visible",
        "star-range-min-value-visible",
        "and-text-visible",
        "dropdown-value-max-visible",
        "cash-value-max-visible",
        "attribute-combobox-value-max-visible",
        "star-range-max-value-visible",
    };

    // --- StreamedTableReader ops (each runs in one MainThreadDispatcher.Invoke) ---

    public int ScrollToRowPending(int dataIndex)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return -1;
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var row = FindRowByDataIndex(vl, dataIndex);
        return row == null ? -1 : CountPendingCells(row);
    }

    // Pending count, plus the row model only when that count is 0 (the reader discards a not-ready
    // poll's row, so building one is pure waste).
    public (int pending, Row row) CountAndReadRow(int dataIndex)
    {
        var vl = GetVirtualisedList();
        var row = vl != null ? FindRowByDataIndex(vl, dataIndex) : null;
        if (row == null) return (0, null);
        int pending = CountPendingCells(row);
        if (pending != 0) return (pending, null);
        var model = BuildRow(row, dataIndex);
        return string.IsNullOrEmpty(model.NativeIdentity) ? (1, null) : (0, model);
    }

    /// <summary>
    /// Reads every currently-realized row with data index >= <paramref name="startIdx"/> whose cells
    /// are ALL bound (pending count 0), in one call and WITHOUT scrolling — the batch step of the
    /// streamed read (see TableContext.SweepReadyRows, whose contract this mirrors). The realized
    /// window cannot recycle mid-call (no frame advances inside a single main-thread dispatch);
    /// rows still binding — including empty shells, which <see cref="CountPendingCells"/> reports
    /// as pending — and rows not realized are left for the per-row scroll-and-settle path.
    /// </summary>
    public List<KeyValuePair<int, Row>> SweepReadyRows(int startIdx, System.Func<int, bool> skip = null)
    {
        var result = new List<KeyValuePair<int, Row>>();
        var virtList = GetVirtualisedList();
        if (virtList == null) return result;
        try
        {
            var vl = (SI.UI.VirtualisedList)virtList;
            int activeCount = vl.GetActiveCount();
            var seen = new HashSet<int>();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                int idx = (int)_indexProperty.GetValue(virtElem);
                if (idx < startIdx) continue;
                // Rows the caller already holds (its harvest would discard the fresh model anyway)
                // are skipped before the readiness walk and model build.
                if (skip != null && skip(idx)) continue;
                if (!seen.Add(idx))
                {
                    Log.Warn($"[ConditionTable] SweepReadyRows: duplicate data index {idx}, keeping first.");
                    continue;
                }
                var row = UnwrapRow(_contentProperty.GetValue(virtElem));
                if (row == null) continue;
                if (CountPendingCells(row) != 0) continue;
                var model = BuildRow(row, idx);
                if (!string.IsNullOrEmpty(model.NativeIdentity))
                    result.Add(new KeyValuePair<int, Row>(idx, model));
            }
        }
        catch (System.Exception e)
        {
            Log.Warn($"[ConditionTable] SweepReadyRows failed after {result.Count} row(s): {e.GetType().Name}: {e.Message}");
        }
        return result;
    }

    public Row ReadRow(int dataIndex)
    {
        var vl = GetVirtualisedList();
        var row = vl != null ? FindRowByDataIndex(vl, dataIndex) : null;
        return row == null ? null : BuildRow(row, dataIndex);
    }

    /// <summary>
    /// Re-realises (scrolls in) the row and returns the live control for a column key: "enabled",
    /// "andor", "compare", "up", "down", "delete", or a value slot name from <see cref="ValueSlots"/>.
    /// Null while a recycled row's controls have not re-bound yet — the handle retries across frames.
    /// </summary>
    public VisualElement RealizeControl(int dataIndex, string columnKey, string expectedIdentity = null, string expectedContext = null)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return null;
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var row = FindRowByDataIndex(vl, dataIndex);
        if (row == null) return null;

        // A freshly re-realised (recycled) row exists a frame or two before its binders finish:
        // its controls look right but clicks no-op and values read empty. Refuse to hand out a
        // control until the row's texts are bound — the same readiness signal the loader waits on
        // — so the handle's retry loop, not the caller, absorbs the re-bind wait.
        if (CountPendingCells(row) > 0) return null;
        if (expectedIdentity != null && ReadRuleIdentity(row) != expectedIdentity) return null;
        if (expectedContext != null && BuildRow(row, dataIndex).CommandContext != expectedContext) return null;

        switch (columnKey)
        {
            case "enabled": return FindDescendantNamed(row, "enabled-checkbox");
            case "andor":
                var toggleSlot = FindDescendantNamed(row, "and-or-toggle-visible");
                return toggleSlot != null ? FindDescendantNamed(toggleSlot, "inputs-switch-toggle-default") : null;
            case "compare":
                var compareSlot = FindDescendantNamed(row, "compare-flag-visible");
                return compareSlot != null ? FindDescendantOfType(compareSlot, "SIDropdown") : null;
            case "up": return FindDescendantNamed(row, "change-order-up-button");
            case "down": return FindDescendantNamed(row, "change-order-down-button");
            case "delete":
                var del = FindDescendantNamed(row, "divider-delete-container");
                return del != null ? FindDescendantNamed(del, "buttons-controls-pc-default") : null;
            default:
                var slot = FindDescendantNamed(row, columnKey);
                if (slot == null) return null;
                // The slot's control: a combobox slot holds an FMCombobox, a dropdown/star slot an
                // SIDropdown, a text/date slot its edit box.
                return FindDescendantOfType(slot, "DateSelectorWidget")
                    ?? FindDescendantOfType(slot, "FMCombobox")
                    ?? FindDescendantOfType(slot, "SIDropdown")
                    ?? FindDescendantOfType(slot, "BindableTextEditBox");
        }
    }

    // --- Row reading ---

    // The native rule reference includes its owning UI handler and rule type.
    // Its Index can also change on reorder: retain the full actual reference
    // as command context, never identify a condition by its caption/ordinal.
    private string ReadRuleIdentity(VisualElement rowEl)
    {
        for (var up = rowEl; up != null && up.Pointer != _list.Pointer; up = up.parent)
        {
            var node = Server.BindingRecords.NodeOf(up);
            if (node == null) continue;
            foreach (var child in node.Children)
                if (child.Name == "rule")
                    return Server.BindingRecords.ReferenceIdentity(
                        Server.BindingRecords.ValueOf(child)?.Get()?.TryCast<SI.Interop.InteropReference>());
        }
        return null;
    }

    internal bool MatchesRowIdentity(int dataIndex, string expected)
    {
        var vl = GetVirtualisedList();
        var row = vl == null ? null : FindRowByDataIndex(vl, dataIndex);
        return row != null && !string.IsNullOrEmpty(expected) && ReadRuleIdentity(row) == expected;
    }

    internal bool MatchesRowContext(int dataIndex, string identity, string context)
    {
        var row = ReadRow(dataIndex);
        return row != null && row.NativeIdentity == identity && row.CommandContext == context;
    }

    internal static string AdjustmentContext(Row row, string slot)
    {
        if (row == null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var value in new[] { row.NativeIdentity, row.Name, row.Comparator, row.Enabled?.ToString(), row.Connector })
            ContextPart(sb, value);
        foreach (var part in row.Values)
        {
            ContextPart(sb, part.SlotKey);
            ContextPart(sb, part.Kind);
            if (part.SlotKey != slot) ContextPart(sb, part.Value);
        }
        return sb.ToString();
    }

    // The game also recycles SAME-TYPE rule references between different
    // predicates. Preserve the real reference and separately snapshot its
    // native value/comparator context, like the inherited conversation guard.
    private static void ContextPart(System.Text.StringBuilder sb, string value)
        => sb.Append(value?.Length ?? -1).Append(':').Append(value);

    private static string ValueContext(SI.Core.TypedValue value, int depth = 0)
    {
        if (value == null) return "null";
        if (depth > 8) throw new System.InvalidOperationException("Search-rule value context is too deeply nested.");
        var reference = value.Get()?.TryCast<SI.Interop.InteropReference>();
        if (reference != null) return Server.BindingRecords.ReferenceIdentity(reference);
        var record = Server.BindingRecords.RecordOf(value);
        if (record != null)
        {
            var parts = new List<string>();
            var en = record.GetEnumerator();
            while (en.MoveNext())
            {
                var kv = en.Current;
                parts.Add(kv.Key + "=" + ValueContext(kv.Value, depth + 1));
                if (parts.Count > 4096) throw new System.InvalidOperationException("Search-rule record context is too large.");
            }
            parts.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder("record:");
            foreach (var part in parts) ContextPart(sb, part);
            return sb.ToString();
        }
        var list = Server.BindingRecords.ListOf(value);
        if (list != null)
        {
            if (list.Count > 4096) throw new System.InvalidOperationException("Search-rule list context is too large.");
            var sb = new System.Text.StringBuilder("list:");
            for (int i = 0; i < list.Count; i++) ContextPart(sb, ValueContext(list[i], depth + 1));
            return sb.ToString();
        }
        return value.Get()?.GetIl2CppType().FullName + ":" + value.AsString();
    }

    private string ReadValueContext(VisualElement rowEl)
    {
        for (var up = rowEl; up != null && up.Pointer != _list.Pointer; up = up.parent)
        {
            var node = Server.BindingRecords.NodeOf(up);
            if (node == null) continue;
            foreach (var rule in node.Children)
                if (rule.Name == "rule")
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var field in rule.Children)
                        if (field.Name == "FilterValue" || field.Name == "SelectedCompareFlag")
                        {
                            ContextPart(sb, field.Name);
                            ContextPart(sb, ValueContext(Server.BindingRecords.ValueOf(field)));
                        }
                    return sb.ToString();
                }
        }
        return null;
    }

    private Row BuildRow(VisualElement rowEl, int dataIndex)
    {
        var row = new Row { DataIndex = dataIndex, NativeIdentity = ReadRuleIdentity(rowEl) };

        var enabledEl = FindDescendantNamed(rowEl, "enabled-checkbox");
        if (enabledEl != null)
        {
            try { row.Enabled = enabledEl.Cast<Toggle>().value; }
            catch (System.Exception e) { Degrade.Note($"condition row {dataIndex}: enabled checkbox unreadable ({e.GetType().Name})"); }
        }

        var ifSlot = FindDescendantNamed(rowEl, "if-cell-visible");
        var andOrSlot = FindDescendantNamed(rowEl, "and-or-toggle-visible");
        if (IsActiveSlot(ifSlot))
        {
            row.Connector = FirstVisibleText(ifSlot);
        }
        else if (IsActiveSlot(andOrSlot))
        {
            var label = FindDescendantNamed(andOrSlot, "inputs-switch-toggle-label");
            row.Connector = label != null
                ? FirstVisibleText(label)
                : Degrade.Value($"condition row {dataIndex}: connector word unreadable");
            row.ConnectorIsToggle = true;
        }

        var nameSlot = FindDescendantNamed(rowEl, "condition-name-visible");
        if (IsActiveSlot(nameSlot))
            row.Name = FirstVisibleText(nameSlot)
                ?? Degrade.Value($"condition row {dataIndex}: name unreadable");

        var compareSlot = FindDescendantNamed(rowEl, "compare-flag-visible");
        if (IsActiveSlot(compareSlot))
        {
            var dd = FindDescendantOfType(compareSlot, "SIDropdown");
            row.Comparator = dd != null
                ? ReadDropdownValue(dd, dataIndex)
                : Degrade.Value($"condition row {dataIndex}: comparator missing");
            row.ComparatorEnabled = IsControlEnabled(dd);
        }

        foreach (var slotName in ValueSlots)
        {
            var slot = FindDescendantNamed(rowEl, slotName);
            if (!IsActiveSlot(slot)) continue;
            var part = ReadValuePart(slot, slotName, dataIndex);
            if (part != null) row.Values.Add(part);
        }

        var reorder = FindDescendantNamed(rowEl, "change-order-visible");
        row.HasReorder = IsActiveSlot(reorder);
        if (row.HasReorder)
        {
            row.CanMoveUp = IsControlEnabled(FindDescendantNamed(reorder, "change-order-up-button"));
            row.CanMoveDown = IsControlEnabled(FindDescendantNamed(reorder, "change-order-down-button"));
        }

        row.HasDelete = FindDescendantNamed(rowEl, "divider-delete-container") is VisualElement del
            && FindDescendantNamed(del, "buttons-controls-pc-default") != null;

        var context = new System.Text.StringBuilder();
        ContextPart(context, ReadValueContext(rowEl));
        ContextPart(context, row.Name);
        ContextPart(context, row.Comparator);
        ContextPart(context, row.Enabled?.ToString());
        foreach (var part in row.Values)
        {
            ContextPart(context, part.SlotKey);
            ContextPart(context, part.Kind);
            ContextPart(context, part.Value);
        }
        row.FocusContext = context.ToString();
        ContextPart(context, row.Connector);
        row.CommandContext = context.ToString();

        return row;
    }

    // A slot applies to this row only when it is effectively visible AND holds content. Recycled
    // rows keep a previous occupant's controls under a hidden SIVisible, so content alone lies;
    // an empty visible slot is simply not part of this condition type.
    private static bool IsActiveSlot(VisualElement slot)
        => slot != null && slot.hierarchy.childCount > 0 && ElementBridge.IsVisible(slot);

    private static ValuePart ReadValuePart(VisualElement slot, string slotName, int dataIndex)
    {
        var dateWidget = FindDescendantOfType(slot, "DateSelectorWidget");
        if (dateWidget != null)
        {
            var date = new Adapters.DateSelectorAdapter(dateWidget);
            string value = date.GetLabel();
            if (!date.IsUnset && date.TryGetSelectedDateTime(out int day, out int month, out int year))
                value = FM27Access.Navigation.DateFieldText.ToDmy(year, month, day);
            return new ValuePart { SlotKey = slotName, Kind = "date", Value = value, Enabled = IsControlEnabled(dateWidget) };
        }

        var combobox = FindDescendantOfType(slot, "FMCombobox");
        if (combobox != null)
        {
            string value;
            try { value = new Adapters.ComboboxAdapter(combobox).GetCurrentValue(); }
            catch (System.Exception e)
            {
                value = Degrade.Value($"condition row {dataIndex}: {slotName} combobox unreadable ({e.GetType().Name})");
            }
            var adapter = new Adapters.ComboboxAdapter(combobox);
            return new ValuePart { SlotKey = slotName, Kind = "combobox", Value = value ?? "", Enabled = IsControlEnabled(combobox),
                CanIncrement = adapter.IsIncrementEnabled(), CanDecrement = adapter.IsDecrementEnabled() };
        }

        var dropdown = FindDescendantOfType(slot, "SIDropdown");
        if (dropdown != null)
            return new ValuePart
            {
                SlotKey = slotName,
                Kind = "dropdown",
                Value = ReadDropdownValue(dropdown, dataIndex),
                Enabled = IsControlEnabled(dropdown),
            };

        // An editable value box ("Days since last scouted" and other free-number/text conditions).
        var editBox = FindDescendantOfType(slot, "BindableTextEditBox");
        if (editBox != null)
        {
            string value;
            try { value = new Adapters.TextFieldAdapter(editBox).GetCurrentText() ?? ""; }
            catch (System.Exception e)
            {
                value = Degrade.Value($"condition row {dataIndex}: {slotName} edit box unreadable ({e.GetType().Name})");
            }
            return new ValuePart { SlotKey = slotName, Kind = "textfield", Value = value, Enabled = IsControlEnabled(editBox) };
        }

        // Text-bearing slots: the "and" between a min and max, and the text/date families this
        // sweep has not seen populated (their visible text is still an honest read). A visible,
        // populated slot with nothing readable degrades to a marker rather than vanishing.
        var text = FirstVisibleText(slot);
        return new ValuePart
        {
            SlotKey = slotName,
            Kind = "text",
            Value = text ?? Degrade.Value($"condition row {dataIndex}: {slotName} has no readable value"),
        };
    }

    // A value dropdown's current display: its SelectionField's text, or the star widget's rating
    // for the star-range pickers (whose face holds an FMStarRatingTemp, no text).
    private static string ReadDropdownValue(VisualElement dropdown, int dataIndex)
    {
        var field = FindDescendantNamed(dropdown, "SelectionField");
        if (field == null) return Degrade.Value($"condition row {dataIndex}: dropdown has no SelectionField");

        var star = FindDescendantOfType(field, "FMStarRatingTemp");
        if (star != null && ElementBridge.IsVisible(star))
        {
            try
            {
                if (Adapters.StarRatingHelper.TryReadStarLabel(star, out var starLabel))
                    return starLabel;
            }
            catch (System.Exception e) { Degrade.Note($"condition row {dataIndex}: star value unreadable ({e.GetType().Name})"); }
            return Degrade.Unavailable;
        }

        var text = FirstVisibleText(field);
        return text ?? Degrade.Value($"condition row {dataIndex}: dropdown value unreadable");
    }

    // Enabled state via the hierarchy (the game gates these buttons with SIEnabled ancestors). An
    // unreadable state defaults to enabled — a click on a game-disabled button is a no-op, while
    // presenting an enabled button as disabled would wall off a real action.
    // The same enabled signal ServerText.IsDisabled and DropdownSession.Open read, so a control
    // rendered operable here is one the game will actually open. An unreadable state presents as
    // enabled: inventing unavailability the game never claimed is its own kind of lie, and the
    // server's own refusal still speaks if the open turns out to be blocked.
    private static bool IsControlEnabled(VisualElement el)
    {
        if (el == null) return false;
        try { return el.enabledInHierarchy; }
        catch (System.Exception e)
        {
            Degrade.Note($"condition row control enabled-state unreadable ({e.GetType().Name}); presenting as enabled");
            return true;
        }
    }

    // --- Virtualised list access (same shape as LeagueTableContext) ---

    private object GetVirtualisedList()
    {
        var vlVE = FindDescendantOfType(_list, "VirtualisedList");
        if (vlVE == null) return null;
        return new SI.UI.VirtualisedList(((Il2CppSystem.Object)vlVE).Pointer);
    }

    private VisualElement FindRowByDataIndex(object virtList, int dataIndex)
    {
        var vl = (SI.UI.VirtualisedList)virtList;
        int activeCount = vl.GetActiveCount();
        for (int i = 0; i < activeCount; i++)
        {
            var virtElem = vl.GetActiveItem(i);
            if (virtElem == null) continue;
            if ((int)_indexProperty.GetValue(virtElem) != dataIndex) continue;
            return UnwrapRow(_contentProperty.GetValue(virtElem));
        }
        return null;
    }

    // The active item's Content unwrapped to the row frame (the cells' parent). Null while the
    // item shell has no content yet (means "wait").
    private static VisualElement UnwrapRow(object content)
    {
        if (content == null) return null;
        var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
        return FindDescendantNamed(contentVE, "Frame-condition") ?? contentVE;
    }

    private int CountPendingCells(VisualElement row)
    {
        int bound = 0, notReady = 0;
        CountReadiness(row, ref bound, ref notReady);
        return bound == 0 ? 1 : notReady;
    }

    private void CountReadiness(VisualElement el, ref int bound, ref int notReady)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var binder = _siTextBinderProperty.GetValue(el.Cast<SI.Bindable.SIText>());
            if (binder != null)
            {
                bound++;
                if (!(bool)_binderIsReadyProperty.GetValue(binder)) notReady++;
            }
        }
        int childCount = el.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
            CountReadiness(el.hierarchy[i], ref bound, ref notReady);
    }

    // --- Tree helpers ---

    private static string FirstVisibleText(VisualElement root)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = ReadText(root);
            if (!string.IsNullOrEmpty(t)) return t;
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var t = FirstVisibleText(root.hierarchy[i]);
            if (t != null) return t;
        }
        return null;
    }

    private static string ReadText(VisualElement el)
    {
        try
        {
            var t = el.Cast<SI.Bindable.SIText>().text;
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindDescendantNamed(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static VisualElement FindDescendantOfType(VisualElement root, string typeName)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName) return child;
            var deeper = FindDescendantOfType(child, typeName);
            if (deeper != null) return deeper;
        }
        return null;
    }
}

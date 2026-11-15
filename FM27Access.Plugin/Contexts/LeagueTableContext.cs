using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Structural reader for the Add/Remove Leagues grid: a streamed, virtualised list
/// (StreamedObjectList of "frame league-row" rows) whose realized window is only the ~6 on-screen
/// rows, so the full nation list is read by scrolling each row in and waiting for its cells to bind
/// (the StreamedTableReader protocol). Rows are plain strings + flags — never held element
/// references, since virtualised rows recycle — and every interaction re-realises its control by
/// data index through <see cref="RealizeControl"/>. Mirrors FixtureScheduleContext rather than
/// reusing TableContext (this is a bespoke StreamedObjectList, not the game's StreamedTable widget).
/// </summary>
internal sealed class LeagueTableContext
{
    private static PropertyInfo _indexProperty;
    private static PropertyInfo _contentProperty;
    private static MethodInfo _scrollToMethod;
    private static PropertyInfo _siTextBinderProperty;
    private static PropertyInfo _binderIsReadyProperty;
    private static bool _reflectionInitialized;

    // The playable-setting picker shows its value as an icon, not text; the icon is applied by an
    // SIStyleSetter whose USS class is the value signal (mirroring the fixtures result-badge read).
    // Only the confirmed class is mapped — an unrecognised one is logged, never guessed into a word,
    // so the cell never shows a setting the game isn't showing.
    private static readonly System.Collections.Generic.HashSet<string> _loggedSettingClasses = new();

    private readonly VisualElement _list; // the StreamedObjectList of league rows

    public LeagueTableContext(VisualElement list)
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

    // --- Row model ---

    internal sealed class Row
    {
        public int DataIndex;
        public string Nation;
        public string Mode;
        public string MenRule;        // the men's rule dropdown's current value ("Austrian First Division and above")
        public bool MenRuleEnabled;   // that dropdown is openable in-game
        public string MenCount;       // active/total divisions ("2/2")
        public bool MenSetting;       // a men's playable-setting dropdown is present
        public bool MenSettingEnabled;
        public string MenSettingValue;// the men's setting's current value word ("Playable"), null when unmapped
        public string WomenRule;      // the women's rule dropdown's value, null when the fallback stands in
        public bool WomenRuleEnabled;
        public string WomenNone;      // the non-interactive "None" fallback text, null when a rule dropdown is present
        public string WomenCount;     // women's active/total ("1/1", "0/0")
        public bool WomenSetting;     // a women's playable-setting dropdown is present
        public bool WomenSettingEnabled;
        public string WomenSettingValue;
        public bool HasDelete;     // a remove button is present
    }

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
        return (pending, pending == 0 ? BuildRow(row, dataIndex) : null);
    }

    public Row ReadRow(int dataIndex)
    {
        var vl = GetVirtualisedList();
        var row = vl != null ? FindRowByDataIndex(vl, dataIndex) : null;
        return row == null ? null : BuildRow(row, dataIndex);
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
                    Log.Warn($"[LeagueTable] SweepReadyRows: duplicate data index {idx}, keeping first.");
                    continue;
                }
                var row = UnwrapRow(_contentProperty.GetValue(virtElem));
                if (row == null) continue;
                if (CountPendingCells(row) != 0) continue;
                result.Add(new KeyValuePair<int, Row>(idx, BuildRow(row, idx)));
            }
        }
        catch (System.Exception e)
        {
            Log.Warn($"[LeagueTable] SweepReadyRows failed after {result.Count} row(s): {e.GetType().Name}: {e.Message}");
        }
        return result;
    }

    /// <summary>
    /// Re-realises (scrolls in) the row and returns the live control element for a column key
    /// ("men-rule", "men-setting", "women-rule", "women-setting", "remove"). Null while a recycled
    /// row's controls have not re-bound yet — the handle retries across frames.
    /// </summary>
    public VisualElement RealizeControl(int dataIndex, string columnKey)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return null;
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var row = FindRowByDataIndex(vl, dataIndex);
        if (row == null) return null;

        switch (columnKey)
        {
            case "men-rule": return FindRuleDropdown(FindDescendantNamed(row, "league-men"));
            case "men-setting": return FindSettingDropdown(FindDescendantNamed(row, "league-men"));
            case "women-rule": return FindRuleDropdown(FindDescendantNamed(row, "league-women"));
            case "women-setting": return FindSettingDropdown(FindDescendantNamed(row, "league-women"));
            case "remove":
                var rc = FindDescendantNamed(row, "league-remove");
                return rc != null ? FindDescendantNamed(rc, "buttons-controls-pc-default") : null;
            default: return null;
        }
    }

    // --- Row reading ---

    private Row BuildRow(VisualElement rowEl, int dataIndex)
    {
        var row = new Row { DataIndex = dataIndex };

        var nationCell = FindDescendantNamed(rowEl, "league-nation");
        if (nationCell != null)
        {
            var texts = new List<string>();
            CollectVisibleTexts(nationCell, texts, 2);
            if (texts.Count > 0) row.Nation = texts[0];
            if (texts.Count > 1) row.Mode = texts[1];
        }

        var menCell = FindDescendantNamed(rowEl, "league-men");
        if (menCell != null)
        {
            var menRule = FindRuleDropdown(menCell);
            row.MenRule = ReadDropdownValue(menRule);
            row.MenRuleEnabled = IsControlEnabled(menRule);
            row.MenCount = ReadCount(menCell);
            var menSetting = FindSettingDropdown(menCell);
            row.MenSetting = menSetting != null;
            row.MenSettingEnabled = IsControlEnabled(menSetting);
            row.MenSettingValue = ReadSettingValue(menSetting);
        }

        var womenCell = FindDescendantNamed(rowEl, "league-women");
        if (womenCell != null)
        {
            var ruleGroup = FindDescendantNamed(womenCell, "Rulegroup");
            if (ruleGroup != null && ElementBridge.IsVisible(ruleGroup))
            {
                var womenRule = FindRuleDropdown(womenCell);
                row.WomenRule = ReadDropdownValue(womenRule);
                row.WomenRuleEnabled = IsControlEnabled(womenRule);
                var womenSetting = FindSettingDropdown(womenCell);
                row.WomenSetting = womenSetting != null;
                row.WomenSettingEnabled = IsControlEnabled(womenSetting);
                row.WomenSettingValue = ReadSettingValue(womenSetting);
            }
            else
            {
                var fake = FindDescendantNamed(womenCell, "FakeCombobox");
                row.WomenNone = fake != null ? FirstVisibleText(fake) : null;
            }
            row.WomenCount = ReadCount(womenCell);
        }

        row.HasDelete = FindDescendantNamed(rowEl, "league-remove") is VisualElement rem
            && FindDescendantNamed(rem, "buttons-controls-pc-default") != null;

        return row;
    }

    // The rule dropdown's current value: the "label" SIText inside its SelectionField (icon-only
    // setting dropdowns have none, so this is read only for rule dropdowns). Degrades to a marker
    // rather than a blank if the dropdown is present but its label can't be read.
    private static string ReadDropdownValue(VisualElement dropdown)
    {
        if (dropdown == null) return null;
        var field = FindDescendantNamed(dropdown, "SelectionField");
        var label = field != null ? FindDescendantNamed(field, "label") : null;
        var text = label != null ? FirstVisibleText(label) : null;
        return string.IsNullOrEmpty(text) ? Degrade.Value("league table: rule value unreadable") : text;
    }

    // The same enabled signal ServerText.IsDisabled and DropdownSession.Open read, so a dropdown
    // rendered openable is one the game will actually open. An unreadable state presents as
    // enabled: inventing unavailability the game never claimed is its own kind of lie.
    private static bool IsControlEnabled(VisualElement el)
    {
        if (el == null) return false;
        try { return el.enabledInHierarchy; }
        catch (System.Exception e)
        {
            Degrade.Note($"league table: control enabled-state unreadable ({e.GetType().Name}); presenting as enabled");
            return true;
        }
    }

    private static string ReadCount(VisualElement cell)
    {
        var frame = FindDescendantNamed(cell, "Frame 1000003311");
        return frame != null ? FirstVisibleText(frame) : null;
    }

    // The playable-setting picker's current value word. The value is icon-only: an SIStyleSetter
    // applies a success-tick for Playable and an eye for the two view states, which the icon alone
    // can't tell apart — so the eye state is split by the dropdown's selected index (live-probed:
    // Playable=0/tick, "Playable / View-Only Below"=1/eye, "View Only"=eye with index not 1, the
    // game leaving View Only's SelectedIndex untracked at -1). An unrecognised icon class is logged,
    // never guessed into a word, so the cell never shows a setting the game isn't showing.
    private static string ReadSettingValue(VisualElement settingDropdown)
    {
        if (settingDropdown == null) return null;
        var field = FindDescendantNamed(settingDropdown, "SelectionField");
        var setter = field != null ? FindDescendantOfType(field, "SIStyleSetter") : null;
        if (setter == null) return null;
        string cls;
        try { cls = Adapters.StyleSetterClasses.Read(setter); }
        catch (System.Exception e)
        {
            Degrade.Note($"league setting: applied style classes could not be read: {e.GetType().Name}: {e.Message}");
            return null;
        }
        if (string.IsNullOrEmpty(cls)) return null;
        if (cls.Contains("tick_circle_success")) return "Playable";
        if (cls.Contains("view_small"))
        {
            int idx;
            try { idx = new Adapters.DropdownAdapter(settingDropdown).GetSelectedIndex(); }
            catch { idx = -1; }
            return idx == 1 ? "Playable / View-Only Below" : "View Only";
        }
        if (_loggedSettingClasses.Add(cls))
            Speech.Log.Info($"[LeagueSetting] unmapped setting icon class '{cls}' (add to ReadSettingValue mapping)");
        return null;
    }

    private static VisualElement FindRuleDropdown(VisualElement cell)
        => cell != null ? FindDescendantNamed(cell, "dropdown-base-regular-trailing-icon-label") : null;

    private static VisualElement FindSettingDropdown(VisualElement cell)
        => cell != null ? FindDescendantNamed(cell, "inputs-combobox-default-default") : null;

    // --- Virtualised list access ---

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
        return FindDescendantNamed(contentVE, "frame league-row") ?? contentVE;
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

    private static void CollectVisibleTexts(VisualElement root, List<string> texts, int max)
    {
        if (texts.Count >= max || !ElementBridge.IsVisible(root)) return;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = ReadText(root);
            if (!string.IsNullOrEmpty(t)) { texts.Add(t); if (texts.Count >= max) return; }
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            CollectVisibleTexts(root.hierarchy[i], texts, max);
            if (texts.Count >= max) return;
        }
    }

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

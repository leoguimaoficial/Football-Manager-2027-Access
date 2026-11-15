using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Structural reader for the Calendar tool's Fixture Schedule: a column-header row (DynamicItem
/// "Header" of per-column sort buttons) over a streamed, virtualised list (StreamedObjectList
/// "FixturesList") of fixture rows. Each row is a "FixtureInfo" frame whose cells are children
/// named after their column ("Date", "Time", "Opposition", ...), plus a row-selection SICheckBox
/// and an "IsOnTV" flag. Rows virtualise and their cells stream-bind asynchronously, so reading
/// follows the StreamedTableReader protocol (scroll a row in, wait for its binders to report
/// ready, read once). This mirrors TableContext's machinery rather than reusing it because that
/// class is built around the game's StreamedTable widget and this list is a plain
/// StreamedObjectList with a bespoke row template. Read live, never cached: row models are plain
/// strings (virtualised elements recycle, so holding their references would go stale), and every
/// interaction re-realises its control by data index through <see cref="RealizeControl"/>.
/// </summary>
internal sealed class FixtureScheduleContext
{
    private static PropertyInfo _indexProperty;
    private static PropertyInfo _contentProperty;
    private static MethodInfo _scrollToMethod;
    private static PropertyInfo _siTextBinderProperty;
    private static PropertyInfo _binderIsReadyProperty;
    private static PropertyInfo _visibleValueProperty;
    private static PropertyInfo _visibleBinderProperty;
    private static PropertyInfo _switchBinderProperty;
    private static bool _reflectionInitialized;

    private readonly VisualElement _list; // the StreamedObjectList("FixturesList")
    private readonly IReadOnlyList<string> _columnKeys;

    public FixtureScheduleContext(VisualElement list, IReadOnlyList<string> columnKeys)
    {
        EnsureReflection();
        _list = list;
        _columnKeys = columnKeys;
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

        // The per-cell binding-completion signal, same as TableContext: SIText.m_binder.IsReady
        // flips true the frame a cell's value is resolved and applied.
        _siTextBinderProperty = typeof(SI.Bindable.SIText).GetProperty("m_binder",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_siTextBinderProperty == null)
            throw new System.MissingMemberException("SIText", "m_binder");
        _binderIsReadyProperty = typeof(SI.Bindable.Binder).GetProperty("IsReady",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_binderIsReadyProperty == null)
            throw new System.MissingMemberException("Binder", "IsReady");

        _visibleValueProperty = typeof(SI.Bindable.SIVisible).GetProperty("m_value",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_visibleValueProperty == null)
            Log.Warn("[FixtureSchedule] SIVisible.m_value not found; TV flags will not be shown.");

        // The readiness gate's second signal: a freshly realized row hides each cell behind an
        // SIVisible / BindableSwitchElement wrapper until that wrapper's binding applies, so the
        // wrappers' own binders must count as pending or a shell row (only its Date bound) reads
        // as ready with empty cells. Essential like the SIText binder above: without it the
        // schedule presents half-read rows as truth.
        _visibleBinderProperty = typeof(SI.Bindable.SIVisible).GetProperty("m_binder",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_visibleBinderProperty == null)
            throw new System.MissingMemberException("SIVisible", "m_binder");
        _switchBinderProperty = typeof(SI.Bindable.BindableSwitchElement).GetProperty("m_binder",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_switchBinderProperty == null)
            throw new System.MissingMemberException("BindableSwitchElement", "m_binder");
    }

    // --- Shape detection & headers (called by ScreenWalker at walk time) ---

    /// <summary>
    /// Whether this element is the Fixture Schedule wrapper: it holds both the column-header row
    /// and the streamed fixture list. The "Opposition" column gate keeps this off any other
    /// header/list pairing that happens to share the generic names.
    /// </summary>
    public static bool IsScheduleWrapper(VisualElement el)
    {
        var header = ScheduleHeader(el);
        if (header == null || FindDescendantNamed(header, "Opposition") == null) return false;
        return FindDescendantNamed(el, "FixturesList") != null;
    }

    // FM27's in-match club/nation profiles use the compact header template
    // over the same streamed FixtureInfo rows. Keep the original schedule
    // reader, sorting, selection, binding readiness and HTML.
    private static VisualElement ScheduleHeader(VisualElement wrapper)
        => FindDescendantNamed(wrapper, "Header") ?? FindDescendantNamed(wrapper, "HeaderSmall");

    /// <summary>One schedule column: its template key, visible label, and live sort button.</summary>
    internal sealed class HeaderCol
    {
        public string Key;            // the column template's name ("Selected", "TV", "Date", ...)
        public string Text;           // the visible header label ("Sel.", "Date", ...)
        public VisualElement Button;  // the sort button (headers are not virtualised, safe to hold)
        public string SortState;      // ColumnHeaderAdapter.GetState() form, "" when unsorted
        public bool HasContextMenu;   // the game's per-column options menu (insert/remove/replace, views)
    }

    /// <summary>
    /// The column headers in document order. Each column is a TemplateContainer (named for the
    /// column) holding one sort Button; the key comes from that container, the label from the
    /// button's text, the sort state from the button's arrow sprite.
    /// </summary>
    public static List<HeaderCol> ReadHeaders(VisualElement wrapper)
    {
        var cols = new List<HeaderCol>();
        var header = ScheduleHeader(wrapper);
        if (header == null) return cols;
        CollectHeaderButtons(header, cols);
        return cols;
    }

    private static void CollectHeaderButtons(VisualElement el, List<HeaderCol> cols)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "Button")
        {
            var adapter = new Adapters.ColumnHeaderAdapter(el);
            cols.Add(new HeaderCol
            {
                Key = el.hierarchy.parent?.name ?? "",
                Text = FirstVisibleText(el) ?? "",
                Button = el,
                SortState = adapter.GetState(),
                HasContextMenu = adapter.HasContextMenu,
            });
            return; // the sort arrow and label live inside the button; nothing nests further
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectHeaderButtons(el.hierarchy[i], cols);
    }

    // --- Row models ---

    internal enum CellKind { Content, Checkbox, TvFlag }

    internal sealed class Cell
    {
        public CellKind Kind;
        public bool On;         // Checkbox: checked; TvFlag: televised
        public string Text;     // Content: the cell's raw game text ("" when the cell shows nothing)
        public bool Clickable;  // Content: a live button backs the cell
        public bool HasMenu;    // Content: that button carries a right-click context menu
        public string Outcome;  // Result cell: "W"/"D"/"L" from the game's badge class, null when absent/unread
        public string UnmappedOutcomeClass; // Result cell: a badge class MapOutcome didn't recognize (noted once per load)
    }

    internal sealed class Row
    {
        public int DataIndex;
        public string NativeIdentity;
        public List<Cell> Cells; // aligned with the walk-time column keys
    }

    // --- StreamedTableReader ops (each runs in one MainThreadDispatcher.Invoke) ---

    /// <summary>
    /// Scrolls the row at <paramref name="dataIndex"/> into view and returns its count of not-ready
    /// cells (0 = fully bound), or -1 if no such row exists (past the last row).
    /// </summary>
    public int ScrollToRowPending(int dataIndex)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return -1;
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var row = FindRowByDataIndex(vl, dataIndex);
        return row == null ? -1 : CountPendingCells(row);
    }

    /// <summary>
    /// Re-checks the realized row's not-ready cell count, plus its model when that count is 0 — so
    /// the settle poll that confirms readiness also captures the row. While cells are still binding
    /// the row is null (the reader discards a not-ready poll's row, so building one is pure waste).
    /// </summary>
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

    /// <summary>Reads the realized row into a model. Null if not realized.</summary>
    public Row ReadRow(int dataIndex)
    {
        var vl = GetVirtualisedList();
        var row = vl != null ? FindRowByDataIndex(vl, dataIndex) : null;
        return row == null ? null : BuildRow(row, dataIndex);
    }

    /// <summary>
    /// The number of fixtures the list holds, or -1 when the list is not realized or unreadable.
    /// The schedule reads every row rather than a page (see FixtureScheduleLoader), so this is what
    /// bounds the read: an unreadable count means the reader cannot know where the season ends and
    /// falls back to a fixed cap it reports as truncation. Mirrors TableContext.RowCount.
    /// </summary>
    public int RowCount()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return -1;
        try
        {
            var vl = (SI.UI.VirtualisedList)virtList;
            var items = vl.Items;
            if (items == null) return -1;
            // Count lives on ICollection, which the IL2CPP interop IList does not surface directly.
            return items.Cast<Il2CppSystem.Collections.ICollection>().Count;
        }
        catch (System.Exception e)
        {
            Degrade.Note($"fixture schedule row count unreadable: {e.GetType().Name}: {e.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Reads currently-realized rows with data index in [<paramref name="startIdx"/>,
    /// <paramref name="endIdx"/>] whose cells are ALL bound (pending count 0), in one call and
    /// WITHOUT scrolling — the batch step of the streamed read (see TableContext.SweepReadyRows,
    /// whose contract this mirrors). The realized window cannot recycle mid-call (no frame advances
    /// inside a single main-thread dispatch); rows still binding — including empty shells, which
    /// <see cref="CountPendingCells"/> reports as pending — and rows not realized are left for the
    /// per-row scroll-and-settle path.
    ///
    /// <paramref name="maxRows"/> caps how many rows one call builds, so the caller can spend the
    /// window over several dispatches instead of holding the main thread for all of it (see
    /// HarvestSlicer). <paramref name="hitCap"/> is the caller's terminator and reports WHY the call
    /// stopped: false means the whole window was scanned and there is nothing further to harvest,
    /// true means the cap cut the scan short and another call has work to do. Duplicate-index
    /// reporting is the caller's to gate: a duplicate pair that never binds is never cached, so an
    /// ungated warning would repeat on every call.
    /// </summary>
    public List<KeyValuePair<int, Row>> SweepReadyRows(
        int startIdx, System.Func<int, bool> skip, int endIdx, int maxRows,
        bool logDuplicates, out bool hitCap)
    {
        hitCap = false;
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
                // Before the next row, not after the last one: a call that adds nothing still has to
                // stop, because scanning a window of still-binding rows costs a readiness walk each.
                if (result.Count >= maxRows) { hitCap = true; break; }
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                int idx = (int)_indexProperty.GetValue(virtElem);
                if (idx < startIdx || idx > endIdx) continue;
                // Rows the caller already holds (its harvest would discard the fresh model anyway)
                // are skipped before the readiness walk and model build.
                if (skip != null && skip(idx)) continue;
                if (!seen.Add(idx))
                {
                    if (logDuplicates)
                        Log.Warn($"[FixtureSchedule] SweepReadyRows: duplicate data index {idx}, keeping first.");
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
            Log.Warn($"[FixtureSchedule] SweepReadyRows failed after {result.Count} row(s): {e.GetType().Name}: {e.Message}");
        }
        return result;
    }

    /// <summary>
    /// Selects the fixture at <paramref name="dataIndex"/> through the list's own index-addressed
    /// selection entry (<c>VirtualisedList.SetSelection</c>). Current FM27's controller-only
    /// UpdateSelection can change the realized checkbox without notifying the bound fixture;
    /// the next harvest then restores the previous fixture. The public list operation notifies
    /// the native selection owner, as in the existing GridSelectorSelection adapter.
    /// Index-native, no synthetic checkbox click, supplemental event or ScrollTo.
    /// Label is the row's date and opposition when the row happens to be realised (best-effort).
    /// </summary>
    public bool SelectRow(int dataIndex, out string label)
    {
        label = null;
        var vlVE = FindChildOfType(_list, "VirtualisedList");
        if (vlVE == null) return false;
        var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)vlVE).Pointer);
        if (vl.Controller == null || !vl.IsItemSelectable(dataIndex)) return false;
        vl.SetSelection(dataIndex);

        var row = FindRowByDataIndex(vl, dataIndex);
        if (row != null)
        {
            var parts = new List<string>(2);
            foreach (var key in new[] { "Date", "Opposition" })
            {
                var cell = FindCellElement(row, key);
                var text = cell != null ? FirstVisibleText(cell) : null;
                if (!string.IsNullOrEmpty(text)) parts.Add(text);
            }
            if (parts.Count > 0) label = string.Join(", ", parts);
        }
        return true;
    }

    /// <summary>
    /// Re-realises (scrolls in) the row at <paramref name="dataIndex"/> and returns the live
    /// control backing the given column's cell button. Null while the recycled row's controls have
    /// not re-bound yet — the handle retries across frames, exactly like a streamed table cell.
    /// (The "Selected" column does not realize a control; it dispatches through
    /// <see cref="SelectRow"/> instead.)
    /// </summary>
    public VisualElement RealizeControl(int dataIndex, string columnKey)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return null;
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var row = FindRowByDataIndex(vl, dataIndex);
        if (row == null) return null;
        var cell = FindCellElement(row, columnKey);
        return cell == null ? null : FirstVisibleButton(cell);
    }

#if FM27_DEVELOPER_TOOLS
    /// <summary>
    /// Dev diagnostic: scrolls the row at <paramref name="dataIndex"/> in and, once realized,
    /// dumps its RAW content subtree (no FixtureInfo unwrap, so a foreign row template is visible)
    /// plus the backing item model's type — for identifying rows whose cells harvest empty. Null
    /// while the row is not realized yet (the caller polls across frames).
    /// </summary>
    public string DumpRow(int dataIndex)
    {
        var vl = GetVirtualisedList();
        if (vl == null) return "no VirtualisedList\n";
        _scrollToMethod.Invoke(vl, new object[] { dataIndex });
        var content = FindRawContent(vl, dataIndex);
        if (content == null) return null;
        var sb = new System.Text.StringBuilder();
        sb.Append("item: ").Append(DescribeItem(vl, dataIndex)).Append('\n');
        DumpNode(content, 0, sb);
        return sb.ToString();
    }

    // TreeDump plus per-node binder readiness — the probe exists to show which binder signal
    // separates a shell row from a bound one, which TreeDump has no reason to know about.
    private void DumpNode(VisualElement el, int depth, System.Text.StringBuilder sb)
    {
        var type = el.GetIl2CppType().Name;
        sb.Append(new string(' ', depth * 2)).Append(type);
        if (!string.IsNullOrEmpty(el.name)) sb.Append(" name=\"").Append(el.name).Append('"');
        sb.Append(" visible=").Append(ElementBridge.IsVisible(el));
        object binder = type switch
        {
            "SIText" => _siTextBinderProperty.GetValue(el.Cast<SI.Bindable.SIText>()),
            "SIVisible" => _visibleBinderProperty.GetValue(el.Cast<SI.Bindable.SIVisible>()),
            "BindableSwitchElement" => _switchBinderProperty.GetValue(el.Cast<SI.Bindable.BindableSwitchElement>()),
            _ => null,
        };
        if (binder != null)
            sb.Append(" binder=").Append((bool)_binderIsReadyProperty.GetValue(binder) ? "READY" : "PENDING");
        else if (type is "SIText" or "SIVisible" or "BindableSwitchElement")
            sb.Append(" binder=none");
        if (type == "SIText")
        {
            try { sb.Append(" text=\"").Append(Diagnostics.TreeDump.Truncate(el.Cast<TextElement>().text, 60)).Append('"'); }
            catch { sb.Append(" text=?"); }
        }
        sb.Append('\n');
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            DumpNode(el.hierarchy[i], depth + 1, sb);
    }

    // The active item's Content as-is — DumpRow must see rows whose template is NOT FixtureInfo,
    // which UnwrapRow (deliberately) reports as "not mounted yet".
    private VisualElement FindRawContent(object virtList, int dataIndex)
    {
        var vl = (SI.UI.VirtualisedList)virtList;
        int activeCount = vl.GetActiveCount();
        for (int i = 0; i < activeCount; i++)
        {
            var virtElem = vl.GetActiveItem(i);
            if (virtElem == null) continue;
            if ((int)_indexProperty.GetValue(virtElem) != dataIndex) continue;
            var content = _contentProperty.GetValue(virtElem);
            return content == null ? null : new VisualElement(((Il2CppSystem.Object)content).Pointer);
        }
        return null;
    }

    private static string DescribeItem(object virtList, int dataIndex)
    {
        try
        {
            var vl = (SI.UI.VirtualisedList)virtList;
            var items = vl.Items;
            if (items == null) return "no Items";
            var obj = items.Cast<Il2CppSystem.Collections.IList>()[dataIndex];
            if (obj == null) return "null";
            string str;
            try { str = obj.ToString(); } catch { str = "(ToString failed)"; }
            return obj.GetIl2CppType().FullName + " \"" + str + "\"";
        }
        catch (System.Exception e) { return "unreadable: " + e.GetType().Name + ": " + e.Message; }
    }

#endif

    // --- Row reading ---

    private Row BuildRow(VisualElement fixtureInfo, int dataIndex)
    {
        var row = new Row { DataIndex = dataIndex, NativeIdentity = ReadFixtureIdentity(fixtureInfo), Cells = new List<Cell>(_columnKeys.Count) };
        foreach (var key in _columnKeys)
            row.Cells.Add(BuildCell(fixtureInfo, key));
        return row;
    }

    // The native list's integer Items are model keys, not a displayed row's
    // identity. Its own listelement record contains the real FixtureReference;
    // Index and IsFirstFixtureOfTheMonth are presentation, not that identity.
    private string ReadFixtureIdentity(VisualElement fixtureInfo)
    {
        for (var up = fixtureInfo; up != null && up.Pointer != _list.Pointer; up = up.parent)
        {
            var node = Server.BindingRecords.NodeOf(up);
            if (node == null) continue;
            foreach (var child in node.Children)
            {
                if (child.Name != "listelement") continue;
                var record = Server.BindingRecords.RecordOf(Server.BindingRecords.ValueOf(child));
                if (record == null) continue;
                var fields = Server.BindingRecords.Fields(record);
                if (!fields.TryGetValue("Fixture", out var value)) continue;
                return Server.BindingRecords.ReferenceIdentity(value?.Get()?.TryCast<FM.UI.FixtureReference>());
            }
        }
        return null;
    }

    internal bool MatchesRowIdentity(int dataIndex, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        var vl = GetVirtualisedList();
        var row = vl == null ? null : FindRowByDataIndex(vl, dataIndex);
        return row != null && ReadFixtureIdentity(row) == expected;
    }

    private static Cell BuildCell(VisualElement fixtureInfo, string key)
    {
        if (key == "Selected")
        {
            var cb = FindDescendantOfType(fixtureInfo, "SICheckBox");
            if (cb == null) return new Cell { Kind = CellKind.Content, Text = "" };
            bool on;
            try { on = cb.Cast<Toggle>().value; }
            catch { return new Cell { Kind = CellKind.Content, Text = Degrade.Unavailable }; }
            return new Cell { Kind = CellKind.Checkbox, On = on };
        }

        if (key == "TV")
        {
            // The televised flag is an icon under the "IsOnTV" cell's SIVisible. Child presence is
            // NOT the signal: the icon is cloned lazily on first show and then survives row
            // recycling, so a recycled untelevised row still holds it (observed live: every row
            // read as televised once the whole list had been scrolled). Read the SIVisible's own
            // bound condition (m_value) instead — the binding system's truth.
            var tvCell = FindCellElement(fixtureInfo, "IsOnTV");
            var visFlag = tvCell != null ? FindDescendantOfType(tvCell, "SIVisible") : null;
            return new Cell { Kind = CellKind.TvFlag, On = visFlag != null && ReadVisibleValue(visFlag) };
        }

        var cell = FindCellElement(fixtureInfo, key);
        if (cell == null)
            // A column the header declares but the row template lacks — a structural mismatch, so
            // an honest marker, not a blank that reads as "no data". Logged once per load by the
            // loader (a per-row Note here would spam once per settle poll).
            return new Cell { Kind = CellKind.Content, Text = Degrade.Unavailable };

        var btn = FirstVisibleButton(cell);
        var result = new Cell
        {
            Kind = CellKind.Content,
            Text = FirstVisibleText(cell) ?? "",
            Clickable = btn != null,
            HasMenu = btn != null && Adapters.ContextMenuOpener.HasContextMenu(btn),
        };

        // A played fixture's result badge is coloured by an SIStyleSetter ("result") whose applied
        // USS class is the game's own win/draw/defeat signal ("game-concepts-win_small", ...) — the
        // visual a sighted player reads the outcome from. Read it into a W/D/L marker for the
        // score. An unmapped class leaves Outcome null and is noted once per load by the loader.
        if (key == "Result" && btn != null && !string.IsNullOrEmpty(result.Text))
        {
            var setter = FindDescendantNamed(btn, "result") ?? FindDescendantNamed(btn, "IconStyleSetter");
            string cls = setter != null ? ReadAddedClass(setter) : null;
            result.Outcome = MapOutcome(cls);
            if (result.Outcome == null && !string.IsNullOrEmpty(cls))
                result.UnmappedOutcomeClass = cls;
        }
        return result;
    }

    private static string MapOutcome(string ussClass)
    {
        if (string.IsNullOrEmpty(ussClass)) return null;
        // Reuse the standings reader's official localized abbreviations. Only
        // actual outcome-class families qualify; unrelated style words do not.
        foreach (var token in ussClass.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("game-concepts-win_", StringComparison.Ordinal))
                return Server.Features.TeamFormReader.ResultLetter(3);
            if (token.StartsWith("game-concepts-draw_", StringComparison.Ordinal))
                return Server.Features.TeamFormReader.ResultLetter(1);
            if (token.StartsWith("game-concepts-defeat_", StringComparison.Ordinal))
                return Server.Features.TeamFormReader.ResultLetter(0);
        }
        return null;
    }

    // The SIVisible's bound condition (IL2CPP exposes the private m_value field as a property).
    // Supplementary read: a failure degrades to "no TV flag" with a note, never blocks the row.
    private static bool ReadVisibleValue(VisualElement visFlag)
    {
        try
        {
            var v = _visibleValueProperty?.GetValue(visFlag.Cast<SI.Bindable.SIVisible>());
            return v is bool b && b;
        }
        catch (System.Exception e)
        {
            Degrade.Note($"fixture schedule: TV flag read failed: {e.GetType().Name}");
            return false;
        }
    }

    // The style setter's actual applied class(es). A failure degrades to "no outcome letter", never blocks the
    // row, and logs so the gap is diagnosable.
    private static string ReadAddedClass(VisualElement setter)
    {
        try
        {
            return Adapters.StyleSetterClasses.Read(setter);
        }
        catch (System.Exception e)
        {
            Degrade.Note($"fixture schedule: result style class read failed: {e.GetType().Name}");
            return null;
        }
    }

    // A row's cell container is the FixtureInfo child named for its column ("Date", "Time", ...).
    // The TV column's container is named "IsOnTV" (the caller maps the header key).
    private static VisualElement FindCellElement(VisualElement fixtureInfo, string name)
        => FindChildNamed(fixtureInfo, name);

    // --- Virtualised list access ---

    private object GetVirtualisedList()
    {
        var vlVE = FindChildOfType(_list, "VirtualisedList");
        if (vlVE == null) return null;
        return new SI.UI.VirtualisedList(((Il2CppSystem.Object)vlVE).Pointer);
    }

    // The realized row content for a data index: the active VirtualisedElement whose Index matches,
    // unwrapped to its "FixtureInfo" frame (the cells' parent). Null when not realized, or while
    // the item shell exists but the FixtureInfo template has not mounted yet (both mean "wait").
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

    // The active item's Content unwrapped to the "FixtureInfo" frame (the cells' parent). Null when
    // the item shell exists but the FixtureInfo template has not mounted yet (means "wait").
    private static VisualElement UnwrapRow(object content)
    {
        if (content == null) return null;
        var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
        return FindDescendantNamed(contentVE, "FixtureInfo");
    }

    // Same readiness signal as TableContext.CountPendingCells: visible SITexts whose binder has
    // not applied its value yet; zero bound texts means the shell is still empty, so keep waiting.
    private int CountPendingCells(VisualElement row)
    {
        int bound = 0, notReady = 0;
        CountReadiness(row, ref bound, ref notReady);
        if (bound == 0) return 1;
        if (notReady > 0) return notReady;
        // Binder IsReady is NOT trustworthy on a freshly recycled row: probed live, every binder in
        // the shell reports READY while the texts are still cleared from the previous tenant, and
        // rebinding flips them PENDING only a frame or two later. A row caught in that gap harvests
        // as "ready" with a date and nothing else, and the missing result then files a played
        // fixture under "Upcoming fixtures" (observed live). Every fixture has a date, a venue
        // letter and an opposition once bound (a club link or the no-team branch both render text),
        // so an empty one of those means mid-bind, not "no data". A row that genuinely never grows
        // them is read at the settle budget's end at its honest current state, not dropped.
        int missing = 0;
        foreach (var key in RequiredCellKeys)
        {
            var cell = FindCellElement(row, key);
            if (cell == null || string.IsNullOrEmpty(FirstVisibleText(cell))) missing++;
        }
        return missing;
    }

    private static readonly string[] RequiredCellKeys = { "Date", "Venue", "Opposition" };

    private void CountReadiness(VisualElement el, ref int bound, ref int notReady)
    {
        // The visibility wrappers are checked BEFORE the visibility cut: a cell that has not bound
        // yet is hidden behind its SIVisible / BindableSwitchElement, so a purely visible walk sees
        // a freshly realized shell row (only its Date applied) as 0 pending and it gets harvested
        // half-empty — observed live as played fixtures rendered date-only under "Upcoming
        // fixtures". The wrapper's own binder is the applied-or-not truth for the cell it gates.
        var type = el.GetIl2CppType().Name;
        if (type == "SIVisible")
            CountBinder(_visibleBinderProperty.GetValue(el.Cast<SI.Bindable.SIVisible>()), ref bound, ref notReady);
        else if (type == "BindableSwitchElement")
            CountBinder(_switchBinderProperty.GetValue(el.Cast<SI.Bindable.BindableSwitchElement>()), ref bound, ref notReady);

        if (!ElementBridge.IsVisible(el)) return;
        if (type == "SIText")
            CountBinder(_siTextBinderProperty.GetValue(el.Cast<SI.Bindable.SIText>()), ref bound, ref notReady);
        int childCount = el.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
            CountReadiness(el.hierarchy[i], ref bound, ref notReady);
    }

    private void CountBinder(object binder, ref int bound, ref int notReady)
    {
        if (binder == null) return;
        bound++;
        if (!(bool)_binderIsReadyProperty.GetValue(binder)) notReady++;
    }

    // --- Tree helpers ---

    private static string FirstVisibleText(VisualElement root)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.GetIl2CppType().Name == "SIText")
        {
            try
            {
                var t = root.Cast<TextElement>().text;
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch { return null; }
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var t = FirstVisibleText(root.hierarchy[i]);
            if (t != null) return t;
        }
        return null;
    }

    private static VisualElement FirstVisibleButton(VisualElement root)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.GetIl2CppType().Name == "SIButton") return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var b = FirstVisibleButton(root.hierarchy[i]);
            if (b != null) return b;
        }
        return null;
    }

    private static VisualElement FindChildNamed(VisualElement parent, string name)
    {
        int n = parent.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (parent.hierarchy[i].name == name) return parent.hierarchy[i];
        return null;
    }

    private static VisualElement FindChildOfType(VisualElement parent, string typeName)
    {
        int n = parent.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (parent.hierarchy[i].GetIl2CppType().Name == typeName) return parent.hierarchy[i];
        return null;
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

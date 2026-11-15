using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

internal sealed class TableContext : IContainerContext
{
    private readonly VisualElement _container;
    private readonly ClassificationCache _cache;

    private static PropertyInfo _listElementProperty;
    private static PropertyInfo _columnHeadersProperty;
    private static PropertyInfo _virtualisedElementContentProperty;
    private static MethodInfo _scrollToMethod;
    private static PropertyInfo _siTextBinderProperty;

    private const string VirtItemUssClass = "virtualised-list__item";

    public object Scope => _container;

    internal TableContext(VisualElement container, ClassificationCache cache)
    {
        _container = container;
        _cache = cache;
        EnsureReflection();
    }

    public bool HandlesDirection(NavigationDirection direction) => true;

    public bool ExitsOnBoundary(NavigationDirection direction) => false;

    public string GetBoundaryAnnouncement(NavigationDirection direction) => direction switch
    {
        NavigationDirection.Left => "left edge of table",
        NavigationDirection.Right => "right edge of table",
        NavigationDirection.Up => "top edge of table",
        NavigationDirection.Down => "bottom edge of table",
        _ => null
    };

    public IAccessibleElement GetFirstChild()
    {
        return TreeWalker.FindFirstStopInContainer(_container, _cache);
    }

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        return NavigateCell(current, direction);
    }

    public IAccessibleElement NavigateCell(IAccessibleElement current, NavigationDirection direction)
    {
        var backing = ((PluginAdapterBase)current).BackingElement;

        var headerPanes = GetColumnHeaders();
        if (headerPanes != null && IsInHeaderArea(backing, headerPanes))
        {
            if (direction == NavigationDirection.Left || direction == NavigationDirection.Right)
                return NavigateHeaderColumn(backing, headerPanes, direction);
            if (direction == NavigationDirection.Down)
                return NavigateHeaderToData(backing, headerPanes);
            // Up from header = top of table boundary
            return null;
        }

        if (direction == NavigationDirection.Left || direction == NavigationDirection.Right)
            return NavigateColumn(current, backing, direction);

        if (direction == NavigationDirection.Up || direction == NavigationDirection.Down)
            return NavigateRow(backing, direction);

        return null;
    }

    public IAccessibleElement NavigateInnerCell(IAccessibleElement current, NavigationDirection direction)
    {
        if (direction != NavigationDirection.Left && direction != NavigationDirection.Right)
            return null;

        var backing = ((PluginAdapterBase)current).BackingElement;

        // No inner-cell navigation in header area
        var headerPanes = GetColumnHeaders();
        if (headerPanes != null && IsInHeaderArea(backing, headerPanes))
            return null;

        var (_, rowContent) = FindVirtualisedItemInfo(backing);
        if (rowContent == null) return null;

        int colIndex = FindColumnIndex(rowContent, backing);
        if (colIndex < 0) return null;

        var cell = NthColumnChild(rowContent, colIndex);
        if (cell == null) return null;

        var stops = FindVisibleStops(cell);
        if (stops.Count <= 1) return null;

        int innerIndex = FindInnerIndex(current, stops);
        int step = direction == NavigationDirection.Right ? 1 : -1;
        int nextInner = innerIndex + step;
        if (nextInner >= 0 && nextInner < stops.Count)
            return stops[nextInner];

        return null;
    }

    public bool IsAtExitBoundary(IAccessibleElement current, NavigationDirection direction)
    {
        var backing = ((PluginAdapterBase)current).BackingElement;

        // Up from header = exit
        if (direction == NavigationDirection.Up)
        {
            var headerPanes = GetColumnHeaders();
            if (headerPanes != null && IsInHeaderArea(backing, headerPanes))
                return true;
        }

        // Down from last row = exit
        if (direction == NavigationDirection.Down)
        {
            var headerPanes = GetColumnHeaders();
            if (headerPanes != null && IsInHeaderArea(backing, headerPanes))
                return false;

            var (dataIndex, rowContent) = FindVirtualisedItemInfo(backing);
            if (rowContent == null) return false;

            var virtList = GetVirtualisedList();
            if (virtList == null) return false;

            int step = 1;
            int targetIndex = dataIndex + step;
            ScrollTo(virtList, targetIndex);
            var targetRow = FindRowByDataIndex(targetIndex);
            return targetRow == null;
        }

        return false;
    }

    // ── Header navigation ───────────────────────────────────────────────

    private VisualElement GetColumnHeaders()
    {
        try
        {
            var streamedTable = new SI.Bindable.StreamedTable(_container.Pointer);
            var headers = _columnHeadersProperty.GetValue(streamedTable);
            if (headers == null) return null;
            return new VisualElement(((Il2CppSystem.Object)headers).Pointer);
        }
        catch
        {
            Speech.Log.Warn("[Table] Failed to get column headers from StreamedTable.");
            return null;
        }
    }

    private static bool IsInHeaderArea(VisualElement descendant, VisualElement headerPanes)
    {
        var headerPtr = headerPanes.Pointer;
        var current = descendant;
        while (current != null)
        {
            if (current.Pointer == headerPtr)
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    private static int FindHeaderPaneIndex(VisualElement descendant, VisualElement headerPanes)
    {
        var headerPtr = headerPanes.Pointer;
        var current = descendant;
        while (current != null)
        {
            var parent = current.hierarchy.parent;
            if (parent != null && parent.Pointer == headerPtr)
            {
                int count = parent.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    if (parent.hierarchy[i].Pointer == current.Pointer)
                        return i;
                }
                return -1;
            }
            current = parent;
        }
        return -1;
    }

    private IAccessibleElement NavigateHeaderColumn(VisualElement current, VisualElement headerPanes, NavigationDirection direction)
    {
        int paneIndex = FindHeaderPaneIndex(current, headerPanes);
        if (paneIndex < 0)
        {
            Speech.Log.Warn("[Table] Could not determine header pane index.");
            return null;
        }

        int step = direction == NavigationDirection.Right ? 1 : -1;
        int paneCount = headerPanes.hierarchy.childCount;

        for (int next = paneIndex + step; next >= 0 && next < paneCount; next += step)
        {
            var pane = headerPanes.hierarchy[next];
            if (!ElementBridge.IsVisible(pane))
                continue;
            var stop = TreeWalker.FindFirstStopInContainer(pane, _cache);
            if (stop != null)
                return stop;
        }

        return null;
    }

    private IAccessibleElement NavigateHeaderToData(VisualElement current, VisualElement headerPanes)
    {
        int paneIndex = FindHeaderPaneIndex(current, headerPanes);
        if (paneIndex < 0)
        {
            Speech.Log.Warn("[Table] Could not determine header pane index for down navigation.");
            return null;
        }

        var virtList = GetVirtualisedList();
        if (virtList == null) return null;

        ScrollTo(virtList, 0);

        var firstRow = FindRowByDataIndex(0);
        if (firstRow == null)
        {
            Speech.Log.Info("[Table] No data rows in table.");
            return null;
        }

        int cellCount = ColumnChildCount(firstRow);
        if (paneIndex < cellCount)
        {
            var cell = NthColumnChild(firstRow, paneIndex);
            return CreateCellElement(cell) ?? new EmptyCellAdapter(cell);
        }
        else
        {
            Speech.Log.Warn($"[Table] Header pane index {paneIndex} exceeds data row column count {cellCount}.");
        }

        return new EmptyCellAdapter(firstRow);
    }

    // ── Data cell navigation ────────────────────────────────────────────

    private IAccessibleElement NavigateColumn(IAccessibleElement currentElement, VisualElement backing, NavigationDirection direction)
    {
        var (_, rowContent) = FindVirtualisedItemInfo(backing);
        if (rowContent == null)
        {
            Speech.Log.Warn("[Table] Could not find row element for column navigation.");
            return null;
        }

        int colIndex = FindColumnIndex(rowContent, backing);
        if (colIndex < 0)
        {
            Speech.Log.Warn("[Table] Could not determine column index.");
            return null;
        }

        int colStep = direction == NavigationDirection.Right ? 1 : -1;
        int nextCol = colIndex + colStep;
        int colCount = ColumnChildCount(rowContent);

        if (nextCol >= 0 && nextCol < colCount)
        {
            var nextCell = NthColumnChild(rowContent, nextCol);
            return CreateCellElement(nextCell) ?? new EmptyCellAdapter(nextCell);
        }

        return null;
    }

    private static int FindInnerIndex(IAccessibleElement current, List<IAccessibleElement> stops)
    {
        if (current is CellAdapter)
            return 0;

        if (current is PluginAdapterBase currentAdapter)
        {
            var currentBacking = currentAdapter.BackingElement;
            if (currentBacking == null) return 0;
            for (int i = 0; i < stops.Count; i++)
            {
                // A fresh wrapper can represent the same native inner stop.
                // Managed equality otherwise loses its position within the cell.
                if (stops[i] is PluginAdapterBase adapter && adapter.BackingElement?.Pointer == currentBacking.Pointer)
                    return i;
            }
        }

        return 0;
    }

    private IAccessibleElement NavigateRow(VisualElement current, NavigationDirection direction)
    {
        var (dataIndex, rowContent) = FindVirtualisedItemInfo(current);
        if (rowContent == null)
        {
            Speech.Log.Warn("[Table] Could not find row element for row navigation.");
            return null;
        }

        int colIndex = FindColumnIndex(rowContent, current);
        Speech.Log.Info($"[Table] NavigateRow: dataIndex={dataIndex}, colIndex={colIndex}, direction={direction}");
        if (colIndex < 0)
            colIndex = 0;

        int currentDataIndex = dataIndex;
        if (currentDataIndex < 0)
        {
            Speech.Log.Warn("[Table] Could not determine data index for current row.");
            return null;
        }

        var virtList = GetVirtualisedList();
        if (virtList == null) { Speech.Log.Warn("[Table] NavigateRow: virtList is null"); return null; }

        int step = direction == NavigationDirection.Down ? 1 : -1;
        int targetIndex = currentDataIndex + step;

        if (targetIndex < 0)
        {
            var headerPanes = GetColumnHeaders();
            if (headerPanes != null && headerPanes.hierarchy.childCount > colIndex)
            {
                var pane = headerPanes.hierarchy[colIndex];
                var stop = TreeWalker.FindFirstStopInContainer(pane, _cache);
                if (stop != null)
                    return stop;
            }
            return null;
        }

        ScrollTo(virtList, targetIndex);

        var targetRow = FindRowByDataIndex(targetIndex);
        if (targetRow == null)
        {
            Speech.Log.Info($"[Table] NavigateRow: no row at index {targetIndex} (end of table)");
            return null;
        }

        int targetColCount = ColumnChildCount(targetRow);
        Speech.Log.Info($"[Table] NavigateRow: targetRow.columns={targetColCount}, using colIndex={colIndex}");
        if (colIndex < targetColCount)
        {
            var cell = NthColumnChild(targetRow, colIndex);
            return CreateCellElement(cell) ?? new EmptyCellAdapter(cell);
        }

        return new EmptyCellAdapter(targetRow);
    }

    // ── Virtualised item lookup ─────────────────────────────────────────

    /// <summary>
    /// Walk up from a descendant to find the virtualised row.
    /// The DOM structure is: VirtualisedElement → BindingRoot (USS class) → row VisualElement (cells).
    /// Returns (dataIndex, rowContent) where rowContent is the VisualElement containing column cells.
    /// </summary>
    private (int dataIndex, VisualElement rowContent) FindVirtualisedItemInfo(VisualElement descendant)
    {
        // First try USS class approach
        var current = descendant;
        int walkSteps = 0;
        while (current != null && current.Pointer != _container.Pointer)
        {
            walkSteps++;
            if (IsVirtualisedItem(current))
            {
                int dataIdx = FindDataIndexForRow(current);
                // FM27's player database marks the cell-owning row itself as
                // the virtualized item. Its first child is SelectionCell, not
                // another row wrapper. Do not descend into that checkbox and
                // lose every column for native navigation/caption resolution.
                VisualElement rowContent = ColumnChildCount(current) > 0 ? current
                    : current.hierarchy.childCount > 0 ? FindRowContent(current.hierarchy[0]) : current;
                Speech.Log.Debug($"[Table] USS hit: dataIdx={dataIdx}, rowContent.children={rowContent.hierarchy.childCount}");
                return (dataIdx, rowContent);
            }
            current = current.hierarchy.parent;
        }
        Speech.Log.Debug($"[Table] USS class NOT found after {walkSteps} steps. Trying ActiveElements fallback.");

        // Log ancestor chain for diagnostics
        current = descendant;
        var chain = new System.Text.StringBuilder("[Table] Ancestor chain: ");
        while (current != null && current.Pointer != _container.Pointer)
        {
            chain.Append($"{current.GetIl2CppType().Name}→");
            current = current.hierarchy.parent;
        }
        Speech.Log.Debug(chain.ToString());

        // Fallback: use ActiveElements to find which VirtualisedElement contains this descendant
        var virtList = GetVirtualisedList();
        if (virtList == null)
        {
            Speech.Log.Warn("[Table] GetVirtualisedList() returned null in fallback.");
            return (-1, null);
        }

        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            int activeCount = vl.GetActiveCount();
            Speech.Log.Debug($"[Table] ActiveElements: count={activeCount}, descendant.Pointer={descendant.Pointer}");
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) { Speech.Log.Debug($"[Table] ActiveItem[{i}] is null"); continue; }
                var content = _virtualisedElementContentProperty.GetValue(virtElem);
                if (content == null) { Speech.Log.Debug($"[Table] ActiveItem[{i}].Content is null"); continue; }
                var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                int idx = virtElem.Index;
                bool isMatch = contentVE.Pointer == descendant.Pointer || TreeWalker.IsDescendantOf(descendant, contentVE);
                Speech.Log.Debug($"[Table] ActiveItem[{i}]: idx={idx}, contentPtr={contentVE.Pointer}, match={isMatch}");
                if (isMatch)
                {
                    var rowContent = FindRowContent(contentVE);
                    Speech.Log.Debug($"[Table] Match found! dataIdx={idx}, rowContent.children={rowContent.hierarchy.childCount}");
                    return (idx, rowContent);
                }
            }
            Speech.Log.Warn($"[Table] No ActiveElement matched descendant.");
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Table] ActiveElements fallback failed: {e.GetType().Name}: {e.Message}");
        }

        return (-1, null);
    }

    private static VisualElement FindRowContent(VisualElement root)
    {
        // Walk down single-child chains until we hit a node with multiple children (the row cells).
        // Some tables wrap cells inside a container (e.g. FMDraggableObject) that is itself inside
        // a node with only 2-3 children. When we stop at a node with few children and one child
        // has far more children than the rest, drill into that child — the real cells are there.
        // Stop as soon as a node directly holds a column cell: a single-column row (one Cell per
        // row, e.g. the coach Tendencies table) is a single-child chain right through its lone Cell,
        // so an unconditional descent would overshoot past the Cell into its icon/text content and
        // report zero columns. A multi-column row stops here anyway (its cells make childCount > 1).
        var current = root;
        while (current.hierarchy.childCount == 1 && ColumnChildCount(current) == 0)
            current = current.hierarchy[0];

        if (current.hierarchy.childCount >= 2 && current.hierarchy.childCount <= 3)
        {
            int bestIndex = -1;
            int bestCount = 0;
            int secondBest = 0;
            for (int i = 0; i < current.hierarchy.childCount; i++)
            {
                int cc = current.hierarchy[i].hierarchy.childCount;
                if (cc > bestCount)
                {
                    secondBest = bestCount;
                    bestCount = cc;
                    bestIndex = i;
                }
                else if (cc > secondBest)
                {
                    secondBest = cc;
                }
            }

            if (bestIndex >= 0 && bestCount >= 5 && bestCount > secondBest * 3)
                current = current.hierarchy[bestIndex];
        }

        // If we still have no Cell children, search deeper for a descendant that contains them.
        // This handles tables like the Tactics Planner where cells are nested behind
        // ContextShortcutsElement > FMDraggableObject wrappers.
        if (ColumnChildCount(current) == 0)
        {
            var cellParent = FindDescendantWithCells(current, 5);
            if (cellParent != null)
                current = cellParent;
        }

        return current;
    }

    private static VisualElement FindDescendantWithCells(VisualElement root, int maxDepth)
    {
        if (maxDepth <= 0) return null;
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (ColumnChildCount(child) >= 3)
                return child;
            var deeper = FindDescendantWithCells(child, maxDepth - 1);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private static bool IsVirtualisedItem(VisualElement element)
    {
        try
        {
            return element.ClassListContains(VirtItemUssClass);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Find the data index for a row by iterating VirtualisedList.ActiveElements
    /// and matching the Content pointer against ancestors of the USS-classed element.
    /// </summary>
    private int FindDataIndexForRow(VisualElement ussElement)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return -1;

        // Collect ancestor pointers from ussElement up to _container
        var ancestors = new HashSet<System.IntPtr>();
        var cur = ussElement;
        while (cur != null && cur.Pointer != _container.Pointer)
        {
            ancestors.Add(cur.Pointer);
            cur = cur.hierarchy.parent;
        }

        return FindDataIndexByAncestors(virtList, ancestors);
    }

    private int FindDataIndexByAncestors(object virtList, HashSet<System.IntPtr> ancestorPointers)
    {
        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            int activeCount = vl.GetActiveCount();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                var content = _virtualisedElementContentProperty.GetValue(virtElem);
                if (content == null) continue;
                var contentPtr = ((Il2CppSystem.Object)content).Pointer;
                if (ancestorPointers.Contains(contentPtr))
                    return virtElem.Index;
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Table] FindDataIndexByAncestors failed: {e.GetType().Name}: {e.Message}");
        }
        return -1;
    }

    private static bool IsColumnCell(VisualElement element)
    {
        try
        {
            var typeName = element.GetIl2CppType().Name;
            return typeName == "Cell" || typeName == "SelectionCell";
        }
        catch
        {
            return false;
        }
    }

    private static VisualElement NthColumnChild(VisualElement row, int logicalIndex)
    {
        int count = row.hierarchy.childCount;
        int seen = 0;
        for (int i = 0; i < count; i++)
        {
            var child = row.hierarchy[i];
            if (IsColumnCell(child))
            {
                if (seen == logicalIndex)
                    return child;
                seen++;
            }
        }
        return null;
    }

    private static int ColumnChildCount(VisualElement row)
    {
        int count = row.hierarchy.childCount;
        int cols = 0;
        for (int i = 0; i < count; i++)
        {
            if (IsColumnCell(row.hierarchy[i]))
                cols++;
        }
        return cols;
    }

    private int FindColumnIndex(VisualElement row, VisualElement descendant)
    {
        int childCount = row.hierarchy.childCount;
        int logicalIndex = 0;
        for (int i = 0; i < childCount; i++)
        {
            var child = row.hierarchy[i];
            if (child.Pointer == descendant.Pointer || TreeWalker.IsDescendantOf(descendant, child))
                return logicalIndex;
            if (IsColumnCell(child))
                logicalIndex++;
        }
        return -1;
    }

    private object GetVirtualisedList()
    {
        try
        {
            var streamedTable = new SI.Bindable.StreamedTable(_container.Pointer);
            return _listElementProperty.GetValue(streamedTable);
        }
        catch
        {
            Speech.Log.Warn("[Table] Failed to get VirtualisedList from StreamedTable.");
            return null;
        }
    }

    /// <summary>
    /// The scroll viewport the rows live in. ScrollTo does the minimum scroll that brings a row to
    /// an edge, which routinely leaves that row half outside this rect — and the game ignores a
    /// pointer whose position falls in the clipped part, so the click is swallowed with no sign.
    /// Activation checks containment against this before firing. Zero-size when the list is gone.
    /// </summary>
    public UnityEngine.Rect ViewportRect()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return default;
        return new VisualElement(((Il2CppSystem.Object)virtList).Pointer).worldBound;
    }

    /// <summary>
    /// Scroll one row PAST <paramref name="dataIndex"/>, in the direction it is clipped
    /// (<paramref name="step"/> is +1 for clipped at the bottom, -1 at the top), then back. The
    /// overshoot pushes the target a whole row clear of that edge, and because ScrollTo only ever
    /// scrolls the minimum needed, the return call then has nothing left to do and leaves it there.
    /// </summary>
    public void NudgeIntoView(int dataIndex, int step)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return;
        int past = dataIndex + step;
        if (past < 0) return;   // already at the top of the list; nowhere to overshoot to
        ScrollTo(virtList, past);
        ScrollTo(virtList, dataIndex);
    }

    private VisualElement FindRowByDataIndex(int dataIndex)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return null;

        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            int activeCount = vl.GetActiveCount();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                int idx = virtElem.Index;
                if (idx == dataIndex)
                {
                    var content = _virtualisedElementContentProperty.GetValue(virtElem);
                    if (content == null) continue;
                    var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                    return FindRowContent(contentVE);
                }
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Table] FindRowByDataIndex failed: {e.GetType().Name}: {e.Message}");
        }
        return null;
    }

    private void ScrollTo(object virtList, int index)
    {
        try
        {
            _scrollToMethod.Invoke(virtList, new object[] { index });
        }
        catch (System.Exception e)
        {
            Speech.Log.Error($"[Table] ScrollTo({index}) failed: {e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    private IAccessibleElement CreateCellElement(VisualElement cell)
    {
        var stops = FindVisibleStops(cell);
        if (stops.Count == 0)
            return TryCreateSpriteAdapter(cell);
        if (stops.Count == 1)
            return stops[0];
        return new CellAdapter(cell, _cache, stops[0]);
    }

    // The active panel profile's hide rules apply inside table cells exactly as they do to loose
    // stops, on both surfaces — without this, a hidden control stayed reachable whenever it sat
    // in a table (the tactics squad table's split-button role cells).
    private List<IAccessibleElement> FindVisibleStops(VisualElement cell)
    {
        var stops = TreeWalker.FindAllStopsInContainer(cell, _cache);
        // Native compact progress lines have no text children. Reuse one
        // readonly stop on BOTH table surfaces, including cell realization,
        // then apply the same profile visibility policy as every other stop.
        if (stops.Count == 0 && CompactLineChartAdapter.TryCreate(cell) is { } trend)
            stops.Add(trend);
        stops.RemoveAll(NavigationManager.IsHidden);
        RadialGaugeAdapter.FoldPairedCaptions(stops);
        DropdownAdapter.FoldExternalTableValues(stops);
        return stops;
    }

    /// <summary>
    /// Unwraps a cell element to its dropdown adapter: the element itself, or the first
    /// stop of a multi-stop cell. Null if the cell is not dropdown-backed.
    /// </summary>
    public static DropdownAdapter AsDropdown(IAccessibleElement el) => el switch
    {
        DropdownAdapter dd => dd,
        CellAdapter c when c.FirstStop is DropdownAdapter dd => dd,
        _ => null,
    };

    /// <summary>
    /// The table's total row count, read from the list's own bound data source — the same list the
    /// row indices address, so it counts every row whether or not it has ever been realized. Drives
    /// the HTML surface's paging ("Page 3 of 7"), which is why it is read rather than inferred from
    /// how far a read happened to get. Returns -1 when it cannot be read; the caller then leaves the
    /// table unpaged rather than showing a page count that might be wrong.
    /// </summary>
    public int RowCount()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return -1;
        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            var items = vl.Items;
            if (items == null) return -1;
            // Count lives on ICollection, which the IL2CPP interop IList does not surface directly.
            return items.Cast<Il2CppSystem.Collections.ICollection>().Count;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"table row count unreadable: {e.GetType().Name}: {e.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Scrolls the row at <paramref name="dataIndex"/> into view and returns its count of not-ready
    /// cells (0 = fully bound), or -1 if no such row exists (past the last row). One main-thread step
    /// of the streamed read (see <see cref="StreamedTableReader{TRow}"/>): the row element realizes
    /// synchronously with the scroll; its cell values bind over the following frames, and each cell's
    /// binder reports ready as its value lands. Folds the old scroll/exists and first check into one
    /// call to save a round-trip.
    /// </summary>
    public int ScrollToRowPending(int dataIndex)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return -1;
        ScrollTo(virtList, dataIndex);
        var row = FindRowByDataIndex(dataIndex);
        return row == null ? -1 : CountPendingCells(row);
    }

    /// <summary>
    /// Re-checks the realized row at <paramref name="dataIndex"/>: its current not-ready cell count,
    /// plus its full model when that count is 0 — so the settle poll that confirms readiness also
    /// captures the row without a second round-trip. While cells are still binding the row is null
    /// (the reader discards a not-ready poll's row, so building one is pure waste — a row that binds
    /// over 200ms used to build and throw away ~12 models). Pending 0 / null row if not realized.
    /// </summary>
    public (int pending, TableRow row) CountAndReadRow(int dataIndex)
    {
        var row = FindRowByDataIndex(dataIndex);
        if (row == null) return (0, null);
        int pending = CountPendingCells(row);
        var model = pending == 0 ? BuildRowModel(row, dataIndex) : null;
        return model != null && HasUnresolvedDropdown(model) ? (1, null) : (pending, model);
    }

    /// <summary>
    /// The realized row's count of not-ready cells (see <see cref="CountPendingCells"/>) WITHOUT
    /// scrolling, or -1 when the row is not realized. The binding system's own readiness signal,
    /// used by activation to tell a row that has landed from one still filling in.
    /// </summary>
    public int PendingCellsAt(int dataIndex)
    {
        var row = FindRowByDataIndex(dataIndex);
        return row == null ? -1 : CountPendingCells(row);
    }

    /// <summary>
    /// Reads the realized row at <paramref name="dataIndex"/> into a model row. Null if not realized.
    /// Fallback for the rare case where the settle loop never polled (a budget spent during scroll).
    /// </summary>
    public TableRow ReadRow(int dataIndex)
    {
        var row = FindRowByDataIndex(dataIndex);
        return row == null ? null : BuildRowModel(row, dataIndex);
    }

    /// <summary>
    /// Reads currently-realized rows with data index in [<paramref name="startIdx"/>,
    /// <paramref name="endIdx"/>] whose cells are ALL bound (pending count 0), in one call and
    /// WITHOUT scrolling. The batch step of the streamed read: the realized window cannot recycle
    /// mid-call (no frame advances inside a single main-thread dispatch), so this snapshots the
    /// window's ready rows at once; rows still binding — including empty shells, which
    /// <see cref="CountPendingCells"/> reports as pending — and rows not realized are left for the
    /// per-row scroll-and-settle path. A partial result after a mid-sweep failure is safe for the
    /// same reason: missing rows fall through to that path.
    ///
    /// <paramref name="maxRows"/> caps how many rows one call builds, so the caller can spend the
    /// window over several dispatches instead of holding the main thread for all of it (a 51-row
    /// squad table was a single 250-563ms hold before this existed — see HarvestSlicer).
    /// <paramref name="hitCap"/> is the caller's terminator and reports WHY the call stopped: false
    /// means the whole window was scanned and there is nothing further to harvest, true means the
    /// cap cut the scan short and another call has work to do. Rows already harvested are rejected
    /// by <paramref name="skip"/> before the readiness walk, so successive calls resume rather than
    /// repeat. Duplicate-index reporting is the caller's to gate: a duplicate pair that never binds
    /// is never cached, so an ungated warning would repeat on every call.
    /// </summary>
    public List<KeyValuePair<int, TableRow>> SweepReadyRows(
        int startIdx, System.Func<int, bool> skip, int endIdx, int maxRows,
        bool logDuplicates, out bool hitCap)
    {
        hitCap = false;
        var result = new List<KeyValuePair<int, TableRow>>();
        var virtList = GetVirtualisedList();
        if (virtList == null) return result;
        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            int activeCount = vl.GetActiveCount();
            var seen = new HashSet<int>();
            for (int i = 0; i < activeCount; i++)
            {
                // Before the next row, not after the last one: a call that adds nothing still has to
                // stop, because scanning a window of still-binding rows costs a readiness walk each.
                if (result.Count >= maxRows) { hitCap = true; break; }
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                int idx = virtElem.Index;
                if (idx < startIdx || idx > endIdx) continue;
                // Rows the caller already holds (its harvest would discard the fresh model anyway)
                // are skipped before the readiness walk and model build.
                if (skip != null && skip(idx)) continue;
                if (!seen.Add(idx))
                {
                    if (logDuplicates)
                        Speech.Log.Warn($"[Table] SweepReadyRows: duplicate data index {idx}, keeping first.");
                    continue;
                }
                var content = _virtualisedElementContentProperty.GetValue(virtElem);
                if (content == null) continue;
                var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                var row = FindRowContent(contentVE);
                if (row == null) continue;
                if (CountPendingCells(row) != 0) continue;
                var model = BuildRowModel(row, idx);
                if (HasUnresolvedDropdown(model)) continue;
                result.Add(new KeyValuePair<int, TableRow>(idx, model));
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Table] SweepReadyRows failed after {result.Count} row(s): {e.GetType().Name}: {e.Message}");
        }
        return result;
    }

    // Count of the row's bound text cells whose value has not yet been applied — the binding system's
    // own readiness signal, not a guess from the rendered text. Each SIText that participates in
    // binding carries a Binder; Binder.IsReady flips true the frame the cell's value is resolved and
    // applied (the slowest cell, e.g. a loan expiry date, lands frames after the rest). A row is
    // complete when this is 0. An empty-valued cell reports ready like a filled one, so a row with
    // genuinely fewer values still reaches 0 — no full-count to infer. Invisible subtrees (an inactive
    // switch branch) are skipped: their cells never bind and would never report ready.
    internal static int CountPendingCells(VisualElement row)
        => ReadBindingReadiness(row).PendingRowCells;

    internal static NativeBindingReadiness ReadBindingReadiness(VisualElement root)
    {
        int bound = 0, notReady = 0;
        CountReadiness(root, ref bound, ref notReady);
        // No bound text cells found yet (the row shell exists but its cells are not populated): treat
        // as not-ready for a required row. Optional footer cells must use only
        // the actual pending-binding count, not this row-shell inference.
        return new NativeBindingReadiness(bound, notReady);
    }

    /// <summary>
    /// Pending (not-ready) cell counts for every realized row with data index in
    /// [<paramref name="startIdx"/>, <paramref name="endIdx"/>] the caller does not already hold,
    /// in ONE pass over the virtualised window. The snapshot half of the streamed loader's stall
    /// detection: per-index <see cref="PendingCellsAt"/> rescans the whole active window per row,
    /// which priced an 11-row snapshot at ~400ms of IL2CPP marshalling alone. Ready rows (pending
    /// 0) are omitted — the loader's next harvest captures those with their values.
    /// </summary>
    public Dictionary<int, int> PendingCountsInWindow(int startIdx, int endIdx, System.Func<int, bool> skip)
    {
        var counts = new Dictionary<int, int>();
        var virtList = GetVirtualisedList();
        if (virtList == null) return counts;
        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            int activeCount = vl.GetActiveCount();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                int idx = virtElem.Index;
                if (idx < startIdx || idx > endIdx) continue;
                if (skip != null && skip(idx)) continue;
                if (counts.ContainsKey(idx)) continue;
                var content = _virtualisedElementContentProperty.GetValue(virtElem);
                if (content == null) continue;
                var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                var row = FindRowContent(contentVE);
                if (row == null) continue;
                int pending = CountPendingCells(row);
                if (pending > 0) counts[idx] = pending;
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Table] PendingCountsInWindow failed after {counts.Count} row(s): {e.GetType().Name}: {e.Message}");
        }
        return counts;
    }

    /// <summary>
    /// Reads the realized row at <paramref name="dataIndex"/> ONLY if its pending count still
    /// equals <paramref name="expectedPending"/> — the confirm-and-read half of stall detection,
    /// in one window scan instead of a <see cref="PendingCellsAt"/> plus a <see cref="ReadRow"/>.
    /// Null when the row is gone or its count moved (binding activity: the caller leaves it to
    /// the reader's settle path).
    /// </summary>
    public TableRow ReadRowIfPendingStill(int dataIndex, int expectedPending)
    {
        var row = FindRowByDataIndex(dataIndex);
        if (row == null) return null;
        if (CountPendingCells(row) != expectedPending) return null;
        var model = BuildRowModel(row, dataIndex);
        return HasUnresolvedDropdown(model) ? null : model;
    }

    // A native row may report its text binders ready before its sibling menu
    // caption is attached. Do not cache that partial interactive row as complete.
    private static bool HasUnresolvedDropdown(TableRow row)
    {
        foreach (var cell in row.Cells)
            foreach (var element in cell.Elements)
                if (element.IsDropdown && element.DropdownLabel == Speech.Degrade.UnknownControl) return true;
        return false;
    }

    // Diagnostic: names the SIText elements that keep a row's pending count above zero, so a row
    // that waits out its whole ready budget can say WHICH cell never bound.
    public string DescribePendingCells(int dataIndex)
    {
        var row = FindRowByDataIndex(dataIndex);
        if (row == null) return "row not realized";
        var parts = new List<string>();
        int bound = 0;
        DescribeReadiness(row, parts, ref bound);
        if (bound == 0) return "no bound SIText in row (empty shell)";
        return parts.Count == 0 ? "all ready now" : string.Join(" | ", parts);
    }

    private static void DescribeReadiness(VisualElement el, List<string> parts, ref int bound)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name is "FMLineChart" or "SILineChart"
            && CompactLineChartAdapter.TryCreate(el) is { } chart && !NavigationManager.IsHidden(chart))
        {
            bound++;
            if (!chart.IsReadyToRead) parts.Add("notready compact chart plotted data");
        }
        if (el.GetIl2CppType().Name == "SIText")
        {
            var binder = _siTextBinderProperty.GetValue(el.Cast<SI.Bindable.SIText>());
            if (binder != null)
            {
                bound++;
                var typedBinder = new SI.Bindable.Binder(((Il2CppSystem.Object)binder).Pointer);
                if (!typedBinder.IsReady)
                    parts.Add($"notready name=\"{el.name}\" parent=\"{el.hierarchy.parent?.name}\"");
            }
        }
        int childCount = el.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
            DescribeReadiness(el.hierarchy[i], parts, ref bound);
    }

    private static void CountReadiness(VisualElement el, ref int bound, ref int notReady)
    {
        // Reuse FixtureScheduleContext's FM26 rule: a visibility/switch binder
        // must settle before pruning the branch it controls. FM27 recycles these
        // wrappers while their old text can still report ready.
        var type = el.GetIl2CppType().Name;
        SI.Bindable.Binder boundary = type switch
        {
            "SIVisible" => new SI.Bindable.SIVisible(el.Pointer).m_binder,
            "BindableSwitchElement" => new SI.Bindable.BindableSwitchElement(el.Pointer).m_binder,
            _ => null,
        };
        if (boundary != null)
        {
            bound++;
            if (!boundary.IsReady) notReady++;
        }
        if (!ElementBridge.IsVisible(el)) return;
        if (type is "FMLineChart" or "SILineChart"
            && CompactLineChartAdapter.TryCreate(el) is { } chart && !NavigationManager.IsHidden(chart))
        {
            bound++;
            if (!chart.IsReadyToRead) notReady++;
        }
        if (type == "FMStarRatingTemp")
        {
            var rating = new FM.UI.FMStarRatingTemp(el.Pointer).m_binder;
            if (rating != null)
            {
                bound++;
                if (!rating.IsReady) notReady++;
            }
        }
        if (type == "SIText")
        {
            var binder = _siTextBinderProperty.GetValue(el.Cast<SI.Bindable.SIText>());
            if (binder != null)
            {
                bound++;
                // Typed read, not reflection: IsReady is a bool, and an IL2CPP reflection read of a
                // value type boxes it into a fresh IL2CPP object every call. This runs per cell, per
                // row, per walk — the mod's hottest read — so that is thousands of allocations per
                // table. Reference-typed reads above cost nothing and stay as they are.
                var typedBinder = new SI.Bindable.Binder(((Il2CppSystem.Object)binder).Pointer);
                if (!typedBinder.IsReady) notReady++;
            }
        }
        int childCount = el.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
            CountReadiness(el.hierarchy[i], ref bound, ref notReady);
    }

    // Builds a row's model: one TableCell per column, each carrying every interactive element the
    // cell holds (not just the first), so multi-control cells render in full on the HTML surface.
    // The column cells are collected in ONE pass over the row's children (NthColumnChild rescans
    // from child 0 per call, an O(cols²) type-name marshal on the mod's hottest path) and shared
    // with the selection-label pass.
    private TableRow BuildRowModel(VisualElement row, int dataIndex)
    {
        var rowModel = new TableRow { DataIndex = dataIndex, NativeIdentity = ReadRowIdentity(row) };
        var columnCells = CollectColumnCells(row);
        foreach (var cell in columnCells)
            rowModel.Cells.Add(BuildCellModel(cell));
        rowModel.SelectionLabel = BuildSelectionLabel(columnCells, rowModel);
        return rowModel;
    }

    // A StreamedTable's Items can be integer model keys, not entity records.
    // Its actual row BindingRoot exposes the referenced record as "binding".
    // Never search beyond this table or identify a row by its displayed name.
    private string ReadRowIdentity(VisualElement row)
    {
        for (var up = row; up != null && up.Pointer != _container.Pointer; up = up.hierarchy.parent)
        {
            var node = Server.BindingRecords.NodeOf(up);
            if (node == null) continue;
            foreach (var child in node.Children)
                if (child.Name == "binding")
                {
                    var binding = Server.BindingRecords.ValueOf(child);
                    var reference = Server.BindingRecords.ReferenceIdentity(
                        binding?.Get()?.TryCast<SI.Interop.InteropReference>());
                    if (reference != null) return reference;
                    // Only the demonstrated taker component has the type/side
                    // schema. Other inline rows (including team-talk reactions)
                    // retain their own actual player/team reference context.
                    return Server.SetPieceTakers.Block(_container) != null
                        ? Server.SetPieceTakers.RowIdentity(binding)
                        : Server.BindingRecords.InlineReferenceIdentity(binding);
                }
        }
        return null;
    }

    // Called after realizing the row in the SAME main-thread dispatch as its
    // action. Non-reference rows retain their inherited protocol; reference
    // rows must still name the record captured by the renderer.
    internal bool MatchesRowIdentity(int dataIndex, string expected)
        => expected == null || ReadRowIdentity(FindRowByDataIndex(dataIndex)) == expected;

    // The row's column cells in NthColumnChild's order: same loop, same IsColumnCell test, collected
    // once so per-column consumers index the list instead of rescanning the children.
    private static List<VisualElement> CollectColumnCells(VisualElement row)
    {
        var cells = new List<VisualElement>();
        int count = row.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = row.hierarchy[i];
            if (IsColumnCell(child)) cells.Add(child);
        }
        return cells;
    }

    // The name for the row's selection checkbox: the entity it selects plus its position/detail,
    // e.g. "Maxim De Cuyper, D/WB/M (L)" — not the row's full stat line. Find the name cell (the one
    // carrying the "NameText"/"NameButton" entity link) and pair its text with the next text-bearing
    // column. Uses the model's already-cleaned cell text, so markup never leaks. Null when the row
    // has no name cell (e.g. a file table), leaving the HTML surface its plain-text fallback.
    private string BuildSelectionLabel(List<VisualElement> columnCells, TableRow model)
    {
        int nameCol = -1;
        for (int c = 0; c < columnCells.Count; c++)
        {
            if (ElementBridge.HasNameDescendant(columnCells[c])) { nameCol = c; break; }
        }
        if (nameCol < 0) return null;

        string name = NameCellText(model.Cells[nameCol]);
        if (string.IsNullOrEmpty(name)) return null;

        // Detail comes only from plain-text cells (the squad picker's position column). A cell
        // holding a control would bake that control's LIVE value into the checkbox name — the
        // opposition instructions row read "Omar Pierce, Always" (its Tight Marking dropdown's
        // value), an identity label that goes stale the moment the player edits the row.
        for (int c = nameCol + 1; c < model.Cells.Count; c++)
        {
            if (CellHasControl(model.Cells[c])) continue;
            var detail = FirstCellText(model.Cells[c]);
            if (!string.IsNullOrEmpty(detail))
                return name + ", " + detail;
        }
        return name;
    }

    private static bool CellHasControl(TableCell cell)
    {
        foreach (var e in cell.Elements)
            if (e.IsDropdown || e.IsToggle || e.CanActivate) return true;
        return false;
    }

    private static string FirstCellText(TableCell cell)
    {
        foreach (var e in cell.Elements)
            if (!e.ExcludeFromSelectionLabel && !string.IsNullOrEmpty(e.Text)) return e.Text;
        return null;
    }

    // The name cell's entity link text (the NameButton that made this the name cell), not the
    // cell's leading text: a squad kit cell renders the shirt number before the name, and "14"
    // is no identity. A non-activatable NameText cell keeps the plain first-text read.
    private static string NameCellText(TableCell cell)
    {
        foreach (var e in cell.Elements)
            if (e.CanActivate && !e.IsToggle && !e.IsDropdown && !string.IsNullOrEmpty(e.Text))
                return e.Text;
        return FirstCellText(cell);
    }

    // A cell's full element list: every navigation stop it contains, each as a renderable element.
    // Mirrors CreateCellElement's stop walk, but keeps all stops instead of collapsing to the first.
    // No stops falls back to a sprite (an icon-only cell); a recycled/empty cell yields no elements.
    private TableCell BuildCellModel(VisualElement cell)
    {
        var model = new TableCell();
        if (cell == null) return model;
        var stops = FindVisibleStops(cell);
        if (stops.Count == 0)
        {
            // A standings team form cell is colour-only pips with no stop; its results are read
            // from the pips' bindings. Every other stopless cell pays one bounded name lookup.
            var teamForm = Server.Features.TeamFormReader.Read(cell);
            if (teamForm != null)
            {
                model.Elements.Add(new TableCellElement { Text = teamForm });
                return model;
            }
            var sprite = TryCreateSpriteAdapter(cell);
            if (sprite != null) model.Elements.Add(ToCellElement(sprite));
            return model;
        }
        bool soleControl = stops.Count(stop => stop.CanActivate) == 1;
        foreach (var stop in stops)
            model.Elements.Add(ToCellElement(stop, soleControl));

        // A form cell's button carries no text of its own — the ratings exist only as coloured
        // pips — so its element text is replaced with the ratings read from the pips' bindings
        // (most recent first). The stop itself is untouched: same button, same CellHandle, so
        // activation still opens the game's form card. Gated on the adapter type so every other
        // cell pays one type check and nothing else.
        for (int s = 0; s < stops.Count; s++)
        {
            if (stops[s] is not Adapters.FormButtonAdapter form) continue;
            var ratings = Server.Features.FormRatingsReader.Read(form.BackingElement);
            if (ratings == null) continue;
            var elem = model.Elements[s];
            elem.Text = ratings;
            model.Elements[s] = elem;
        }

        // The status-flag widget parents only its first badge into the row and builds the rest in a
        // hover popup, so this cell is read from the widget's data rather than from the stops above.
        // Keyed off the badge we already found, so the detection costs an ancestor walk, not a
        // second pass over the cell.
        model.StatusFlags = Server.Features.StatusFlagsReader.Read(
            cell, (stops[0] as Adapters.PluginAdapterBase)?.BackingElement, out bool flagsUnreadable);
        model.StatusFlagsUnreadable = flagsUnreadable;
        return model;
    }

    private static TableCellElement ToCellElement(IAccessibleElement el, bool soleControl = false)
    {
        var dd = AsDropdown(el);
        // A checkbox/toggle/radio cell renders as a native HTML <input> carrying its live checked
        // state, not a button: the game's "Sel." column is a per-row on/off the player must be able
        // to hear and flip. CheckBoxAdapter and ToggleAdapter both report role "checkbox"; a true
        // radio reports "radio button". Dropdown-backed cells are handled above and never reach this.
        string role = el.Role ?? "";
        bool isToggle = dd == null && (role == "checkbox" || role == "toggle" || role == "radio button");
        // The single-select row disclosure presents as a button but still carries the game's radio
        // state ("selected" on the one row the list has on), which the renderer needs for its
        // native radio input.
        bool isDisclosure = el is ToggleAdapter ta && ta.IsSingleSelectDisclosure;
        string state = isToggle || isDisclosure ? (el.GetState() ?? "") : "";
        var segmented = el as SegmentedToggleAdapter;
        return new TableCellElement
        {
            IsSegmented = segmented != null,
            SegmentOptions = segmented?.Options(),
            SegmentIndex = segmented?.SelectedIndex() ?? -1,
            // Visible text uses the HTML surface's single source of truth, which strips the role
            // suffix (the <td>/<button>/<select> carries the role): a player button reads "Name",
            // not "Name, button". GetAnnouncement is the speech-cursor string and must not leak here.
            Text = DisplayText.Of(el) ?? "",
            CanActivate = el.CanActivate,
            IsDropdown = dd != null,
            IsDisabled = Server.ServerText.IsDisabled(el),
            // Mirror the standalone dropdown path: a profile rename wins over the adapter label,
            // so in-cell dropdowns honor rename rules exactly like top-level ones.
            DropdownLabel = dd == null ? null : Server.ServerText.RenameOr(el, dd.GetLabel()),
            DropdownState = dd?.GetState(),
            // A rename is a deliberate name and must not be displaced by the header relabel.
            IsValueNamedDropdown = dd != null && (dd.IsValueNamed() || soleControl && dd.HasExternalTableValue())
                                   && string.IsNullOrEmpty(NavigationManager.GetRenameLabel(el)),
            IsToggle = isToggle,
            IsRadio = role == "radio button",
            IsChecked = state == "checked" || state == "on" || state == "selected",
            HasContextMenu = el.HasContextMenu,
            IsSelectionDisclosure = isDisclosure,
            ExcludeFromSelectionLabel = el is CompactLineChartAdapter,
        };
    }

    /// <summary>
    /// Visible column header texts, index-aligned with row cell columns.
    /// </summary>
    public List<string> GetHeaderTexts()
    {
        var result = new List<string>();
        var headerPanes = GetColumnHeaders();
        if (headerPanes == null) return result;
        int paneCount = headerPanes.hierarchy.childCount;
        for (int i = 0; i < paneCount; i++)
        {
            var pane = headerPanes.hierarchy[i];
            if (!ElementBridge.IsVisible(pane)) continue;
            var stop = TreeWalker.FindFirstStopInContainer(pane, _cache);
            result.Add(stop != null ? stop.GetAnnouncement() : "");
        }
        return result;
    }

    // A native icon/value command inside a real table can be named by its own
    // column. Resolve the same realized row/cell/header alignment used by table
    // navigation, never an ancestor's unrelated caption or a fixed column index.
    internal static string ReadColumnLabel(VisualElement element)
    {
        for (var ancestor = element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
        {
            if (ancestor.TryCast<SI.Bindable.StreamedTable>() == null) continue;
            var cache = new ClassificationCache();
            var context = new TableContext(ancestor, cache);
            var (_, row) = context.FindVirtualisedItemInfo(element);
            var headers = context.GetColumnHeaders();
            if (row == null || headers == null || ColumnChildCount(row) != headers.hierarchy.childCount)
                return null;
            int column = 0;
            for (int i = 0; i < row.hierarchy.childCount; i++)
            {
                var cell = row.hierarchy[i];
                if (!IsColumnCell(cell)) continue;
                if (cell.Pointer == element.Pointer || TreeWalker.IsDescendantOf(element, cell))
                {
                    var pane = headers.hierarchy[column];
                    if (!ElementBridge.IsVisible(pane)) return null;
                    var stop = TreeWalker.FindFirstStopInContainer(pane, cache);
                    var label = stop == null ? null : DisplayText.Of(stop);
                    return string.IsNullOrWhiteSpace(label) || label == "column header" ? null : label;
                }
                column++;
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// Visible column headers with their sort button and current sort state, index-aligned with row
    /// cell columns. A header backed by a clickable column-header button carries that button so the
    /// HTML surface can dispatch the game's own sort on activation; a non-button header carries none.
    /// </summary>
    public List<TableHeader> GetHeaders()
    {
        var result = new List<TableHeader>();
        var headerPanes = GetColumnHeaders();
        if (headerPanes == null) return result;
        int paneCount = headerPanes.hierarchy.childCount;
        for (int i = 0; i < paneCount; i++)
        {
            var pane = headerPanes.hierarchy[i];
            if (!ElementBridge.IsVisible(pane)) continue;
            var stop = TreeWalker.FindFirstStopInContainer(pane, _cache);
            // "column header" is a label-role in DisplayText, so this yields the bare column name
            // ("Age"), not the speech string "Age, column header"; the <th> conveys the role.
            var text = stop != null ? DisplayText.Of(stop) : "";
            // An action column (remove, reorder) has no name, so the header resolves to the bare
            // role word. That is not a column name and it is not a usable sort handle either:
            // render the <th> empty rather than showing "column header" as if it were the title.
            bool unnamed = string.Equals(text, "column header", StringComparison.OrdinalIgnoreCase);
            var header = new TableHeader { Text = unnamed ? "" : text };
            // A column-header button classifies as ColumnHeaderAdapter (any Button under the
            // "column-headers" panes); that is exactly the sortable handle and its live state.
            if (!unnamed && stop is ColumnHeaderAdapter cha)
            {
                header.HeaderButton = ((Adapters.PluginAdapterBase)cha).BackingElement;
                header.SortState = cha.GetState();
                // Snapshotted at walk time like CellInfo.HasContextMenu: the mark costs a scoped
                // Resolve walk, and the render path runs once per header per compose.
                header.HasContextMenu = cha.HasContextMenu;
            }
            result.Add(header);
        }
        return result;
    }

    /// <summary>
    /// True when the game declares this table's rows multi-selectable (the list's Many selection
    /// mode - the database tables whose Selection column of checkboxes feeds the bulk Actions
    /// tools). The walk-time gate for the surface's Select all / Clear selection buttons; an
    /// unreadable mode reads as false, so a table the game never declared multi-select can never
    /// grow buttons that do nothing.
    /// </summary>
    public bool IsMultiSelect()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return false;
        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
            return vl.SelectionMode == SI.UI.VirtualisedList.SelectionModes.Many;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"table selection mode unreadable ({e.GetType().Name}); no bulk-selection buttons");
            return false;
        }
    }

    /// <summary>
    /// Selects every row through the list's own range-selection entry - anchor row 0, then the
    /// shift-click range to the last row (<c>UpdateSelection(selectRange: true)</c>), so the game's
    /// Actions tools see exactly what a manual region selection produces. Addressed by data index
    /// against the bound source, so it covers the whole filtered table whether or not a row was
    /// ever realised. Returns the spoken reply; the count is read back from the game's selection
    /// rather than assumed, so a selection the game capped or refused is never announced as "all".
    /// </summary>
    public string SelectAllRows()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return Speech.Degrade.Value("table select all: list gone");
        int rows = RowCount();
        if (rows == 0) return "no rows";
        if (rows < 0) return Speech.Degrade.Value("table select all: row count unreadable");
        var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
        var controller = vl.Controller;
        if (controller == null) return Speech.Degrade.Value("table select all: no controller");
        controller.UpdateSelection(false, false, 0);
        controller.UpdateSelection(false, true, rows - 1);
        int selected = SelectedCount(vl);
        return selected < 0 ? "selected count unavailable, review the checkboxes"
            : $"{selected} selected";
    }

    /// <summary>Clears the list's row selection; the reply reads the count back like SelectAllRows.</summary>
    public string ClearRowSelection()
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return Speech.Degrade.Value("table clear selection: list gone");
        var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
        vl.ClearSelection();
        int selected = SelectedCount(vl);
        if (selected == 0) return "selection cleared";
        return selected < 0 ? "selected count unavailable, review the checkboxes"
            : $"{selected} still selected";
    }

    // The game's own count of selected rows, or -1 unreadable. Selection&lt;int&gt; is generic over a
    // struct, the interop shape whose members are unreliable; the non-generic IEnumerable cast
    // throws Il2CppException (verified live), so this reads the typed Count directly and degrades
    // if that path breaks too.
    private static int SelectedCount(SI.UI.VirtualisedList vl)
    {
        try
        {
            var sel = vl.Selection;
            if (sel == null) return -1;
            return sel.Cast<SI.UI.Selection<int>>().Count;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"table selected count unreadable ({e.GetType().Name})");
            return -1;
        }
    }

    /// <summary>
    /// Selects the row at <paramref name="dataIndex"/> through the list's own index-addressed selection
    /// entry (<c>VirtualisationController.UpdateSelection</c>) — the same path a real click ultimately
    /// reaches, so it selects the row AND opens its disclosure. Index-native: the list is read from the
    /// container directly and the index is the stored data index, so there is NO ScrollTo and no element
    /// re-realisation — it cannot be defeated by a recycled row's stale worldBound or a shared-scroll
    /// shift. Sets <paramref name="label"/> to the row's selection label for the announcement when the
    /// row happens to be realised (best-effort, no scroll). False if the list or its controller is gone.
    /// </summary>
    public bool SelectRow(int dataIndex, out string label)
    {
        label = null;
        var virtList = GetVirtualisedList();
        if (virtList == null) return false;
        var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtList).Pointer);
        var controller = vl.Controller;
        if (controller == null) return false;
        controller.UpdateSelection(false, false, dataIndex);
        label = ReadRow(dataIndex)?.SelectionLabel;
        return true;
    }

    /// <summary>
    /// Re-realises a single interactive element within a cell by data index, column, and the
    /// element's position among the cell's stops (scrolling the row into view first). Used to
    /// dispatch an action to the right control when a cell holds several (and the single-element
    /// case). Virtualised cell references from the row read recycle once scrolled away and cannot
    /// be held, so activation re-realises here. Null if the row, cell, or that element is gone.
    /// </summary>
    /// <param name="scroll">
    /// Whether to scroll the row into view first. A caller that polls the same row across frames
    /// (waiting for a just-scrolled row's geometry to settle) passes false after the first attempt:
    /// re-scrolling every attempt restarts the settle it is waiting for.
    /// </param>
    /// <summary>
    /// Realizes the cell and returns its dropdown stop, wherever it sits. A dropdown open must
    /// resolve by KIND, not by stored index: a split-button cell (the squad table's role cells)
    /// holds a name button plus the dropdown, and the panel profile hides the button during the
    /// walk — at action time that profile need not be the active one, so the button reappears
    /// and shifts every index (observed live as "not a dropdown" on a visible role selector).
    /// A cell holds at most one dropdown, so kind-directed resolution cannot pick a wrong one.
    /// Null when the cell has no dropdown stop (not dropdown-backed, or not re-bound yet).
    /// </summary>
    public DropdownAdapter RealizeCellDropdown(int dataIndex, int colIndex, bool scroll = true)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return null;
        if (scroll) ScrollTo(virtList, dataIndex);
        var row = FindRowByDataIndex(dataIndex);
        if (row == null) return null;
        var cell = NthColumnChild(row, colIndex);
        if (cell == null) return null;
        foreach (var stop in FindVisibleStops(cell))
            if (AsDropdown(stop) is { } dd)
                return dd;
        return null;
    }

    public IAccessibleElement RealizeCellElement(int dataIndex, int colIndex, int elementIndex, bool scroll = true)
    {
        var virtList = GetVirtualisedList();
        if (virtList == null) return null;

        if (scroll) ScrollTo(virtList, dataIndex);
        var row = FindRowByDataIndex(dataIndex);
        if (row == null) return null;

        var cell = NthColumnChild(row, colIndex);
        if (cell == null) return null;

        // Same visible-stop filter as the cell model builders: ElementIndex was allocated against
        // the filtered list, so resolving against the raw list would dispatch to a hidden stop.
        var stops = FindVisibleStops(cell);
        if (stops.Count == 0)
            return elementIndex == 0 ? TryCreateSpriteAdapter(cell) : null;
        if (elementIndex < 0 || elementIndex >= stops.Count) return null;
        return stops[elementIndex];
    }

    private static IAccessibleElement TryCreateSpriteAdapter(VisualElement cell)
    {
        var image = FindSpriteImage(cell, 0);
        if (image == null)
            return null;
        var label = SpriteHelper.GetSpriteLabel(image);
        if (label == null)
            return null;
        return new SpriteImageAdapter(image, label);
    }

    private static VisualElement FindSpriteImage(VisualElement element, int depth)
    {
        // A hidden icon is not content, whatever sprite it still carries: the Data Hub player
        // statistics hide the Time cell's incident icon for a player with no incident and leave the
        // last sprite on it, so reading through would report a substitution that never happened.
        if (depth > 10 || !ElementBridge.IsVisible(element)) return null;
        var typeName = element.GetIl2CppType().Name;
        if (typeName == "SIImage" || typeName == "SIStyleSetter")
            return element;
        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindSpriteImage(element.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static bool _initialized;

    private static void EnsureReflection()
    {
        if (_initialized) return;
        _initialized = true;

        var streamedTableType = typeof(SI.Bindable.StreamedTable);
        var streamedListViewType = streamedTableType.BaseType;

        _listElementProperty = streamedListViewType.GetProperty("ListElement",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_listElementProperty == null)
        {
            Speech.Log.Error("StreamedListView.ListElement property not found.");
            throw new System.MissingMemberException("StreamedListView", "ListElement");
        }

        _columnHeadersProperty = streamedTableType.GetProperty("m_columnHeaders",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_columnHeadersProperty == null)
        {
            Speech.Log.Error("StreamedTable.m_columnHeaders property not found.");
            throw new System.MissingMemberException("StreamedTable", "m_columnHeaders");
        }

        var virtListType = typeof(SI.UI.VirtualisedList);

        _scrollToMethod = virtListType.GetMethod("ScrollTo",
            BindingFlags.Public | BindingFlags.Instance,
            null, new System.Type[] { typeof(int) }, null);
        if (_scrollToMethod == null)
        {
            Speech.Log.Error("VirtualisedList.ScrollTo(int) method not found.");
            throw new System.MissingMethodException("VirtualisedList", "ScrollTo");
        }

        var virtElemType = typeof(SI.UI.VirtualisedList.VirtualisedElement);

        _virtualisedElementContentProperty = virtElemType.GetProperty("Content",
            BindingFlags.Public | BindingFlags.Instance);
        if (_virtualisedElementContentProperty == null)
        {
            Speech.Log.Error("VirtualisedElement.Content property not found.");
            throw new System.MissingMemberException("VirtualisedElement", "Content");
        }

        // The per-cell binding-completion signal: SIText.m_binder.IsReady flips true the frame a
        // cell's value is resolved and applied. A row is fully bound when no cell's binder is
        // not-ready (see CountPendingCells). Il2CppInterop exposes the private m_binder field and the
        // Binder.IsReady property as readable properties.
        _siTextBinderProperty = typeof(SI.Bindable.SIText).GetProperty("m_binder",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (_siTextBinderProperty == null)
        {
            Speech.Log.Error("SIText.m_binder property not found.");
            throw new System.MissingMemberException("SIText", "m_binder");
        }

        Speech.Log.Info("[Table] Reflection initialized successfully.");
    }
}

internal sealed class TableRow
{
    public int DataIndex;
    public string NativeIdentity;
    public List<TableCell> Cells = new();
    // The row's selection-checkbox name ("<entity>, <position>"), or null on rows with no name cell
    // so the HTML surface falls back to joining plain-text cells. Computed once at row-model build.
    public string SelectionLabel;
}

// A cell can hold more than one interactive element (e.g. a name button beside a role dropdown),
// matching what the cursor surface exposes via CellAdapter. Each element renders in document order
// inside the one <td>, so the HTML cell is as rich as the game's.
internal sealed class TableCell
{
    public List<TableCellElement> Elements = new();
    // The squad "Inf" column's status flags, read off the game's ExpandingList rather than walked:
    // it parents only its first badge into the tree and builds the rest in a hover popup. Null when
    // the cell holds no such widget (every other column). Unreadable means the widget was there but
    // its flags would not read, so the renderer must show a gap marker instead of a partial list.
    public List<Navigation.StatusFlagsHtml.StatusFlag> StatusFlags;
    public bool StatusFlagsUnreadable;
}

internal struct TableCellElement
{
    public string Text;
    // A changing plotted trend is cell data, never an entity's identifying
    // detail. Keep the inherited name/position checkbox label stable.
    public bool ExcludeFromSelectionLabel;
    public bool CanActivate;
    public bool IsDropdown;
    public string DropdownLabel;
    public string DropdownState;
    // The game has greyed this control out. Snapshotted here because the renderer only ever sees
    // this struct, never the live element — which is why in-cell dropdowns used to render openable
    // whatever the game said. Same signal ServerText.IsDisabled and DropdownSession.Open read.
    public bool IsDisabled;
    // A checkbox/toggle/radio control: render as a native <input> reflecting IsChecked, not a
    // button. IsRadio picks type=radio over type=checkbox. State is read live at model-build time.
    public bool IsToggle;
    public bool IsRadio;
    public bool IsChecked;
    // Overrides the renderer's per-table radio grouping. Set when a single choice spans several
    // tables (the progress report's one-attribute selection covers all four attribute groups), so
    // the browser and the screen reader treat them as the one set of options they are.
    public string RadioGroup;
    // The cell control carries a right-click context menu (a player-name link's actions menu). Snapshot
    // at build time; the companion renders it as aria-haspopup and the CellHandle re-realises to open it.
    public bool HasContextMenu;
    // This cell is a single-select disclosure row-selector (the staff-meeting advice pattern). Activation
    // selects the row by its data index through the list's own selection API, so the CellHandle drives it
    // index-natively (no ScrollTo re-realisation) — immune to virtualised worldBound staleness.
    public bool IsSelectionDisclosure;
    // The dropdown's accessible name was borrowed from its own displayed value (no real label
    // anywhere around it, no profile rename). The renderer relabels such a cell by its column
    // header and shows the borrowed value as the collapsed value.
    public bool IsValueNamedDropdown;
    // A two-option segmented control (the opposition grid's Position/Player column). Rendered as a
    // native two-option <select> rather than a button whose text flips: the select names both
    // choices and carries the current one as a real value, so the player hears which state it is in
    // instead of a bare word whose meaning depends on what they pressed last.
    public bool IsSegmented;
    public string[] SegmentOptions;
    public int SegmentIndex;
}

// A column header: its label, the live clickable sort button (null when the column has none), the
// game's current sort state (ColumnHeaderAdapter.GetState() form, "" when unsorted), and whether the
// column carries the game's per-column options menu (insert / remove / replace column, view
// management). Headers are not virtualised, so the button reference is safe to hold from the walk to
// the render.
internal struct TableHeader
{
    public string Text;
    public UnityEngine.UIElements.VisualElement HeaderButton;
    public string SortState;
    public bool HasContextMenu;
}

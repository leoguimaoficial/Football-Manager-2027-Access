using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Declarative synthesized tables. Many panels draw label/value runs, stat rows, or visual grids
/// that the walker can only flatten into loose text stops — read sequentially the labels and
/// values lose each other. Each Spec here names one such container on one panel and how to read
/// its rows; the walk consolidates the whole container into a single semantic table in place.
/// HTML-only, mirroring the chart and match-stats consolidations: the speech surface keeps the
/// raw stop order.
///
/// Extraction is live per render (BuildBody re-walks the panel), never cached. Decline in place:
/// a container whose subtree holds a control the extraction cannot carry into cells (any
/// interactive stop, unless the spec opts links in as plain text) or whose rows do not hold the
/// expected shape keeps its raw stops, so no table ever silently swallows a control or value.
/// </summary>
internal static class SyntheticTables
{
    internal enum RowMode
    {
        Children,   // rows = the container's visible direct children (minus SkipRows)
        RowNames,   // rows = visible descendants whose element name is in RowNames
        RowType,    // rows = visible descendants whose Il2Cpp type name is RowTypeName
        FlatPairs,  // no row elements: all cells under the container in order, chunked by Columns
        ValueAnchors, // rows found by their named VALUE element, ascending AnchorAscend levels to the row
    }

    internal sealed class Spec
    {
        public string Panel;            // PanelID name the container must sit inside
        public string Container;        // element name that anchors the table (null when a probe is used)
        public string ScopeAncestor;    // narrows Container/probe to one region of the panel: an
                                        // element name that must appear among the container's near
                                        // ancestors. For a template whose own frames are named
                                        // generically ("Frame") but whose section is not — without
                                        // it a spec broad enough to catch the section catches every
                                        // same-shaped frame on every other tab too.
        public int ScopeAncestorDepth = 3; // how far up ScopeAncestor may sit
        public string ProbeChild;       // match an unnamed container by one distinctive direct child
        public string ProbeDescendant;  // match an unnamed container by a distinctive descendant within two levels
        public string[] ProbeChildTypes; // match an unnamed container by its exact direct-child type multiset
        public RowMode Mode = RowMode.Children;
        public string[] RowNames;
        public string RowNamePrefix;    // RowNames/ValueAnchors alternative: rows whose name starts with this
        public string RowTypeName;
        public int AnchorAscend;        // ValueAnchors: levels from the value element up to its row
        public string NameIndexLabel;   // prefix a per-row label from the row element's trailing -N index
        public string[] IndexBindingValues; // IndexNames companion: for a cell the game never renders,
                                        // the binding property holding its real value, by the same
                                        // draw-order index (null where the drawn text is fine).
                                        // Applied only under the same exact-grid guard as IndexNames,
                                        // and only when the binding reads a positive value the cell
                                        // is not already showing — a cell the game DOES render always
                                        // keeps its rendered text.
        public string[] IndexNames;     // NameIndexLabel refinement: the real name of each position in
                                        // a fixed positional grid, indexed by the row's trailing -N.
                                        // Used where the game draws a named place but writes no text
                                        // for it (the shot map's goal areas). Applied ONLY when the
                                        // extracted row count matches this array's length exactly —
                                        // a template that gained or lost a cell falls back to the
                                        // positional NameIndexLabel rather than shifting every name
                                        // onto the wrong place.
        public string OrdinalIndexLabel; // prefix a per-row label from the row's 1-based position in the
                                        // extracted set, for a positional grid whose rows carry no index
                                        // of their own (a pitch zone map)
        public Func<VisualElement, string> RowLabelFrom; // OrdinalIndexLabel refinement: resolve each
                                        // row's label from the row element itself (a zone id read from
                                        // its binding) instead of its position. All-or-nothing — one
                                        // row this cannot name leaves the WHOLE table on positional
                                        // labels, so a renumbered template degrades to honest
                                        // numbering rather than a half-named grid.
        public int[] CellOrder;         // reorder collected cells into column order (missing -> empty)
        public bool TitleFromFirstCell; // FlatPairs: a single leading orphan cell is the block's own
                                        // drawn title (an UNNAMED title text HeadingChild cannot
                                        // reach) — promote it to the table heading instead of
                                        // declining the uneven run. Letters required; a leading
                                        // orphan figure still declines.
        public bool FirstRowHeaders;    // the first extracted row is the game's own column headers
                                        // (drawn as a header row HeaderChild cannot reach by name).
                                        // All-or-nothing: a first row that does not fill the columns
                                        // declines the table, because falling back would put data
                                        // under headers belonging to a different template.
        public string LeadRowLabel;     // FlatPairs: label for a single leading orphan cell's own row
        public string[] SkipRows;       // direct-child names Children mode ignores
        public string HeadingChild;     // descendant whose text becomes an <h3> above the table
        public string Heading;          // mod-authored title, used only where the game draws none of
                                        // its own (HeadingChild wins whenever it resolves). For a
                                        // block the game leaves untitled, so the table is not an
                                        // unannounced slab of numbers mid-panel.
        public string[] CellNames;      // per-row named sub-elements to read as cells, in column order
        public string HeaderChild;      // descendant whose texts become the column headers (falls back to Headers)
        public string[] Headers;        // column headers (mod chrome, concise)
        public int Columns;             // cells per row; extra cells join into the last column
        public string OverflowSeparator = " "; // how those extra cells join. A space suits a row whose
                                        // overflow is one broken-up phrase (the default, and what
                                        // every spec predating this flag relies on); ", " suits one
                                        // whose overflow is a LIST of distinct values, where a space
                                        // runs them into a single unreadable phrase ("Stamina Natural
                                        // Fitness Work Rate").
        public bool SwapFirstTwo;       // layout reads value before label; present label first
        public bool SwapNumericLeadPairs; // SwapFirstTwo's per-row form, for a block that mixes
                                        // label-then-value rows with radial-gauge tiles, which draw
                                        // the figure inside the ring and its caption underneath and
                                        // so extract value-then-label. Swaps ONLY a two-cell row
                                        // whose first cell is a bare figure and whose second carries
                                        // letters, so a row the game labelled in words is never
                                        // touched. Without it a gauge tile reads "746 | 95% Passes
                                        // Completed" — the figure sitting in the stat-name column,
                                        // claiming to name a statistic.
        public bool IncludeLinkText;    // carry SIButton link text as a plain cell instead of declining
        public bool IncludeProgress;    // read SIProgressBar fills as percentage cells
        public bool IncludeGauges;      // read a native radial meter beside its factor label
        public int MinRows = 2;         // fewer real rows than this declines (no pointless tables)
        public int HeadingLevel = 3;    // level for the table's own title; raise where the table nests
                                        // under a section heading the profile already emits
        public Func<VisualElement, List<List<string>>, string[], string[]> WidenColumns; // columns
                                        // the drawn cells do not carry, read from the container
                                        // itself: given the container, the extracted rows and the
                                        // headers, appends to every row and returns the widened
                                        // headers, or returns null and leaves the rows untouched.
                                        // All-or-nothing, so a failed read keeps the table as drawn.
    }

    /// <summary>
    /// The shot map's thirteen goal areas, in the order the pitch graphic lays them out
    /// (<c>data_display-graph-shotmap-data-N</c>): a penalty-area band of five columns left to
    /// right, a band behind it, then everything beyond. The game draws these as unlabelled cells,
    /// but binds each to a named location property, and its localisation table gives each an
    /// English name — these are those names, in that order.
    ///
    /// Derived by matching every cell's bound value against the thirteen named location values, and
    /// verified against four independent data sets (the Opposition Report's and the Data Hub's
    /// scoring and conceding maps): 52 of 52 cells agreed. Note the cell order within a column is
    /// NOT uniform — the centre column lists the six-yard box before the penalty spot while the
    /// side columns list the deeper cell first — so this table is the mapping, never a near/far
    /// rule of thumb. See docs and PitchNavigation for the same "name a place the game only draws"
    /// precedent.
    /// </summary>
    private static readonly string[] GoalAreaNames =
    {
        "Outside Area Wide Left Corner",   // LocationOutsideBoxLeftCornerFlag
        "Penalty Area Left Corner",        // LocationPenaltyBoxLeftCorner
        "Penalty Area Left Close",         // LocationPenaltyBoxLeftClose
        "Six Yard Box",                    // LocationSixYardBox
        "Penalty Area Centre",             // LocationPenaltyBoxNearSpot
        "Penalty Area Right Corner",       // LocationPenaltyBoxRightCorner
        "Penalty Area Right Close",        // LocationPenaltyBoxRightClose
        "Outside Area Wide Right Corner",  // LocationOutsideBoxRightCornerFlag
        "Outside Area Wide Left",          // LocationOutsideBoxWideLeft
        "Outside Area Left Central",       // LocationOutsideBoxLeftCentral
        "Outside Area Right Central",      // LocationOutsideBoxRightCentral
        "Outside Area Wide Right",         // LocationOutsideBoxWideRight
        "Long-Range",                      // LocationOutsideBoxFarFromGoal
    };

    /// <summary>
    /// The one shot-map cell the game never renders, and the binding property holding its real value.
    /// Long-Range always draws the "no goals" dash — its <c>SIVisible_Data</c> branch stays empty even
    /// when the count is not zero — so reading the cell would report "none" for goals that happened.
    /// See <see cref="ShotMapValues"/> for how this was proven. Every other cell is null: the game
    /// renders those, and their drawn text is what the player gets.
    /// </summary>
    private static readonly string[] GoalAreaBindingValues =
    {
        null, null, null, null, null, null, null, null, null, null, null, null,
        "LocationOutsideBoxFarFromGoal",
    };

    /// <summary>
    /// The per-player Season Stats table's columns on the Player Comparison card. Shared with
    /// <see cref="ScreenWalker"/>, which folds the card's two of these into one Player 1 / Player 2
    /// table and needs to recognise them by the columns this spec gave them.
    /// </summary>
    internal static readonly string[] ComparisonSeasonStatsHeaders = { "Stat", "Value" };

    /// <summary>
    /// The Overview tab's two attribute blocks: the bio strip (drawn a player per ROW, because that
    /// is how the halves lay it out) and the radar's stat table (drawn a player per COLUMN).
    /// <see cref="ScreenWalker"/> folds them into one, transposing the first; shared so the specs
    /// and the merge cannot drift apart.
    /// </summary>
    internal static readonly string[] ComparisonOverviewBioHeaders = { "Player", "Preferred Foot", "Height" };
    internal static readonly string[] ComparisonAttributeHeaders = { "Attribute", "Player 1", "Player 2" };

    // Declared after GoalAreaNames on purpose: static initialisers run in declaration order and
    // BuildSpecs() reads that array, so moving this above it hands every spec a null name table.
    private static readonly Spec[] Specs = BuildSpecs();

    // One note per spec whose IndexNames no longer fit its grid, so a changed game template stays
    // diagnosable without flooding the log on every render.
    private static readonly HashSet<string> _indexNameMisses = new(StringComparer.Ordinal);

    // One note per spec+property the first time a binding value stands in for an unrendered cell, so
    // the divergence from the drawn screen is always visible in the log.
    private static readonly HashSet<string> _bindingValueNotes = new(StringComparer.Ordinal);

    // One note per spec the first time a row-label resolver comes up empty and the table falls back
    // to positional numbering.
    private static readonly HashSet<string> _rowLabelMisses = new(StringComparer.Ordinal);


    // Container names get a fast pre-check so the per-element cost in WalkRec stays one hash probe.
    private static readonly HashSet<string> ContainerNames = BuildContainerNames();
    private static HashSet<string> BuildContainerNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Specs)
            if (s.Container != null) set.Add(s.Container);
        return set;
    }

    /// <summary>
    /// Consolidate el into a synthetic table when a spec matches and its shape holds. True means an
    /// item was added and the caller must not recurse into el; false leaves the raw walk untouched.
    /// </summary>
    internal static bool TryBuild(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        // Full boundary: even the match phase touches interop (names, children, panel ancestry) on
        // every walked element, so any throw degrades to the raw walk rather than tearing the body
        // build down.
        try { return TryBuildCore(el, items); }
        catch (Exception ex)
        {
            Log.Warn($"[SyntheticTables] match phase failed, raw stops kept: {ex.Message}");
            return false;
        }
    }

    private static bool TryBuildCore(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        string name = el.name;
        bool named = !string.IsNullOrEmpty(name) && ContainerNames.Contains(name);
        // Every probe spec needs children (a direct child, a child-or-grandchild, or an exact
        // child-type sequence), so a childless element can only match by container name. The walk
        // is mostly leaves, and this one childCount read is their whole matching cost — without it
        // every text leaf of every panel pays the full probe scan below.
        int childCount = el.hierarchy.childCount;
        if (!named && childCount == 0) return false;

        foreach (var spec in Specs)
        {
            if (spec.Container != null)
            {
                if (!named || !string.Equals(spec.Container, name, StringComparison.Ordinal)) continue;
            }
            else if (childCount == 0)
            {
                continue; // a named leaf: probe specs cannot match it
            }
            else if (spec.ProbeChild != null)
            {
                if (!HasDirectChildNamed(el, spec.ProbeChild)) continue;
            }
            else if (spec.ProbeDescendant != null)
            {
                if (FindDescendantNamed(el, spec.ProbeDescendant, 1) == null) continue;
            }
            else
            {
                if (childCount != spec.ProbeChildTypes.Length || !HasExactChildTypes(el, spec.ProbeChildTypes)) continue;
            }
            if (spec.ScopeAncestor != null && !HasAncestorNamed(el, spec.ScopeAncestor, spec.ScopeAncestorDepth)) continue;
            if (spec.Panel != null && !ElementBridge.IsInPanel(el, spec.Panel)) continue;

            List<List<string>> rows;
            string heading = null;
            try
            {
                var headingEl = spec.HeadingChild != null ? FindDescendantNamed(el, spec.HeadingChild, 6) : null;
                if (headingEl != null)
                    heading = CleanOrNull(ScreenWalker.ReadFirstText(headingEl));
                // Pointer identity, never wrapper reference: IL2CPP hands out fresh wrappers.
                rows = Extract(el, spec, headingEl != null ? headingEl.Pointer : IntPtr.Zero, ref heading);
                // Game text first, always (HeadingChild, or a TitleFromFirstCell promotion inside
                // Extract): the mod-authored title only stands in where the game names the block
                // nothing at all.
                heading ??= spec.Heading;
            }
            catch (Exception ex)
            {
                Log.Warn($"[SyntheticTables] {spec.Panel}/{spec.Container ?? spec.ProbeChild}: extraction failed, raw stops kept: {ex.Message}");
                return false;
            }
            if (rows == null || rows.Count < spec.MinRows) return false;
            if (spec.IncludeGauges && rows.Exists(row => row.Count != spec.Columns)) return false;

            var headers = ReadHeaders(el, spec);
            if (spec.FirstRowHeaders)
            {
                // The game's own header row becomes the columns, or the whole table declines: data
                // under a fallback header set from a different template would mislabel every column.
                if (rows[0].Count != spec.Columns) return false;
                headers = rows[0].ToArray();
                rows.RemoveAt(0);
                if (rows.Count < spec.MinRows) return false;
            }

            // A multi-column spec whose rows every one yield a single cell has matched a list that
            // is not a table. Rendering it anyway pairs the game's data with headers it never had
            // and an empty column beside them — the Data Hub's set-piece observations came out as
            // "Type | Goals" over four prose sentences and a blank count. Decline and let a
            // better-scoped spec, or the raw stops, have it.
            if (spec.Columns > 1 && !rows.Exists(r => r.Count > 1)) continue;

            if (spec.WidenColumns != null)
            {
                try { headers = spec.WidenColumns(el, rows, headers) ?? headers; }
                catch (Exception ex)
                {
                    Degrade.Note($"[SyntheticTables] {spec.Panel}/{spec.Container}: extra columns failed, table kept as drawn: {ex.Message}");
                }
            }

            if (heading != null)
                items.Add(new ScreenWalker.HeadingItem { Text = heading, Level = spec.HeadingLevel });
            // A container built over a virtualised list only ever holds the realised window; the
            // walk would normally emit the paging chrome when it reached the list, and swallowing
            // the container swallows that too — leaving a table that reads as the whole list when
            // it is twelve rows of sixteen. Emit the chrome above the table instead. A fully
            // realised list (every other spec here) emits nothing, so nothing changes for them.
            var lists = new List<VisualElement>();
            CollectDescendantsTyped(el, "StreamedObjectList", lists, 8);
            if (lists.Count == 1)
                ListWindowSurface.AppendChrome(items, lists[0], ListWindowSurface.Labels.Generic);
            items.Add(new ScreenWalker.SyntheticTableItem { Headers = headers, Rows = rows });
            return true;
        }
        return false;
    }

    // Column headers: the game's own header texts when the spec names a header element and they
    // line up with the expected column count; the spec's mod-chrome headers otherwise.
    private static string[] ReadHeaders(VisualElement container, Spec spec)
    {
        if (spec.HeaderChild == null) return spec.Headers;
        var el = FindDescendantNamed(container, spec.HeaderChild, 6);
        if (el == null) return spec.Headers;
        bool blocked = false;
        var texts = new List<string>();
        CollectCells(el, texts, spec, ref blocked, IntPtr.Zero);
        return (!blocked && texts.Count == spec.Columns) ? texts.ToArray() : spec.Headers;
    }

    private static List<List<string>> Extract(VisualElement container, Spec spec, IntPtr avoidPtr, ref string heading)
    {
        if (spec.Mode == RowMode.FlatPairs) return ExtractFlatPairs(container, spec, avoidPtr, ref heading);

        var rowEls = new List<VisualElement>();
        switch (spec.Mode)
        {
            case RowMode.Children:
                int n = container.hierarchy.childCount;
                for (int i = 0; i < n; i++)
                {
                    var c = container.hierarchy[i];
                    if (!ElementBridge.IsVisible(c)) continue;
                    if (IsSkipped(c.name, spec)) continue;
                    rowEls.Add(c);
                }
                break;
            case RowMode.RowNames:
                if (spec.RowNames != null)
                    foreach (var rn in spec.RowNames)
                        CollectDescendantsNamed(container, rn, rowEls, 20);
                if (spec.RowNamePrefix != null)
                    CollectDescendantsPrefixed(container, spec.RowNamePrefix, rowEls, 20);
                break;
            case RowMode.RowType:
                CollectDescendantsTyped(container, spec.RowTypeName, rowEls, 8);
                break;
            case RowMode.ValueAnchors:
            {
                var anchors = new List<VisualElement>();
                if (spec.RowNames != null)
                    foreach (var rn in spec.RowNames)
                        CollectDescendantsNamed(container, rn, anchors, 12);
                if (spec.RowNamePrefix != null)
                    CollectDescendantsPrefixed(container, spec.RowNamePrefix, anchors, 12);
                foreach (var a in anchors)
                {
                    var row = a;
                    for (int i = 0; i < spec.AnchorAscend && row != null; i++) row = row.hierarchy.parent;
                    if (row != null) rowEls.Add(row);
                }
                break;
            }
        }

        // Names are trusted only for the exact grid they were mapped against; any other size means
        // this is not that template and the positional label is the honest read.
        bool useIndexNames = spec.IndexNames != null && rowEls.Count == spec.IndexNames.Length;
        if (spec.IndexNames != null && !useIndexNames && _indexNameMisses.Add(spec.Panel + "|" + spec.Container))
            Log.Debug($"[SyntheticTables] {spec.Panel}/{spec.Container}: {rowEls.Count} cells but {spec.IndexNames.Length} names, positional labels kept");

        // Per-row labels read from the rows themselves, resolved up front so the table is named
        // all-or-nothing (see Spec.RowLabelFrom).
        string[] rowLabels = ResolveRowLabels(rowEls, spec);

        var rows = new List<List<string>>();
        for (int rowPos = 0; rowPos < rowEls.Count; rowPos++)
        {
            var rowEl = rowEls[rowPos];
            bool blocked = false;
            var cells = new List<string>();
            int rowIndex = -1;
            if (spec.NameIndexLabel != null)
            {
                rowIndex = ParseTrailingIndex(rowEl.name);
                cells.Add(useIndexNames && rowIndex >= 0 && rowIndex < spec.IndexNames.Length
                    ? spec.IndexNames[rowIndex]
                    : $"{spec.NameIndexLabel} {rowIndex + 1}");
            }
            // Where the label cells end and the read cells begin, so a binding-sourced value can
            // replace the first read cell without counting on a fixed column offset.
            int firstReadCell = cells.Count;
            if (spec.OrdinalIndexLabel != null)
                cells.Add(rowLabels != null ? rowLabels[rowPos] : $"{spec.OrdinalIndexLabel} {rows.Count + 1}");
            if (spec.CellNames != null)
            {
                // Named-cell rows keep column alignment even when a middle cell is empty.
                bool any = false;
                foreach (var cn in spec.CellNames)
                {
                    var cellEl = string.Equals(rowEl.name, cn, StringComparison.Ordinal)
                        ? rowEl : FindDescendantNamed(rowEl, cn, 6);
                    var parts = new List<string>();
                    if (cellEl != null) CollectCells(cellEl, parts, spec, ref blocked, IntPtr.Zero);
                    if (blocked) return null;
                    if (parts.Count > 0) any = true;
                    cells.Add(string.Join(" ", parts));
                }
                if (!any) continue;
            }
            else
            {
                CollectCells(rowEl, cells, spec, ref blocked, avoidPtr);
                if (blocked) return null;
                if (cells.Count == 0) continue;
            }
            ApplyBindingValue(cells, spec, container, rowIndex, firstReadCell, useIndexNames);
            ShapeRow(cells, spec);
            rows.Add(cells);
        }
        return rows;
    }

    // FM27 reuses the goal-area layout for shots with xG. Recognise the actual
    // paired native values, rather than calling attempts goals or joining two
    // statistics under one header. Goal-only layouts retain the FM26 reader.
    private static string[] ShotMapColumns(VisualElement container, List<List<string>> rows, string[] headers)
    {
        var zones = new List<VisualElement>();
        CollectDescendantsPrefixed(container, "data_display-graph-shotmap-data-", zones, 12);
        if (zones.Count != rows.Count) return null;
        var values = new List<List<string>>();
        bool hasXg = false;
        var spec = new Spec();
        foreach (var zone in zones)
        {
            var shots = new List<string>();
            var xg = new List<string>();
            bool blocked = false;
            var shotsElement = FindDescendantNamed(zone, "SubjectStatValue", 12);
            var xgElement = FindDescendantNamed(zone, "XgValue", 12);
            if (shotsElement != null) CollectCells(shotsElement, shots, spec, ref blocked, IntPtr.Zero);
            if (xgElement != null) CollectCells(xgElement, xg, spec, ref blocked, IntPtr.Zero);
            if (blocked || (shots.Count == 0) != (xg.Count == 0)) return null;
            hasXg |= xg.Count > 0;
            if (shots.Count == 0)
                CollectCells(zone, shots, spec, ref blocked, IntPtr.Zero);
            if (blocked) return null;
            values.Add(new List<string> { string.Join(" ", shots), string.Join(" ", xg) });
        }
        if (!hasXg) return null;
        for (int i = 0; i < rows.Count; i++)
        {
            string area = rows[i][0];
            rows[i].Clear();
            rows[i].Add(area);
            rows[i].AddRange(values[i]);
        }
        return new[] { headers[0], "Shots", "xG" };
    }

    // Each row's own label, or null when the spec asks for none or any single row cannot be named.
    // All-or-nothing on purpose: a table where two zones are named and the third reads "Zone 3" is
    // worse than one that numbers all three, and a template the mapping no longer fits must say so
    // by falling back rather than by naming most of it.
    private static string[] ResolveRowLabels(List<VisualElement> rowEls, Spec spec)
    {
        if (spec.RowLabelFrom == null || rowEls.Count == 0) return null;
        var labels = new string[rowEls.Count];
        for (int i = 0; i < rowEls.Count; i++)
        {
            labels[i] = spec.RowLabelFrom(rowEls[i]);
            if (labels[i] != null) continue;
            if (_rowLabelMisses.Add(spec.Panel + "|" + spec.Container))
                Log.Debug($"[SyntheticTables] {spec.Panel}: row {i} could not be named, positional labels kept");
            return null;
        }
        return labels;
    }

    // Replace a cell's drawn text with the value the game holds for it, for the cells a spec declares
    // the game never renders. Conservative on every axis: only under the exact-grid guard, only where
    // the spec names a property for this index, only when the binding reads a positive number, and
    // only when the cell is not already showing that number. Anything else leaves the drawn text
    // alone — the mod diverges from the screen only where staying faithful would report "none" for a
    // value the game itself holds.
    private static void ApplyBindingValue(
        List<string> cells, Spec spec, VisualElement container, int rowIndex, int firstReadCell, bool useIndexNames)
    {
        if (!useIndexNames || spec.IndexBindingValues == null) return;
        if (rowIndex < 0 || rowIndex >= spec.IndexBindingValues.Length) return;
        if (firstReadCell >= cells.Count) return;
        var property = spec.IndexBindingValues[rowIndex];
        if (property == null) return;

        if (!ShotMapValues.TryReadLocation(container, property, out int value) || value <= 0) return;
        var text = value.ToString();
        var drawn = cells[firstReadCell];
        if (drawn != null && drawn.StartsWith(text, StringComparison.Ordinal)) return;

        cells[firstReadCell] = text;
        if (_bindingValueNotes.Add(spec.Panel + "|" + property))
            Log.Debug($"[SyntheticTables] {spec.Panel}/{property}: cell drew \"{drawn}\", binding holds {value}; binding used");
    }

    // All cells under the container in document order, chunked into rows of Columns. For plain
    // label/value runs with no per-row element. A partial trailing chunk declines the whole table —
    // it means the run is not the shape the spec assumed.
    private static List<List<string>> ExtractFlatPairs(VisualElement container, Spec spec, IntPtr avoidPtr, ref string heading)
    {
        bool blocked = false;
        var cells = new List<string>();
        CollectCells(container, cells, spec, ref blocked, avoidPtr);
        if (blocked || cells.Count == 0) return null;

        // A single leading orphan with letters is the block's own drawn (unnamed) title; a spec that
        // declares so promotes it to the heading rather than declining the uneven run. Never
        // overrides a title HeadingChild already resolved — game structure beats inference.
        if (spec.TitleFromFirstCell && heading == null
            && cells.Count % spec.Columns == 1 && HasLetter(cells[0]))
        {
            heading = cells[0];
            cells.RemoveAt(0);
        }

        var rows = new List<List<string>>();

        // A single leading orphan cell (the head-coach cell before the label/value pairs) becomes
        // its own labeled row when the spec names it; without a label an uneven run declines.
        if (cells.Count % spec.Columns != 0)
        {
            if (spec.LeadRowLabel == null || cells.Count % spec.Columns != 1) return null;
            rows.Add(new List<string> { spec.LeadRowLabel, cells[0] });
            cells.RemoveAt(0);
        }

        for (int i = 0; i < cells.Count; i += spec.Columns)
        {
            var row = cells.GetRange(i, spec.Columns);
            ShapeRow(row, spec);
            rows.Add(row);
        }
        return rows;
    }


    private static void ShapeRow(List<string> cells, Spec spec)
    {
        if (spec.CellOrder != null)
        {
            var reordered = new List<string>(spec.CellOrder.Length);
            foreach (var idx in spec.CellOrder)
                reordered.Add(idx >= 0 && idx < cells.Count ? cells[idx] : "");
            cells.Clear();
            cells.AddRange(reordered);
            return;
        }
        if (spec.SwapFirstTwo && cells.Count >= 2)
            (cells[0], cells[1]) = (cells[1], cells[0]);
        if (spec.SwapNumericLeadPairs && cells.Count == 2 && IsBareFigure(cells[0]) && HasLetter(cells[1]))
            (cells[0], cells[1]) = (cells[1], cells[0]);
        if (spec.Columns > 0 && cells.Count > spec.Columns)
        {
            var joined = string.Join(spec.OverflowSeparator, cells.GetRange(spec.Columns - 1, cells.Count - spec.Columns + 1));
            cells.RemoveRange(spec.Columns - 1, cells.Count - spec.Columns + 1);
            cells.Add(joined);
        }
    }

    // A cell that is only a figure — digits and the punctuation figures are written with ("0 / 1",
    // "93.60", "95%", "-0.41"). Deliberately strict about letters: "0.5 km" and "0 Saved" are
    // figures a caption could sit beside, and treating them as bare would swap a row whose first
    // cell really is what the game wrote first.
    private static bool IsBareFigure(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        bool digit = false;
        foreach (var c in s)
        {
            if (char.IsDigit(c)) { digit = true; continue; }
            if (c is '.' or ',' or '%' or '/' or '-' or '+' or '(' or ')' or ' ') continue;
            return false;
        }
        return digit;
    }

    private static bool HasLetter(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s) if (char.IsLetter(c)) return true;
        return false;
    }

    // Depth-first cell collection. Text, star ratings, and progress bars become cells; any other
    // interactive element marks the extraction blocked (the caller declines) so no control is ever
    // silently swallowed into a read-only cell. Links are carried as plain text only when the spec
    // opts in.
    private static void CollectCells(VisualElement el, List<string> cells, Spec spec, ref bool blocked, IntPtr avoidPtr)
    {
        if (blocked) return;
        if (!ElementBridge.IsVisible(el)) return;
        if (avoidPtr != IntPtr.Zero && el.Pointer == avoidPtr) return;

        string tn = el.GetIl2CppType().Name;
        switch (tn)
        {
            case "SIText":
            {
                var t = CleanOrNull(ScreenWalker.ReadFirstText(el));
                if (t != null) cells.Add(t);
                return;
            }
            // A few templates drop a plain UI Toolkit Label where their siblings use SIText — the
            // comparison card's "Av R" writes its dash that way. Without this the row extracts its
            // label and no value, and a missing rating reads as a missing cell rather than as the
            // dash the game drew.
            case "Label":
            {
                var t = CleanOrNull(el.Cast<UnityEngine.UIElements.TextElement>().text);
                if (t != null) cells.Add(t);
                return;
            }
            case "FMStarRatingTemp":
                if (Adapters.StarRatingHelper.TryReadStarLabel(el, out var stars)) cells.Add(stars);
                return;
            case "SIProgressBar":
                if (spec.IncludeProgress) cells.Add(ReadProgressPercent(el));
                return;
            case "SIRadialGauge":
                if (spec.IncludeGauges) cells.Add(new Adapters.RadialGaugeAdapter(el).GetState());
                return;
            case "SIButton":
                if (spec.IncludeLinkText)
                {
                    var t = CleanOrNull(ScreenWalker.ReadFirstText(el));
                    if (t != null) cells.Add(t);
                    return;
                }
                blocked = true;
                return;
            case "SIDropdown":
            case "SIMenu":
            case "SICheckBox":
            case "SIRadioButton":
            case "SISlider":
            case "SISliderInt":
            case "Toggle":
            case "Button":
                blocked = true;
                return;
        }

        int n = el.hierarchy.childCount;
        for (int i = 0; i < n && !blocked; i++)
            CollectCells(el.hierarchy[i], cells, spec, ref blocked, avoidPtr);
    }

    // The bar's fill as a percentage — the same width read ProgressBarAdapter announces, so the
    // table cell and the speech surface agree.
    private static string ReadProgressPercent(VisualElement el)
    {
        var progressBar = el.Cast<SI.Bindable.SIProgressBar>();
        var bar = progressBar.ProgressBar;
        float barWidth = bar.resolvedStyle.width;
        float parentWidth = el.resolvedStyle.width;
        if (parentWidth > 0) return $"{(int)(barWidth / parentWidth * 100)}%";
        return "0%";
    }

    internal static string CleanOrNull(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(raw));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static bool IsSkipped(string name, Spec spec)
    {
        if (spec.SkipRows == null || string.IsNullOrEmpty(name)) return false;
        foreach (var s in spec.SkipRows)
            if (string.Equals(s, name, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int ParseTrailingIndex(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        int dash = name.LastIndexOf('-');
        if (dash < 0 || dash == name.Length - 1) return -1;
        return int.TryParse(name.Substring(dash + 1), out int idx) ? idx : -1;
    }

    private static bool HasExactChildTypes(VisualElement el, string[] types)
    {
        int n = el.hierarchy.childCount;
        if (n != types.Length) return false;
        // Order-sensitive: the templates draw these children in a fixed order.
        for (int i = 0; i < n; i++)
            if (!string.Equals(el.hierarchy[i].GetIl2CppType().Name, types[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    private static void CollectDescendantsPrefixed(VisualElement root, string prefix, List<VisualElement> into, int depth)
    {
        if (depth < 0) return;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            var cn = c.name;
            if (!string.IsNullOrEmpty(cn) && cn.StartsWith(prefix, StringComparison.Ordinal)) { into.Add(c); continue; }
            CollectDescendantsPrefixed(c, prefix, into, depth - 1);
        }
    }

    private static bool HasAncestorNamed(VisualElement el, string name, int depth)
    {
        var current = el.hierarchy.parent;
        for (int i = 0; i < depth && current != null; i++)
        {
            if (string.Equals(current.name, name, StringComparison.Ordinal)) return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    private static bool HasDirectChildNamed(VisualElement el, string name)
    {
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (string.Equals(el.hierarchy[i].name, name, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static VisualElement FindDescendantNamed(VisualElement root, string name, int depth)
    {
        if (depth < 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (string.Equals(c.name, name, StringComparison.Ordinal)) return c;
            var found = FindDescendantNamed(c, name, depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectDescendantsNamed(VisualElement root, string name, List<VisualElement> into, int depth)
    {
        if (depth < 0) return;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (string.Equals(c.name, name, StringComparison.Ordinal)) { into.Add(c); continue; }
            CollectDescendantsNamed(c, name, into, depth - 1);
        }
    }

    private static void CollectDescendantsTyped(VisualElement root, string typeName, List<VisualElement> into, int depth)
    {
        if (depth < 0) return;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (string.Equals(c.GetIl2CppType().Name, typeName, StringComparison.Ordinal)) { into.Add(c); continue; }
            CollectDescendantsTyped(c, typeName, into, depth - 1);
        }
    }

    // A pitch row's position slot: the FMPitchIndicator ancestor's template name (GK, DR, DCL,
    // STC...) — the formation slot the game itself placed the player in. Null (falls back to
    // positional numbering, all-or-nothing) when the ancestry is not the mapped shape.
    private static string PitchSlotName(VisualElement row)
    {
        var cur = row;
        for (int i = 0; i < 6 && cur != null; i++)
        {
            if (string.Equals(cur.GetIl2CppType().Name, "FMPitchIndicator", StringComparison.Ordinal))
                return string.IsNullOrEmpty(cur.name) ? null : cur.name;
            cur = cur.hierarchy.parent;
        }
        return null;
    }

    private static Spec[] BuildSpecs() => new[]
    {
        // The native match viewer keeps club links outside each Data-Frame;
        // consolidate only its aligned stat label/value rows, preserving those
        // links and the separate viewer controls/map.
        new Spec
        {
            Panel="OverviewPrototype",Container="Data-Frame",ScopeAncestor="homestats",
            Headers=new[]{"Stat","Value"},Columns=2,MinRows=1,
        },
        new Spec
        {
            Panel="OverviewPrototype",Container="Data-Frame",ScopeAncestor="awaystats",
            Headers=new[]{"Stat","Value"},Columns=2,MinRows=1,
        },
        // FM27's tactical-effectiveness breakdown uses a common label/meter
        // factor row. Reuse the original semantic table builder, keeping every
        // plotted value and declining a changed/range shape as a whole.
        new Spec
        {
            Panel = "TacticalEffectivenessCard", Container = "View",
            Mode = RowMode.RowNames, RowNames = new[] { "HasFactorType" },
            Headers = new[] { "Factor", "Value" }, Columns = 2,
            IncludeGauges = true, MinRows = 1,
        },
        // Mentoring, the Social Groups tile: one row per social group (Core, Secondary A,
        // Secondary B, Others) drawn as an icon, the group name and its player count. The tile is
        // one whole-tile button, so ScreenWalker descends into it for these two specs; the tile's
        // own title is promoted to the heading above the table, hence no HeadingChild here.
        new Spec
        {
            Panel = "MentoringCard", Container = "SquadSocialGroupTileContent",
            Mode = RowMode.FlatPairs,
            Headers = new[] { "Group", "Players" }, Columns = 2,
        },

        // Mentoring, the Hierarchy tile: the same shape for the squad's influence ranks (Leaders,
        // Highly Influential, Influential, Others).
        new Spec
        {
            Panel = "MentoringCard", Container = "tile states-hierarchy-medium",
            Mode = RowMode.FlatPairs,
            Headers = new[] { "Influence", "Players" }, Columns = 2,
        },

        // Supporter Profile fan-type percentages: six type frames each drawing the share above the
        // type name; swapped so the type reads first.
        new Spec
        {
            Panel = "ClubSupportersCard", Container = "percentage",
            Headers = new[] { "Fan type", "Share" }, Columns = 2, SwapFirstTwo = true,
        },

        // Club Profile details: one *-frame row per detail, label in the left frame, value in the
        // right.
        new Spec
        {
            Panel = "ClubProfileCard", Container = "details frame",
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },

        // Staff biography: named field rows (Name/Age/Nationality/Media/Reputation) under an
        // unnamed parent, matched by the distinctive Media child. Reputation's value is a star
        // rating.
        new Spec
        {
            Panel = "PersonBiographyCard", ProbeChild = "Media",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Club Facilities: the stadium details block (name/city/rent/... rows, label left, value
        // right) and the youth block's data-container rows. Section titles stay outside as the
        // profile's h3s.
        new Spec
        {
            Panel = "ClubFacilitiesCard", Container = "container-main",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },
        new Spec
        {
            Panel = "ClubFacilitiesCard", Container = "data-container",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Youth Environment card: the same "data-container" block, here holding Academy Coaching /
        // Youth Recruitment / Youth Level / Youth Stadium. Walked raw it is eight loose texts whose
        // pairing is positional guesswork — and two of the four values are the same words
        // ("Adequate training facilities"), so a misread pairing is invisible.
        new Spec
        {
            Panel = "YouthReportSiteYouthEnvironmentCard", Container = "data-container",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Nationalities: the named field rows of the information block; the block's own "header"
        // text is promoted to the table's heading. The primary-nationality row's nation cell is a
        // link, carried as text.
        new Spec
        {
            Panel = "NationalitiesCard", Container = "frames-nationality information",
            Mode = RowMode.RowNames,
            RowNames = new[] { "Frame 1000002389", "secondary-nationalities", "languages", "place-of-birth", "based-in" },
            HeadingChild = "header",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Tactics familiarity: the Overall block and the per-area rows (each a "* frame" child of
        // BottomPart) — area name, fluency word, and the bar's percentage.
        new Spec
        {
            Panel = "PlayerReportTacticsFamiliarityCard", Container = "Overall",
            Mode = RowMode.FlatPairs, IncludeProgress = true,
            Headers = new[] { "Area", "Level", "Familiarity" }, Columns = 3, MinRows = 1,
        },
        new Spec
        {
            Panel = "PlayerReportTacticsFamiliarityCard", Container = "BottomPart",
            IncludeProgress = true,
            Headers = new[] { "Area", "Level", "Familiarity" }, Columns = 3,
        },

        // Head Coach Stats / staff stats sections: each section block holds a TIFO table heading
        // and label/value stat rows.
        new Spec
        {
            Panel = "NonPlayerReportCareerManagerialStatsTabbedCard", ProbeChild = "TIFO-TableHeaderTemplate",
            SkipRows = new[] { "TIFO-TableHeaderTemplate" }, HeadingChild = "TIFO-TableHeaderTemplate",
            Headers = new[] { "Stat", "Value" }, Columns = 2,
        },

        // Match Rules: the selected stage's rule rows (label beside a nested value list) under the
        // NoLeagueRule block, headed by the stage name.
        new Spec
        {
            Panel = "AboutRulesCard", Container = "NoLeagueRule",
            Mode = RowMode.RowNames, RowNames = new[] { "Frame" },
            HeadingChild = "StageName",
            Headers = new[] { "Rule", "Value" }, Columns = 2,
        },

        // Medical Season Summary: the injury-severity blocks (Major/Moderate/Minor/Slight counts).
        new Spec
        {
            Panel = "MedicalCentreSeasonSummaryCard", ProbeChild = "data_display-string-thematic-accent-medium-unpacked",
            Headers = new[] { "Severity", "Injuries" }, Columns = 2,
        },

        // FM27's shared wages tile aligns status, player count and basic wages
        // in its native list. Preserve the existing named-cell table composer.
        new Spec
        {
            Panel = "ClubFinancesCard", Container = "View",
            ScopeAncestor = "FinancesFfp", ScopeAncestorDepth = 20,
            Mode = RowMode.RowType, RowTypeName = "BindingRoot",
            CellNames = new[] { "TypeText", "QuantityText", "AwarageText" },
            Headers = new[] { "Squad status", "Players", "Basic wages" }, Columns = 3,
        },

        // Finance budget cards: the stat rows beside each card's history graph (Current / Spent
        // this season / Original budget, and the wage/scouting expenditure legends).
        new Spec
        {
            Panel = "RecruitmentTransferBudgetsCard", ProbeDescendant = "data_display-graphs-data_points-default",
            SkipRows = new[] { "data_display-graphs-data_points-default" },
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },
        new Spec
        {
            Panel = "RecruitmentWageBudgetCard", ProbeDescendant = "data_display-graphs-data_points-default",
            SkipRows = new[] { "data_display-graphs-data_points-default" },
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },
        new Spec
        {
            Panel = "RecruitmentScoutingBudgetCard", ProbeDescendant = "data_display-graphs-data_points-default",
            SkipRows = new[] { "data_display-graphs-data_points-default" },
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },
        new Spec
        {
            Panel = "RecruitmentWageBudgetCard", Container = "legend-frame",
            Headers = new[] { "Detail", "Value" }, Columns = 2, MinRows = 1,
        },
        new Spec
        {
            Panel = "RecruitmentScoutingBudgetCard", Container = "legend-frame",
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },

        // Transfer Activity's budget block: two "potential impact" frames and two budget frames,
        // each a title beside its amount, plus the weekly-spend note the game draws as a loose
        // sentence. Walked raw that is nine texts whose label/value pairing is positional, with
        // the budget frames' "Available" qualifier stranded a stop away from its amount. Named
        // rows keep the note in the table rather than letting the swallowed container drop it.
        new Spec
        {
            Panel = "RecruitmentTransferActivityTool", Container = "bottom-frame",
            Mode = RowMode.RowNames,
            RowNames = new[]
            {
                "transfer-impact-frame", "wage-impact-frame",
                "transfer-budget-frame", "wage-budget-frame",
                "currrent-spending-text",
            },
            Headers = new[] { "Budget", "Amount" }, Columns = 2, Heading = "Budget",
        },

        // Scouting Assignments: the selected scout's judging attributes, which the game lays out
        // in a two-column grid it names "FakeTable" — a header pair, then one row per attribute
        // holding a label and its graded value. Walked raw that flattens to six loose texts whose
        // pairing is positional guesswork ("Judging Player Ability", "Average", "Judging Player
        // Potential", "Good"). Rows are anchored on their named value element rather than on the
        // row containers, whose names carry the game's own misspelling ("JudginPlayerAbilityRow")
        // — a patch fixing that typo would silently drop every row from a name-matched spec.
        // Headers come from the grid's own header frame, so they stay the game's words.
        new Spec
        {
            Panel = "RecruitmentScoutAssignmentsCard", Container = "FakeTable",
            Mode = RowMode.ValueAnchors, RowNames = new[] { "value" }, AnchorAscend = 4,
            HeaderChild = "HeaderTextContainer",
            Headers = new[] { "Attribute", "Ability" }, Columns = 2,
        },

        // Club Stats card: each category's Cells frame holds stat rows (name, count, rate); the
        // category title stays outside as the profile's h3. The drawn progress bar duplicates the
        // rate, so it stays excluded.
        new Spec
        {
            Panel = "ClubStatsCard", Container = "Cells frame",
            Mode = RowMode.RowNames, RowNames = new[] { "Row" },
            Headers = new[] { "Stat", "Count", "Rate" }, Columns = 3,
        },

        // Opposition Report, Head Coach Information tile: a coach cell followed by label/value
        // pairs. The tile's own title is promoted above the table; the leading coach cell becomes
        // a "Head Coach" row.
        new Spec
        {
            Panel = "TeamReportSite", Container = "TeamReportManagerInformation",
            Mode = RowMode.FlatPairs, HeadingChild = "text", LeadRowLabel = "Head Coach",
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Opposition Report, Notable Players tile: one row per category, anchored by its PlayerN
        // template; cells collected as position, player, category, count and reordered
        // category-first.
        new Spec
        {
            Panel = "TeamReportSite", Container = "NotablePlayers",
            Mode = RowMode.ValueAnchors, RowNamePrefix = "Player", AnchorAscend = 4,
            HeadingChild = "text",
            CellOrder = new[] { 2, 1, 0, 3 },
            Headers = new[] { "Category", "Player", "Position", "Value" }, Columns = 4,
            IncludeLinkText = true,
        },

        // Opposition Report, Set Piece Analysis: the opposing head coach's set-piece preferences —
        // his name and nation cell, then eight label/value pairs split across two columns on
        // screen. Same shape as the Head Coach Information tile above, so the same flat-pair read.
        new Spec
        {
            Panel = "TeamReportSite", Container = "ManagerSetPiecePreference",
            Mode = RowMode.FlatPairs, HeadingChild = "text",
            Headers = new[] { "Preference", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Opposition Report, Expected Tactic: the Strengths and Weaknesses lists. Each row is a
        // StrengthWeaknessCell holding a category and the scout's observation; flattened they
        // alternate as loose paragraphs with nothing tying an observation to its category. The two
        // lists sit in identically shaped unnamed frames (a title frame beside the streamed list),
        // so the child-type multiset is the anchor and each list tables under its own title.
        new Spec
        {
            Panel = "TeamReportSite", ProbeChildTypes = new[] { "VisualElement", "StreamedObjectList" },
            Mode = RowMode.RowNames, RowNames = new[] { "StrengthWeaknessCell" },
            HeadingChild = "text",
            Headers = new[] { "Category", "Observation" }, Columns = 2, IncludeLinkText = true,
        },

        // Goal Analysis (Opposition Report and its Data Hub twin): Goal Types lists (a title text
        // beside a streamed list of type/count rows), Goal Times bars (count drawn above the
        // minute range, swapped), and the Goal Areas shot map (indexed zone templates as
        // positional "Zone N" rows).
        new Spec
        {
            Panel = "TeamReportSite", ProbeChildTypes = new[] { "SIText", "StreamedObjectList" },
            Mode = RowMode.RowType, RowTypeName = "AsyncBindingRoot",
            HeadingChild = "text",
            // Goal Types sits beside Goal Areas and Goal Times inside the Scoring / Conceding half
            // the profile heads at h3, so its own title belongs a level down with theirs.
            HeadingLevel = 4,
            Headers = new[] { "Type", "Goals" }, Columns = 2,
        },
        new Spec
        {
            Panel = "Data Hub", ProbeChildTypes = new[] { "SIText", "StreamedObjectList" },
            // FM27 binds these goal-type rows through ordinary BindingRoot.
            Mode = RowMode.RowType, RowTypeName = "BindingRoot",
            HeadingChild = "text",
            // Goal Types sits inside the Scoring / Conceding half the profile heads at h3, beside
            // Goal Areas and Goal Times, so its own title belongs a level down with theirs.
            HeadingLevel = 4,
            Headers = new[] { "Type", "Goals" }, Columns = 2,
        },
        new Spec
        {
            Panel = "TeamReportSite", Container = "GoalTimes",
            Mode = RowMode.RowNames, RowNames = new[] { "Frame" },
            Headers = new[] { "Minutes", "Goals" }, Columns = 2, SwapFirstTwo = true,
        },
        new Spec
        {
            Panel = "Data Hub", Container = "GoalTimes",
            Mode = RowMode.RowNames, RowNames = new[] { "Frame" },
            Headers = new[] { "Minutes", "Goals" }, Columns = 2, SwapFirstTwo = true,
        },
        new Spec
        {
            Panel = "TeamReportSite", Container = "data_display-graph-shotmap-layout",
            Mode = RowMode.ValueAnchors, RowNamePrefix = "data_display-graph-shotmap-data-",
            NameIndexLabel = "Zone", IndexNames = GoalAreaNames,
            IndexBindingValues = GoalAreaBindingValues,
            Headers = new[] { "Area", "Goals" }, Columns = 2,
            WidenColumns = ShotMapColumns,
        },
        new Spec
        {
            Panel = "Data Hub", Container = "data_display-graph-shotmap-layout",
            Mode = RowMode.ValueAnchors, RowNamePrefix = "data_display-graph-shotmap-data-",
            NameIndexLabel = "Zone", IndexNames = GoalAreaNames,
            IndexBindingValues = GoalAreaBindingValues,
            Headers = new[] { "Area", "Goals" }, Columns = 2,
            WidenColumns = ShotMapColumns,
        },

        // Player Discipline: the per-competition bans grid (pseudo-headers + crest/name/tally
        // rows), consolidated with the game's own header texts as the column headers. The crest
        // button carries no text and is decoration the profile already hides.
        new Spec
        {
            Panel = "PlayerReportDisciplineCard", ProbeChild = "headers",
            Mode = RowMode.RowNames, RowNames = new[] { "DisciplineRow" },
            CellNames = new[] { "Competition", "Totals", "CurrentTalley" },
            HeaderChild = "headers",
            Headers = new[] { "Competition", "Totals", "Current tally" }, Columns = 3,
            IncludeLinkText = true,
        },

        // The Data Hub analysis cards. The radar card's polar chart carries its axes as
        // Value/Label pairs (the drawn polygon itself is unreadable, but every plotted number is
        // there in text), so the card reads as the stat table it always was underneath.
        new Spec
        {
            Panel = "OnThePitchPolygonGraphCard", Container = "FMPolarChart",
            // Two levels: Value and Label sit in SEPARATE BindingExpect wrappers under one
            // per-axis TemplateContainer, so ascending only one reaches a wrapper holding the
            // number alone.
            Mode = RowMode.ValueAnchors, RowNames = new[] { "Value" }, AnchorAscend = 2,
            Headers = new[] { "Metric", "Value" }, Columns = 2, SwapFirstTwo = true,
            // Only the first series is printed; the comparison series (the league average) is
            // drawn alone, so its figures come from the chart's binding, a column per series.
            WidenColumns = RadarSeriesReader.WidenTable,
        },

        // The Player Instructions card's Role Ability radar: the selected player's key attributes
        // for the role, drawn as Value/Label pairs in per-axis geometry containers. This chart is
        // NAMELESS (unlike the Data Hub's), so the anchor is the wrapper whose direct children are
        // the axis containers, probed by the first axis's index name - "0" appears nowhere else on
        // the card - and scoped under the selected-player block. The game's own "Role Ability"
        // text sits above the chart but outside it, so the table names itself.
        new Spec
        {
            Panel = "PlayerInstructionsCard", ProbeChild = "0", ScopeAncestor = "HasPlayer",
            Mode = RowMode.RowType, RowTypeName = "VisualElementTemplateGeometryContainer",
            Headers = new[] { "Attribute", "Value" }, Columns = 2, SwapFirstTwo = true,
            Heading = "Attributes", MinRows = 3,
        },

        // The two pitch-zone cards. Each zone is a bare percentage drawn on a pitch graphic; its
        // binding carries the zone's segment id, which names it (PitchStripZones: six strips along
        // the pitch; FirstContactZones: three cells across the goalmouth). An unrecognised map keeps
        // list-order numbering rather than names the mod would be inventing.
        new Spec
        {
            Panel = "ZoneMapCard", ProbeChildTypes = new[] { "SIImage", "StreamedObjectList" },
            Mode = RowMode.RowNames, RowNames = new[] { "zone frame" },
            OrdinalIndexLabel = "Zone", RowLabelFrom = PitchStripZones.NameOf,
            Headers = new[] { "Zone", "Share" }, Columns = 2, MinRows = 1,
        },
        new Spec
        {
            Panel = "FirstContactZoneMapCard", ProbeChildTypes = new[] { "SIImage", "StreamedObjectList" },
            Mode = RowMode.RowNames, RowNames = new[] { "zone frame" },
            OrdinalIndexLabel = "Zone", RowLabelFrom = FirstContactZones.NameOf,
            Headers = new[] { "Zone", "Share" }, Columns = 2, MinRows = 1,
        },

        // Player Form's Match Ratings summary boxes: six label/value stat rows, each anchored by
        // its named value element (the label sits in the row's sibling frame). Probed on the
        // boxes frame itself — six VisualElement rows, everything in this template generically
        // named — NOT on the section's "PlayerForm" container: anchoring there swallowed the
        // whole Match Ratings section (filter dropdowns, chart, Key, and the Matches
        // StreamedTable) when the walk returned. A same-shaped frame elsewhere on the card
        // extracts zero anchor rows and declines, so the broad probe is safe.
        new Spec
        {
            Panel = "PlayerFormCard",
            ProbeChildTypes = new[]
            {
                "VisualElement", "VisualElement", "VisualElement",
                "VisualElement", "VisualElement", "VisualElement",
            },
            Mode = RowMode.ValueAnchors,
            RowNames = new[] { "GamesPlayed", "MaxRatingText", "MinRating", "AverageRating", "GoalStats", "Assists" },
            AnchorAscend = 3,
            Headers = new[] { "Stat", "Value" }, Columns = 2, MinRows = 1,
        },

        // Player Comparison, the attribute radar. Every axis is drawn as BOTH players' numbers
        // followed by the axis name, so the raw walk reads "14, 9, Speed, 8, 11, Vision, …" —
        // twenty-four loose stops in value-value-label order, with nothing saying which number
        // belongs to which player. Rows are the chart's per-axis geometry containers (the same
        // shape whichever chart variant the switch shows, so the goalkeeper radar reads too) and
        // the cells are reordered to lead with the axis name. Anchored on the wrapper ABOVE the
        // chart's variant switch rather than on the chart itself, whose name is the variant
        // ("Outfield"); a wrapper holding no axis containers extracts nothing and declines.
        new Spec
        {
            Panel = "OnThePitchComparisonCard", Container = "VisualElement",
            Mode = RowMode.RowType, RowTypeName = "VisualElementTemplateGeometryContainer",
            CellOrder = new[] { 2, 0, 1 },
            Headers = ComparisonAttributeHeaders, Columns = 3, MinRows = 3,
        },

        // Player Comparison, each player's Season Stats block: eight label-above-value frames
        // (Apps/Gls/Asts/Yel/Red/PoM/Pas %/Av R) that flatten into sixteen loose stops. One table
        // per player, each headed by the game's own "Season Stats" text a level below the player's
        // Coach Report heading, so the two blocks never merge into one run of numbers. The Apps
        // frame is the only one the template names for itself; the rest all reuse "Gls frame".
        new Spec
        {
            Panel = "OnThePitchComparisonCard", Container = "Stats frame",
            Mode = RowMode.RowNames, RowNames = new[] { "Apps frame", "Gls frame" },
            HeadingChild = "text", HeadingLevel = 4,
            Headers = ComparisonSeasonStatsHeaders, Columns = 2,
        },

        // Player Comparison, the Statistics tab's two stat lists (the selected competition's
        // totals and the per-90 rates). Each streamed row draws player one's figure, the stat
        // name, then player two's, so the raw walk reads "-, Total appearances, 0, -, Goals, -"
        // — a run in which nothing marks where one stat ends and the next begins. Cells are
        // reordered to lead with the stat name. The lists sit in frames the template names only
        // "Frame", so the anchor is the row type scoped to the tab's own "Body frame" — without
        // that scope the same shape matched the Positions & Roles tab's pitch grid and tabled
        // thirteen position labels under stat headers they had nothing to do with.
        new Spec
        {
            Panel = "OnThePitchComparisonCard", Container = "Frame", ScopeAncestor = "Body frame",
            Mode = RowMode.RowType, RowTypeName = "AsyncBindingRoot",
            CellOrder = new[] { 1, 0, 2 },
            Headers = new[] { "Stat", "Player 1", "Player 2" }, Columns = 3, MinRows = 3,
        },

        // Player Comparison, the Attributes tab's bio strip below the attribute tables. Each row
        // draws the field name, player one's value, the separator glyph the layout parts the two
        // halves with, and player two's — so walked raw it reads "Preferred Foot, Right Only, -,
        // Left Only" and the dash looks like a value. Same three columns as the attribute tables
        // above it, with the separator dropped.
        new Spec
        {
            Panel = "OnThePitchComparisonCard", Container = "generic-container",
            Mode = RowMode.RowNames, RowNames = new[] { "preferred-foot", "height" },
            CellOrder = new[] { 0, 1, 3 },
            // The game titles this strip nothing, and it is the last block on the tab — without a
            // title of its own it reads as part of the Averages section above it.
            Heading = "Details",
            Headers = new[] { "Attribute", "Player 1", "Player 2" }, Columns = 3,
        },

        // Player Comparison, the Overview tab's bio pairs. Unlike the Attributes tab's strip above,
        // this template draws each player's pairs in its OWN half — so flattened it reads
        // "Preferred Foot, Right Only, Height, 182 cm, Preferred Foot, Left Only, Height, 182 cm",
        // four labels and four values with nothing saying where one player ends and the next
        // begins. One row per half, numbered in the halves' own order to match the comparison
        // columns everywhere else on the card. The halves are named but their container is not, so
        // it is reached by the section frame above it; the row frames inside each half share that
        // container name, find no halves of their own, and decline.
        new Spec
        {
            Panel = "OnThePitchComparisonCard", Container = "Frame", ScopeAncestor = "Attributes frame",
            Mode = RowMode.RowNames, RowNames = new[] { "Left", "Right" },
            OrdinalIndexLabel = "Player", CellOrder = new[] { 0, 2, 4 },
            Headers = ComparisonOverviewBioHeaders, Columns = 3,
        },

        // Individual Training's player card header: the ability block draws a Current and a
        // Potential row, each its own label above a star rating, which the flat walk emits as four
        // loose paragraphs whose pairing the reader has to infer from order alone. The block's own
        // name is generic, so it is reached through the named header tile above it.
        // FM27 nests workload and training rating together in TrainingDataVisible;
        // collect their own named rows so neither pair is merged into the other.
        new Spec
        {
            Panel = "TeamIndividualTrainingTool", Container = "Ability", ScopeAncestor = "PlayerHeader",
            ScopeAncestorDepth = 6,
            Mode = RowMode.RowNames, RowNames = new[] { "Current", "Potential", "Workload", "TrainingRating" },
            Headers = new[] { "Ability", "Rating" }, Columns = 2,
        },

        // The training schedule's projected impact, on the picker card and again in the create
        // editor. Each metric is drawn as a diverging pair of bars — one growing negative, one
        // positive — with the metric named once above them. Flattened, that emitted the metric
        // TWICE as identically-labelled progress bars ("Injury Risk: 49%" then "Injury Risk: 1%"),
        // with nothing to say which direction either belonged to.
        //
        // Both sides are reported rather than the larger one alone. In practice only one side
        // carries a value and the other sits at a floor (1% here, 2% in the editor), but that floor
        // is a minimum bar WIDTH, not a value the game promises: picking "the" side would mean the
        // mod inferring which reading is real, and being wrong there would speak a false figure.
        // Two columns state exactly what the game holds and leave the reading to the player.
        //
        // Rows are anchored on the metric's own named text rather than on their container, whose
        // name ("BindingExpect") is shared with every other bound block on both panels.
        new Spec
        {
            // The same native list is embedded in pre-season messages and introductions.
            // Its named metric/negative/positive cells establish the shared contract.
            Container = "impact-list",
            Mode = RowMode.ValueAnchors, RowNames = new[] { "impacttext" }, AnchorAscend = 2,
            CellNames = new[] { "impacttext", "impactprogressnegative", "impactprogresspositive" },
            Headers = new[] { "Impact", "Negative", "Positive" }, Columns = 3,
            IncludeProgress = true,
        },
        new Spec
        {
            // Anchored on the impacts list itself, not on the "text section frame" that holds it: a
            // synthesized table replaces its whole container's stops, and that frame also carries the
            // selected schedule's title and description — consolidating from there silently ate both.
            // The list is unnamed, so it is matched by its child-type shape within that frame, which
            // the schedule list on the other side of the card cannot be confused with (it lives under
            // "side menu frame").
            Panel = "TrainingScheduleSelectCard",
            ProbeChildTypes = new[] { "VirtualisedList", "VisualElement" },
            ScopeAncestor = "text section frame", ScopeAncestorDepth = 2,
            Mode = RowMode.ValueAnchors, RowNames = new[] { "impacttext" }, AnchorAscend = 2,
            CellNames = new[] { "impacttext", "impactprogressnegative", "impactprogresspositive" },
            Headers = new[] { "Impact", "Negative", "Positive" }, Columns = 3,
            IncludeProgress = true,
            Heading = "Projected Impact",
        },
        // The session picker's per-session impacts: one row per affected area, each naming the area
        // and the effect on it ("Fatigue" / "Greatly Reduced"). Flattened they were ten alternating
        // paragraphs whose pairing the reader had to infer from order alone, and a session whose row
        // carries several values (the attribute list an Endurance session trains) broke that order
        // outright. Extra cells join the last column, so a multi-value row reads as one effect cell.
        //
        // Rows are anchored on the area's own named element rather than their container, whose name
        // ("Frame") is the most generic on the panel.
        new Spec
        {
            Panel = "TrainingSessionSelectCard", Container = "impactsframe",
            Mode = RowMode.ValueAnchors, RowNames = new[] { "impactcategory" }, AnchorAscend = 1,
            Headers = new[] { "Impact", "Effect" }, Columns = 2,
            // The Attributes row lists several distinct attributes; joined by a space they read as one
            // phrase ("Stamina Natural Fitness Work Rate") with the boundaries lost.
            OverflowSeparator = ", ",
        },

        // The Coach Report's ability block: a Current and a Potential row, each drawn as a label,
        // a star rating and the coach's wording for that level ("Decent National player"). Walked
        // raw that is six loose paragraphs whose grouping the reader has to infer from order, and
        // the two star ratings read identically apart from their number.
        new Spec
        {
            Panel = "CoachReportCard", Container = "ability-info",
            Mode = RowMode.RowNames, RowNames = new[] { "current-ability-frame", "potential-ability-frame" },
            Headers = new[] { "Ability", "Rating", "Assessment" }, Columns = 3,
            Heading = "Ability",
        },

        // The Scouting Report's Scout tab draws the same ability tile, its two rows as unnamed frames
        // (label, star rating, assessment) directly under the tile.
        new Spec
        {
            Panel = "ScoutingReportCard", Container = "tile-states-coach-report-medium", ScopeAncestor = "ability-info",
            Mode = RowMode.Children,
            Headers = new[] { "Ability", "Rating", "Assessment" }, Columns = 3,
            Heading = "Ability",
        },

        // The Scouting Report's Transfer Information block: its own title, then label/value frames
        // (Transfer Interest, Loan Interest, Transfer Value, Estimated Salary). The title becomes the
        // table's heading.
        new Spec
        {
            Panel = "ScoutingReportCard", Container = "transfer-info",
            Mode = RowMode.FlatPairs, TitleFromFirstCell = true,
            Headers = new[] { "Detail", "Value" }, Columns = 2,
        },

        // The Scouting Report's Matches Watched block: under the match picker and the scout's notes,
        // the chosen match as one unlabelled line (date, opponent's club, score, rating), which read
        // as four loose values. The line's unnamed container is found by its date text.
        new Spec
        {
            Panel = "ScoutingReportCard", ProbeDescendant = "selectedDateText",
            Mode = RowMode.FlatPairs, IncludeLinkText = true,
            Headers = new[] { "Date", "Opponent", "Score", "Rating" }, Columns = 4, MinRows = 1,
        },

        // The Season Stats card's Competition Statistics block — everything below the competitions
        // table, redrawn whenever a different competition is selected. The game lays it out as six
        // tiled sub-blocks of label-above-value frames, which flattened into roughly seventy loose
        // paragraphs alternating stat name and figure, with nothing pairing them but order.
        //
        // Anchored on the whole block rather than per sub-block: the sub-blocks are the position
        // split (a goalkeeper's saves block where an outfielder's goals block goes, under
        // "goalkeeper_outfield_stats_switch"), and only their FRAMES are named — the rows inside
        // are all "Frame". FlatPairs reads whichever variant is showing, so one spec covers both
        // positions and any row the switches add or drop. An odd run declines the whole table and
        // keeps the raw stops, so a template that stops pairing degrades rather than shifts every
        // figure onto the wrong stat.
        //
        // The block's own title becomes the heading and is excluded from the cells; without that
        // exclusion it would be an unpaired lead cell and the table would decline outright.
        new Spec
        {
            Panel = "PlayerSeasonStatsCard", Container = "comp_stats_available",
            Mode = RowMode.FlatPairs,
            HeadingChild = "Title_Frame",
            Headers = new[] { "Statistic", "Value" }, Columns = 2,
            SwapNumericLeadPairs = true,
        },

        // The team scouting report EMAIL (the Messages panel embeds the same team-report templates
        // the Opposition Report screen uses, but under its own panel id, so the TeamReportSite specs
        // above never fire there). Four blocks that flatten into loose paragraph runs:

        // Head Coach Information: an unnamed title text over three label/value frames (Name /
        // Pfd. Formation / Playing Mentality). The block is unnamed throughout, so it is matched by
        // its child-type pair inside the report's FixtureCard, and its drawn title — unreachable by
        // name — is promoted from the leading orphan cell.
        new Spec
        {
            Panel = "PortalMessagesTool", ProbeChildTypes = new[] { "SIText", "BindableSwitchElement" },
            ScopeAncestor = "FixtureCard",
            Mode = RowMode.FlatPairs, TitleFromFirstCell = true,
            Headers = new[] { "Detail", "Value" }, Columns = 2, IncludeLinkText = true,
        },

        // Players: the notable-players grid drawn as three stacked rows — the category labels, the
        // names, the figures — which flattened into nine loose stops in row-major order with
        // nothing tying a name to its category. The label row becomes the column headers and the
        // drawn title the heading, both the game's own text; a template that stops drawing three
        // labels declines rather than mislabelling the columns.
        new Spec
        {
            Panel = "PortalMessagesTool", ProbeChildTypes = new[] { "SIText", "VisualElement" },
            ScopeAncestor = "FixtureCard", ScopeAncestorDepth = 1,
            Mode = RowMode.FlatPairs, TitleFromFirstCell = true, FirstRowHeaders = true,
            Headers = null, Columns = 3, IncludeLinkText = true,
        },

        // The expected-lineup pitch: eleven starters drawn as pitch indicators (shirt number,
        // rating, name), which flattened into eleven anonymous number/rating/name triplets. Rows
        // anchor on the shirt number (only a filled slot has one — the formation's empty positions
        // stay out), named cells keep the columns aligned when a rating is missing, and each row is
        // labelled with its position slot from the indicator's template name (GK, DR, STC...).
        new Spec
        {
            Panel = "PortalMessagesTool", Container = "FMTacticsPanel",
            Mode = RowMode.ValueAnchors, RowNames = new[] { "SquadNumber" }, AnchorAscend = 6,
            OrdinalIndexLabel = "Slot", RowLabelFrom = PitchSlotName,
            CellNames = new[] { "Frame", "SquadNumber", "Expect" },
            Headers = new[] { "Position", "Player", "Number", "Rating" }, Columns = 4,
            Heading = "Lineup", MinRows = 3, IncludeLinkText = true,
        },

        // Strengths and Weaknesses: the same StrengthWeaknessCell lists as the Opposition Report's
        // Expected Tactic tile (the spec above), re-keyed to the email's panel. Each list tables
        // under its own drawn title; the paging chrome for the virtualised list is emitted above.
        new Spec
        {
            Panel = "PortalMessagesTool", ProbeChildTypes = new[] { "VisualElement", "StreamedObjectList" },
            Mode = RowMode.RowNames, RowNames = new[] { "StrengthWeaknessCell" },
            HeadingChild = "text",
            Headers = new[] { "Category", "Observation" }, Columns = 2, IncludeLinkText = true,
        },

        new Spec
        {
            Panel = "CreateTrainingScheduleTool", Container = "Impacts",
            Mode = RowMode.ValueAnchors, RowNames = new[] { "impacttext" }, AnchorAscend = 2,
            CellNames = new[] { "impacttext", "impactprogressnegative", "impactprogresspositive" },
            Headers = new[] { "Impact", "Negative", "Positive" }, Columns = 3,
            IncludeProgress = true,
            Heading = "Projected Impact",
        },
    };
}

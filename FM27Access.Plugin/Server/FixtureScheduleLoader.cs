using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Contexts;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the Fixture Schedule's streamed rows on the request (background) thread, filling a walked
/// <see cref="ScreenWalker.FixtureScheduleItem"/>'s Rows/Truncated. Same protocol as
/// <see cref="StreamedTableLoader"/> (whose row budgets and cap it shares): the list virtualises
/// and its cells stream-bind a frame or two after a row realizes, so the read drives
/// <see cref="StreamedTableReader{TRow}"/> — every game touch marshalled through
/// <see cref="MainThreadDispatcher"/>, a real sleep between polls so frames advance.
/// </summary>
internal static class FixtureScheduleLoader
{
    private const int RowReadyStepMs = 16;
    private const int RowReadyBudgetMs = 700;
    private const int TableLoadBudgetMs = 8000;
    // The fixture schedule is read WHOLE, not paged: its two tables (played and upcoming) are a
    // split of the season, and a page boundary would cut that split at an arbitrary fixture — after
    // 50 played matches the upcoming table simply vanished, because every row the read ever saw was
    // a played one. So the row cap is the list's own RowCount(): reading every row IS the cap, and
    // it needs no constant because a club's season bounds it (~40-80 rows). The generic paged path
    // (StreamedTableLoader) and the consolidated fixture TILE walk (ScreenWalker.PageMatchTiles,
    // which pages at 50 after a 290-tie competition livelocked the surface) are unchanged.
    //
    // TableLoadBudgetMs is the real backstop, and this changes what truncation MEANS here: it is no
    // longer by design, it fires only when the read is genuinely slow or broken — which is exactly
    // when the player should hear about it.
    //
    // Used only when RowCount() is unreadable: without a count the read cannot know where the season
    // ends, so it keeps the fixed cap it has always had and reports the truncation honestly. An
    // unreadable count must never become an unbounded read.
    internal const int MaxRowsWithoutCount = 50;
    // Cap on waiting for the list itself to realize. A tab switch composes immediately but the game
    // realizes the VirtualisedList on its layout cadence, so an instant cold read answers -1 and
    // would drop to the fallback cap for a list that was merely a few frames late.
    private const int ListRealizeBudgetMs = 3000;
    private const int ListRealizeStepMs = 150;
    // Harvest slicing (see HarvestSlicer): how long one slice should hold the main thread, the
    // unmeasured first slice's size, the per-slice row ceiling, and the yield that hands the frame
    // back. Reading the whole season made the old single-dispatch harvest a proportionally longer
    // stall, so the harvest is spent across frames like every other streamed table's.
    private const int SliceTargetMs = 30;
    private const int FirstSliceRows = 4;
    private const int MaxSliceRows = 50;
    private const int SliceYieldMs = 1;
    // Floor on what one harvest may spend when the overall budget is nearly gone: enough to be worth
    // the dispatch, small enough not to extend the load meaningfully.
    private const int MinHarvestBudgetMs = 250;

    // The read drives the list's shared scroll position across frames; two concurrent reads would
    // scroll each other's rows out mid-read (see StreamedTableLoader's lock for the full story).
    // Cross-frame fixture commands use the same lease: a concurrent harvest
    // must not scroll their validated row away while it realizes/rebinds.
    internal static readonly object ReadLock = new object();

    internal static void Load(ScreenWalker.FixtureScheduleItem item)
    {
        try
        {
            lock (ReadLock)
            {
                // The count decides how many rows are read. Unreadable (-1) means the list is not
                // realized yet, which a busy main thread can also make look like an Invoke timeout —
                // the same wait in a different coat, so those failures poll too (first one logged)
                // rather than dropping straight to the fallback cap.
                long start = Environment.TickCount64;
                int totalRows = -1;
                bool invokeFailLogged = false;
                while (true)
                {
                    try
                    {
                        totalRows = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            return item.Context.RowCount();
                        });
                    }
                    catch (ContextMenuGuard.AbortException) { throw; }
                    catch (Exception ex)
                    {
                        if (!invokeFailLogged)
                        {
                            invokeFailLogged = true;
                            ServerText.LogBackground($"[FixtureScheduleLoader] row count read failed, polling: {ex.Message}");
                        }
                    }
                    if (totalRows >= 0 || Environment.TickCount64 - start >= ListRealizeBudgetMs) break;
                    Thread.Sleep(ListRealizeStepMs);
                }
                int maxRows = totalRows < 0 ? MaxRowsWithoutCount : totalRows;
                // Inclusive, and one past the last row deliberately: the reader probes that index to
                // decide MoreRows, so leaving it out would turn the probe into a real scroll.
                int harvestEnd = maxRows;
                // Batch cache: rows that are already realized and fully bound are harvested up front
                // (and re-harvested after each real scroll realizes a fresh window), instead of two
                // dispatches — two frames — per row. See StreamedTableLoader, whose wiring this
                // mirrors: a cached row reports pending 0 ("already ready at scroll") and ReadRow
                // serves it without touching the game, so the reader's waiting/budget/truncation
                // logic is unchanged and only engages for rows that genuinely need realizing or are
                // still binding. Written only inside main-thread dispatches while this thread blocks
                // on them, so there is no concurrent access.
                var cache = new Dictionary<int, FixtureScheduleContext.Row>();
                bool duplicatesLogged = false;
                // One slice. Aborts when a context menu is open or opening — the sweep's scrolling
                // would recycle the menu's anchor row and kill it (see ContextMenuGuard).
                HarvestSlicer.Slice RunSlice(int fromIdx, int maxSliceRows) => MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    long t0 = Stopwatch.GetTimestamp();
                    var swept = item.Context.SweepReadyRows(fromIdx, cache.ContainsKey, harvestEnd,
                                                            maxSliceRows, !duplicatesLogged, out bool hitCap);
                    long elapsed = (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency;
                    duplicatesLogged = true;
                    foreach (var kv in swept) cache[kv.Key] = kv.Value;
                    // Read in the same dispatch as the sweep, so it describes the very window the
                    // slice saw rather than one the game has since moved on from.
                    return new HarvestSlicer.Slice(swept.Count, elapsed, item.Context.RowCount(), hitCap);
                });
                // Must be called from THIS (background) thread, never from inside a dispatch: Invoke
                // runs inline on the main thread, so a slice loop there would run start to finish in
                // one frame and its yield would sleep the main thread — the opposite of the point.
                // Each harvest gets what is left of the load's overall budget, not a budget of its
                // own, so repeated post-scroll harvests cannot blow past TableLoadBudgetMs.
                HarvestSlicer.Outcome HarvestFrom(int fromIdx)
                {
                    long left = TableLoadBudgetMs - (Environment.TickCount64 - start);
                    var slicer = new HarvestSlicer(SliceTargetMs, FirstSliceRows, 1, MaxSliceRows,
                                                   Math.Max(MinHarvestBudgetMs, left));
                    return slicer.Run(new HarvestSlicer.Ops
                    {
                        RunSlice = cap => RunSlice(fromIdx, cap),
                        Yield = () => Thread.Sleep(SliceYieldMs),
                        NowMs = () => Environment.TickCount64,
                    });
                }
                var outcome = HarvestFrom(0);
                if (outcome == HarvestSlicer.Outcome.RowCountChanged)
                {
                    // Fixtures were inserted or removed while the harvest was running (a cup draw
                    // landing, a postponement), so the cache is a mosaic of two different schedules
                    // and a data index no longer means one fixture. Throw it away and harvest once
                    // more against the settled list.
                    ServerText.LogBackground("[FixtureScheduleLoader] rows changed mid-harvest; restarting once");
                    cache.Clear();
                    outcome = HarvestFrom(0);
                }
                if (outcome == HarvestSlicer.Outcome.ListUnreadable)
                {
                    // The list went away mid-harvest (panel teardown, tab switch). What the cache
                    // holds is part of a schedule that no longer exists; rendering it would present
                    // a fragment of the season as the whole of it.
                    ServerText.LogBackground("[FixtureScheduleLoader] list vanished mid-harvest; rendering schedule as unloaded");
                    item.Rows = null;
                    item.Truncated = false;
                    item.LoadFailed = true;
                    return;
                }
                if (outcome != HarvestSlicer.Outcome.Complete)
                    ServerText.LogBackground($"[FixtureScheduleLoader] harvest ended {outcome} with {cache.Count} row(s) cached");
                // The row read gets whatever the list wait and the harvest left of the overall
                // budget, so the total stays under the route's compose timeout.
                int rowBudget = (int)Math.Max(1000, TableLoadBudgetMs - (Environment.TickCount64 - start));
                var reader = new StreamedTableReader<FixtureScheduleContext.Row>(
                    maxRows, RowReadyBudgetMs, rowBudget);
                var ops = new StreamedTableReader<FixtureScheduleContext.Row>.Ops
                {
                    ScrollAndCheckReady = idx =>
                    {
                        if (cache.ContainsKey(idx)) return 0;
                        int pending = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            return item.Context.ScrollToRowPending(idx);
                        });
                        // The scroll just realized a fresh window; harvest its already-bound rows so
                        // the rows after this one become cache hits. Sliced, and in its own
                        // dispatches rather than the scroll's, so the harvest never rides along as
                        // one long hold — and landing a frame later gives the freshly realized rows
                        // a frame to bind, so more of them are ready.
                        if (pending >= 0)
                        {
                            var harvest = HarvestFrom(idx + 1);
                            if (harvest != HarvestSlicer.Outcome.Complete)
                                ServerText.LogBackground($"[FixtureScheduleLoader] post-scroll harvest at {idx + 1} ended {harvest}");
                        }
                        return pending;
                    },
                    PollAndRead = idx => MainThreadDispatcher.Invoke(() =>
                    {
                        if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                        return item.Context.CountAndReadRow(idx);
                    }),
                    ReadRow = idx => cache.TryGetValue(idx, out var cached) ? cached : MainThreadDispatcher.Invoke(() =>
                    {
                        if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                        return item.Context.ReadRow(idx);
                    }),
                    Sleep = () => Thread.Sleep(RowReadyStepMs),
                    NowMs = () => Environment.TickCount64,
                };
                var result = reader.Read(ops);
                // An unreadable count means the list was never realized (GetVirtualisedList returned
                // null), so the very first row answered -1 and the reader stopped empty —
                // indistinguishable here from a genuinely empty schedule, which reports a count of 0.
                // Rendering that as an empty schedule fabricates "this club has no fixtures".
                if (totalRows < 0 && result.Rows.Count == 0)
                {
                    ServerText.LogBackground("[FixtureScheduleLoader] list not realized; rendering schedule as unloaded");
                    item.Rows = null;
                    item.Truncated = false;
                    item.LoadFailed = true;
                    return;
                }
                item.Rows = result.Rows;
                // Read whole: the cap was the season's own row count, so MoreRows cannot mean "cut
                // off" — only a spent budget is a real omission. Without a count the fixed cap is
                // back and a further row past it IS the omission, which is what Truncated means there.
                item.Truncated = totalRows < 0 ? result.Truncated : result.Incomplete;
                if (item.Truncated)
                    ServerText.LogBackground($"[FixtureScheduleLoader] schedule truncated at {result.Rows.Count} of {totalRows} row(s)");
                NoteUnreadableColumns(item);
                NoteUnmappedOutcomes(item);
            }
        }
        catch (ContextMenuGuard.AbortException)
        {
            // Deliberate stand-down, not a failure: the page is behind the modal menu, and the
            // post-close re-render reads it fresh (see ContextMenuGuard).
            ServerText.LogBackground("[FixtureScheduleLoader] read paused: context menu active");
            item.Rows = new List<FixtureScheduleContext.Row>();
            item.LoadFailed = true;
        }
        catch (Exception ex)
        {
            // Background thread: Log.* would hard-crash off the main thread. An honest failure
            // marker renders in place of the rows (see HtmlRenderer.RenderFixtureSchedule).
            ServerText.LogBackground($"[FixtureScheduleLoader] Schedule load failed: {ex.Message}");
            item.Rows = new List<FixtureScheduleContext.Row>();
            item.LoadFailed = true;
        }
    }

    // BuildCell marks a column the header declares but the row template lacks with the Unavailable
    // marker instead of logging per poll; surface that degrade once per load here.
    private static void NoteUnreadableColumns(ScreenWalker.FixtureScheduleItem item)
    {
        var badCols = new HashSet<int>();
        foreach (var row in item.Rows)
        {
            if (row == null) continue;
            for (int c = 0; c < row.Cells.Count; c++)
                if (row.Cells[c].Kind == FixtureScheduleContext.CellKind.Content
                    && row.Cells[c].Text == Speech.Degrade.Unavailable)
                    badCols.Add(c);
        }
        foreach (int c in badCols)
            ServerText.LogBackground($"[degrade] fixture schedule: column {c} ('{(c < item.Headers.Count ? item.Headers[c].Key : "?")}') unreadable in some rows");
    }

    // A result badge class MapOutcome didn't recognize means those scores render without a W/D/L
    // letter; log the class once per load so the mapping can be extended.
    private static void NoteUnmappedOutcomes(ScreenWalker.FixtureScheduleItem item)
    {
        var classes = new HashSet<string>();
        foreach (var row in item.Rows)
        {
            if (row == null) continue;
            foreach (var cell in row.Cells)
                if (!string.IsNullOrEmpty(cell.UnmappedOutcomeClass))
                    classes.Add(cell.UnmappedOutcomeClass);
        }
        foreach (var cls in classes)
            ServerText.LogBackground($"[degrade] fixture schedule: unmapped result badge class '{cls}'; scores shown without W/D/L");
    }
}

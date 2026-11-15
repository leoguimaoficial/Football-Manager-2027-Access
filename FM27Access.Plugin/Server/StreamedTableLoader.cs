using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Contexts;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads a streamed table's rows on the request (background) thread, filling a walked
/// <see cref="ScreenWalker.TableItem"/>'s Rows/Truncated. Streamed rows bind their cell data
/// asynchronously a frame or two after a row is realized, so a single blocking main-thread read
/// only ever sees empty shells. This drives <see cref="StreamedTableReader{TRow}"/>: every game
/// touch is marshalled through <see cref="MainThreadDispatcher"/> and the per-poll wait is a real
/// <see cref="Thread.Sleep"/>, so game frames advance between polls and the data lands.
/// </summary>
internal static class StreamedTableLoader
{
    // Rows per page on the HTML surface. A table longer than this is paged rather than cut off (see
    // TablePages / TablePagerHtml), so this bounds the cost of ONE render no matter how long the
    // table is: the player reaches row 900 by paging, not by making every refresh read 900 rows.
    internal const int PageRows = 50;
    private const int RowReadyStepMs = 16;   // ~one frame; the wait that lets streamed data bind
    // Cap on waiting for a row's cells to all report ready: a row whose slowest cell never binds (a
    // stuck binder) is read at its honest current state once this is spent. Must exceed the slowest
    // single-cell bind lag (the loan expiry date can land 100ms+ after the rest) so a late cell is
    // waited out rather than dropped.
    private const int RowReadyBudgetMs = 700;
    private const int TableLoadBudgetMs = 8000; // overall cap (< the route's main-thread timeouts combined)
    // Cap on waiting for the list itself to exist. A tab switch composes immediately, but the
    // game realizes the new tab's VirtualisedList on its layout cadence, which lags seconds when
    // the main thread is busy (world creation, screen build) — an instant cold read would render
    // the not-loaded marker and burn the retry budget against the same cold state. Charged
    // against the overall budget; capped well below it so a table whose list never realizes (a
    // permanently unreadable one) costs 3s once, not 8s per compose.
    private const int ListRealizeBudgetMs = 3000;
    private const int ListRealizeStepMs = 150;
    // How long one harvest slice should hold the main thread. The whole harvest still costs what it
    // costs; this is how much of it the game pays in any one frame. Kept well under the drain's
    // per-frame budget (MainThreadDispatcher.DrainBudgetMs), because the drain can only stop
    // dequeuing BETWEEN units — the honest worst case is that budget plus one slice.
    private const int SliceTargetMs = 30;
    // The first slice has no per-row cost to size itself from, so it starts small and the second
    // slice onward is sized from measurement. Too large here and the first slice is the very stall
    // this exists to remove.
    private const int FirstSliceRows = 4;
    private const int SliceYieldMs = 1;   // enough for the drain to empty and the frame to finish
    // Floor on what one harvest may spend when the overall budget is nearly gone: enough to be worth
    // the dispatch, small enough not to extend the load meaningfully.
    private const int MinHarvestBudgetMs = 250;
    // How long a row's pending count must sit STILL before the row is declared stalled and read at
    // its honest current state — the same read the RowReadyBudgetMs break performs, taken as soon as
    // the stall is provable instead of after the full budget. Some rows never finish binding at all:
    // an empty tactics sub slot keeps a visible Morale text whose binder stays not-ready forever, and
    // ~10 such slots waiting out 700ms each made every compose of that screen cost ~8s. Stillness,
    // not content, is the tell: a genuinely binding row's pending count moves as cells land, which
    // resets the clock and preserves its full budget. Must comfortably exceed the longest observed
    // real bind gap between cells (a loan expiry date can land 100ms+ after the rest of its row).
    private const int StuckStillMs = 250;
    // Stalled rows found by the batch pre-settle are model-built in chunks of this many per dispatch,
    // for the same reason the harvest is sliced: all of them in one frame is a visible stall.
    private const int StalledReadRows = 4;

    // Reading a streamed table drives the list's scroll position across frames to realize each row.
    // That scroll is shared mutable UI state, so two reads running at once (e.g. an agent /body racing
    // the companion's SSE re-render, or two overlapping renders) scroll each other's rows out mid-read
    // and corrupt the result — a row gets re-realized cold under the other reader and is read blank
    // even though its own cells reported ready. Serialise: only one streamed-table read at a time.
    // Held only by background request threads (never the main thread, which keeps pumping Invokes), so
    // a waiting reader just queues behind the active one and then reads fresh.
    private static readonly object _readLock = new object();

    internal static void Load(ScreenWalker.TableItem item)
    {
        // FM27 can rebind an existing table's columns after a tab action. The
        // walk's headers then describe the previous tab, while the across-frame
        // harvest already reads the new rows. Check the same native headers at
        // the end and re-harvest against the new schema before serving anything.
        // Bounded: continuous changes remain provisional, never mislabeled data.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // An optimistic refresh can still be harvesting the outgoing
            // screen when FM27 tears down its tables. A detached/hidden table
            // is not a cold list: waiting for it to realize again only delays
            // the queued destination body (and multiplied across several lists
            // can exceed the client's body deadline).
            bool attached = MainThreadDispatcher.Invoke(() => item.Context.Scope is UnityEngine.UIElements.VisualElement root
                && root.panel != null && ElementBridge.IsVisibleInHierarchy(root));
            if (!attached)
            {
                item.Rows = null;
                item.Page = 0;
                item.PageCount = 1;
                item.Truncated = false;
                return;
            }
            LoadCore(item);
            // The original loader already reports a vanished/unrealized list
            // as provisional. Do not turn that into another header-rebind load.
            if (item.Rows == null) return;
            var headers = MainThreadDispatcher.Invoke(() => item.Context.GetHeaders());
            bool same = headers.Count == item.Headers.Count;
            for (int i = 0; same && i < headers.Count; i++)
                same = string.Equals(headers[i].Text, item.Headers[i].Text, StringComparison.Ordinal);
            if (same)
            {
                item.Headers = headers;
                MainThreadDispatcher.Invoke(() => { ScreenWalker.ReadTableFooter(item); return true; });
                return;
            }
            item.Headers = headers;
        }
        item.Rows = null;
        ServerText.LogBackground("[StreamedTableLoader] Columns changed throughout harvest; keeping the table provisional.");
    }

    private static void LoadCore(ScreenWalker.TableItem item)
    {
        var ctx = item.Context;
        try
        {
            lock (_readLock)
            {
                // The total decides how many pages there are and which page the stored one clamps
                // to. Unreadable (-1) means no paging at all: the table reads from row 0 and says so
                // if there is more, exactly as it did before paging existed — a page count that
                // might be wrong is worse than none. -1 is polled rather than accepted immediately
                // (see ListRealizeBudgetMs); a busy main thread can also time an Invoke out, which
                // is the same wait in a different coat, so those failures poll too (first one
                // logged) instead of abandoning the read.
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
                            return ctx.RowCount();
                        });
                    }
                    catch (ContextMenuGuard.AbortException) { throw; }
                    catch (Exception ex)
                    {
                        if (!invokeFailLogged)
                        {
                            invokeFailLogged = true;
                            ServerText.LogBackground($"[StreamedTableLoader] row count read failed, polling: {ex.Message}");
                        }
                    }
                    if (totalRows >= 0 || Environment.TickCount64 - start >= ListRealizeBudgetMs) break;
                    Thread.Sleep(ListRealizeStepMs);
                }
                long tCount = Environment.TickCount64 - start;
                int pageCount = totalRows < 0 ? 1 : TablePaging.PageCount(totalRows, PageRows);
                int page = totalRows < 0 ? 0 : TablePaging.Clamp(TablePages.Get(item.Path), pageCount);
                int startIndex = page * PageRows;
                // Batch cache: rows that are already realized and fully bound are harvested up front
                // (and re-harvested after each real scroll realizes a fresh window), instead of two
                // dispatches — two frames — per row. The ops below answer from the cache: a cached
                // row reports pending 0 ("already ready at scroll") and ReadRow serves it without
                // touching the game, so the reader's waiting/budget/truncation logic is unchanged and
                // only engages for rows that genuinely need realizing or are still binding. A cache
                // covering the whole table also means the read never scrolls the player's live view.
                //
                // The up-front harvest is spent over several dispatches rather than one (see
                // HarvestSlicer): the work is right, but all of it in one frame was a 250-563ms stall.
                // So the cache is written across frames, and the window can recycle between them —
                // each ROW is still built inside a single dispatch, from the element bound to its
                // data index, so a row is never assembled from two windows. What the slicer guards is
                // the level above that: a row count that changes mid-harvest means an index no longer
                // means one entity, and the harvest starts over. Only ever written inside main-thread
                // dispatches while this thread blocks on them, so there is no concurrent access.
                var cache = new Dictionary<int, TableRow>();
                // Inclusive of the row one past the page, deliberately. The reader probes that index
                // to decide MoreRows; leaving it out of the cache turns the probe into a real
                // ScrollToRowPending, which scrolls the player's live view for a row nobody reads.
                int harvestEnd = startIndex + PageRows;
                bool duplicatesLogged = false;
                // One slice. Each game-touching op aborts when a context menu is open or opening: the
                // sweep's scrolling would recycle the menu's anchor row and the game would drop the
                // menu (see ContextMenuGuard). The page being read is behind the modal menu anyway.
                HarvestSlicer.Slice RunSlice(int fromIdx, int maxRows) => MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    long t0 = Stopwatch.GetTimestamp();
                    var swept = ctx.SweepReadyRows(fromIdx, cache.ContainsKey, harvestEnd,
                                                   maxRows, !duplicatesLogged, out bool hitCap);
                    long elapsed = (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency;
                    duplicatesLogged = true;
                    foreach (var kv in swept) cache[kv.Key] = kv.Value;
                    // Read in the same dispatch as the sweep, so it describes the very window the
                    // slice saw rather than one the game has since moved on from.
                    return new HarvestSlicer.Slice(swept.Count, elapsed, ctx.RowCount(), hitCap);
                });
                // Must be called from THIS (background) thread, never from inside a dispatch: Invoke
                // runs inline on the main thread, so a slice loop there would run start to finish in
                // one frame and its yield would sleep the main thread — the opposite of the point.
                //
                // Each harvest gets what is left of the load's overall budget, not a budget of its
                // own. A read scrolls once per unrealized window, so a fixed per-harvest budget would
                // let a long table spend it over and over and blow past TableLoadBudgetMs — and past
                // the route timeouts that constant exists to stay under. The floor keeps a harvest
                // starting late from being cancelled before it reads a single row.
                HarvestSlicer.Outcome HarvestFrom(int fromIdx)
                {
                    long left = TableLoadBudgetMs - (Environment.TickCount64 - start);
                    var slicer = new HarvestSlicer(SliceTargetMs, FirstSliceRows, 1, PageRows,
                                                   Math.Max(MinHarvestBudgetMs, left));
                    return slicer.Run(new HarvestSlicer.Ops
                    {
                        RunSlice = cap => RunSlice(fromIdx, cap),
                        Yield = () => Thread.Sleep(SliceYieldMs),
                        NowMs = () => Environment.TickCount64,
                    });
                }
                long tHarvestStart = Environment.TickCount64;
                // Stall-detection snapshot, taken BEFORE the harvest so the harvest's own wall time
                // (frames keep advancing while its slices yield) counts toward the stillness window
                // — on the tactics screen the harvest alone usually covers StuckStillMs, making the
                // pre-settle below cost no extra wait at all.
                var pendingBefore = MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    return ctx.PendingCountsInWindow(startIndex, harvestEnd, null);
                });
                var outcome = HarvestFrom(startIndex);
                if (outcome == HarvestSlicer.Outcome.RowCountChanged)
                {
                    // Rows were inserted or removed while the harvest was running, so the cache is a
                    // mosaic of two different tables and a data index no longer means one entity.
                    // Throw it away and harvest once more against the settled table. The stall
                    // snapshot describes the old table's indices — void it (the reader's own settle
                    // path still covers every row).
                    ServerText.LogBackground("[StreamedTableLoader] rows changed mid-harvest; restarting once");
                    cache.Clear();
                    pendingBefore.Clear();
                    outcome = HarvestFrom(startIndex);
                }
                if (outcome == HarvestSlicer.Outcome.ListUnreadable)
                {
                    // The list went away mid-harvest (panel teardown, tab switch). What the cache
                    // holds is part of a table that no longer exists; rendering it would present a
                    // short table as a whole one.
                    ServerText.LogBackground("[StreamedTableLoader] list vanished mid-harvest; rendering table as unloaded");
                    item.Rows = null;
                    item.Page = 0;
                    item.PageCount = 1;
                    item.Truncated = false;
                    return;
                }
                if (outcome != HarvestSlicer.Outcome.Complete)
                    ServerText.LogBackground($"[StreamedTableLoader] harvest ended {outcome} with {cache.Count} row(s) cached");
                // Batch pre-settle: the realized rows the harvest left behind are either still
                // binding or stalled (see StuckStillMs). Rather than let the reader discover each
                // stalled row alone — serially, a full row budget apiece — the pre-harvest snapshot
                // above and the harvest's own duration form a shared stillness window over ALL of
                // them at once (topped up with a sleep and a re-harvest only when the harvest ran
                // faster than the window). Rows whose pending count did not move over the window are
                // read at their honest current state — exactly what the reader's budget break would
                // do RowReadyBudgetMs later. Rows whose count moved are genuinely binding and keep
                // their full budget in the reader.
                int preSettled = 0;
                // Every newly realized window needs the same shared settle as
                // the initial window. Otherwise a tactics list scrolled from
                // its players back to 26 empty selection slots waits 250ms
                // separately for each permanently pending morale binder and
                // spends the whole table budget before reaching the players.
                void SettleWindow(int fromIndex, Dictionary<int, int> observedPending,
                                  long observedAt, HarvestSlicer.Outcome harvestOutcome)
                {
                    bool anyCandidate = false;
                    foreach (var kv in observedPending)
                        if (!cache.ContainsKey(kv.Key)) { anyCandidate = true; break; }
                    if (harvestOutcome == HarvestSlicer.Outcome.Complete && anyCandidate)
                    {
                        long stillLeft = StuckStillMs - (Environment.TickCount64 - observedAt);
                        if (stillLeft > 0)
                        {
                            Thread.Sleep((int)stillLeft);
                            // Rows that finished binding during the top-up sleep are picked up ready,
                            // with their complete values, instead of being misread as stalled.
                            var again = HarvestFrom(fromIndex);
                            if (again != HarvestSlicer.Outcome.Complete)
                            {
                                // The window shifted or the list went unreadable mid-wait; the snapshot
                                // no longer describes these rows. The reader's own path covers them.
                                ServerText.LogBackground($"[StreamedTableLoader] pre-settle re-harvest ended {again}; leaving stalled rows to the reader");
                                observedPending.Clear();
                            }
                        }
                        var stalled = new List<int>();
                        foreach (var kv in observedPending)
                            if (!cache.ContainsKey(kv.Key)) stalled.Add(kv.Key);
                        stalled.Sort();
                        for (int i = 0; i < stalled.Count; i += StalledReadRows)
                        {
                            int from = i;
                            int to = Math.Min(i + StalledReadRows, stalled.Count);
                            MainThreadDispatcher.Invoke(() =>
                            {
                                if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                                for (int j = from; j < to; j++)
                                {
                                    int idx = stalled[j];
                                    // Null when the count moved since the snapshot (binding
                                    // activity): leave the row to the reader's settle path.
                                    var row = ctx.ReadRowIfPendingStill(idx, observedPending[idx]);
                                    if (row == null) continue;
                                    cache[idx] = row;
                                    preSettled++;
                                }
                                return true;
                            });
                            if (to < stalled.Count) Thread.Sleep(SliceYieldMs);
                        }
                    }
                }
                SettleWindow(startIndex, pendingBefore, tHarvestStart, outcome);
                // The row read gets whatever the list wait and the harvest left of the overall
                // budget, so the total stays under the route's timeouts.
                int rowBudget = (int)Math.Max(1000, TableLoadBudgetMs - (Environment.TickCount64 - start));
                var reader = new StreamedTableReader<TableRow>(PageRows, RowReadyBudgetMs, rowBudget);
                int stallIdx = -1, stallPending = -1, stallSettled = 0;
                long stallSince = 0;
                var ops = new StreamedTableReader<TableRow>.Ops
                {
                    ScrollAndCheckReady = idx =>
                    {
                        if (cache.ContainsKey(idx)) return 0;
                        var observed = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            int pending = ctx.ScrollToRowPending(idx);
                            return (pending, window: pending < 0 ? null
                                : ctx.PendingCountsInWindow(idx, harvestEnd, cache.ContainsKey),
                                at: Environment.TickCount64);
                        });
                        // The scroll just realized a fresh window; harvest its already-bound rows so
                        // the rows after this one become cache hits. Sliced, and in its own dispatches
                        // rather than the scroll's: doing it inside the scroll was the second stall
                        // site (a measured 438ms hold turning to page 2). Landing a frame later also
                        // gives the freshly realized rows a frame to bind, so more of them are ready.
                        if (observed.pending >= 0)
                        {
                            var harvest = HarvestFrom(idx);
                            SettleWindow(idx, observed.window, observed.at, harvest);
                            if (harvest != HarvestSlicer.Outcome.Complete)
                                ServerText.LogBackground($"[StreamedTableLoader] post-scroll harvest at {idx} ended {harvest}");
                            if (cache.ContainsKey(idx)) return 0;
                        }
                        return observed.pending;
                    },
                    // The same stall rule as the batch pre-settle, per row, for rows the reader had
                    // to scroll in: a pending count that has sat still for StuckStillMs is a row the
                    // game is not binding, and the poll answers ready with the row's honest current
                    // state instead of letting it wait out the full row budget. Closure state is
                    // only written inside dispatches while this thread blocks on them.
                    PollAndRead = idx => MainThreadDispatcher.Invoke(() =>
                    {
                        if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                        var polled = ctx.CountAndReadRow(idx);
                        if (polled.pending <= 0) return polled;
                        if (idx != stallIdx || polled.pending != stallPending)
                        {
                            stallIdx = idx;
                            stallPending = polled.pending;
                            stallSince = Environment.TickCount64;
                            return polled;
                        }
                        if (Environment.TickCount64 - stallSince < StuckStillMs) return polled;
                        var row = ctx.ReadRow(idx);
                        if (row == null) return polled;
                        stallSettled++;
                        return (0, row);
                    }),
                    ReadRow = idx => cache.TryGetValue(idx, out var cached) ? cached : MainThreadDispatcher.Invoke(() =>
                    {
                        if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                        ServerText.LogBackground($"[StreamedTableLoader] row {idx} never ready: {ctx.DescribePendingCells(idx)}");
                        return ctx.ReadRow(idx);
                    }),
                    Sleep = () => Thread.Sleep(RowReadyStepMs),
                    NowMs = () => Environment.TickCount64,
                };
                long tReadStart = Environment.TickCount64;
                var result = reader.Read(ops, startIndex);
                long tEnd = Environment.TickCount64;
                ServerText.LogBackground(
                    $"[StreamedTableLoader] timing: countPoll={tCount}ms (total={totalRows}) " +
                    $"harvest={tReadStart - tHarvestStart}ms (cached={cache.Count}, preSettled={preSettled}) " +
                    $"read={tEnd - tReadStart}ms (rows={result.Rows.Count}, stallSettled={stallSettled}) all={tEnd - start}ms");
                // An unreadable count means the list itself was not realized yet (GetVirtualisedList
                // returned null), so ScrollToRowPending answered -1 for the very first row and the
                // reader stopped with an empty result — indistinguishable here from a table that is
                // genuinely empty, which reports a count of 0 rather than -1. Rendering that as an
                // empty tbody fabricates an empty table and reads as "this team has no players".
                // Leave Rows null: the renderer degrades to "table not loaded; refresh" and the
                // compose is marked provisional so the settle tick re-reads it (see BuildBody).
                if (totalRows < 0 && result.Rows.Count == 0)
                {
                    ServerText.LogBackground("[StreamedTableLoader] list not realized; rendering table as unloaded");
                    item.Rows = null;
                    item.Page = 0;
                    item.PageCount = 1;
                    item.Truncated = false;
                    return;
                }
                item.Rows = result.Rows;
                item.Page = page;
                item.PageCount = pageCount;
                // Paged: the pager states where the player is, so "more rows exist" is not an
                // omission and must not read as one. Unpaged (no total): keep the old meaning, where
                // a further row past the cap IS the omission the note reports.
                item.Truncated = totalRows < 0 ? result.Truncated : result.Incomplete;
            }
        }
        catch (ContextMenuGuard.AbortException)
        {
            // Deliberate stand-down, not a failure: a context menu opened, and finishing the sweep
            // would scroll its anchor row out and kill it. The empty table renders behind the modal
            // menu; the post-close re-render reads it fresh.
            ServerText.LogBackground("[StreamedTableLoader] read paused: context menu active");
            item.Rows = new List<TableRow>();
            item.Truncated = false;
        }
        catch (Exception ex)
        {
            // Background thread: Log.* would hard-crash off the main thread, so route to the
            // background log. A failed load is not an empty table — null Rows renders the honest
            // "table not loaded" marker and flags the compose provisional, so the settle tick
            // re-reads instead of leaving the player on a fabricated empty table.
            ServerText.LogBackground($"[StreamedTableLoader] Table load failed, rendering as unloaded: {ex.Message}");
            item.Rows = null;
            item.Truncated = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Contexts;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the Add/Remove Leagues grid's streamed rows on the request (background) thread, filling a
/// walked <see cref="ScreenWalker.LeagueTableItem"/>'s Rows/Truncated. Same protocol as
/// <see cref="FixtureScheduleLoader"/>: the list virtualises and its cells stream-bind a frame or
/// two after a row realizes, so the read drives <see cref="StreamedTableReader{TRow}"/> — every game
/// touch marshalled through <see cref="MainThreadDispatcher"/>, a real sleep between polls so frames
/// advance.
/// </summary>
internal static class LeagueTableLoader
{
    private const int RowReadyStepMs = 16;
    private const int RowReadyBudgetMs = 700;
    private const int TableLoadBudgetMs = 8000;
    private const int MaxRows = 200;

    // The read drives the list's shared scroll position across frames; two concurrent reads would
    // scroll each other's rows out mid-read.
    private static readonly object _readLock = new object();

    internal static void Load(ScreenWalker.LeagueTableItem item)
    {
        try
        {
            lock (_readLock)
            {
                var reader = new StreamedTableReader<LeagueTableContext.Row>(
                    MaxRows, RowReadyBudgetMs, TableLoadBudgetMs);
                // Batch cache: rows that are already realized and fully bound are read in ONE
                // dispatch up front (and re-harvested after each real scroll realizes a fresh
                // window), instead of two dispatches — two frames — per row. See StreamedTableLoader,
                // whose wiring this mirrors: a cached row reports pending 0 ("already ready at
                // scroll") and ReadRow serves it without touching the game, so the reader's
                // waiting/budget/truncation logic is unchanged and only engages for rows that
                // genuinely need realizing or are still binding. Written only inside main-thread
                // dispatches while this thread blocks on them, so there is no concurrent access.
                var cache = new Dictionary<int, LeagueTableContext.Row>();
                void Harvest(int fromIdx)
                {
                    foreach (var kv in item.Context.SweepReadyRows(fromIdx, cache.ContainsKey))
                        cache[kv.Key] = kv.Value;
                }
                MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    Harvest(0);
                    return true;
                });
                var ops = new StreamedTableReader<LeagueTableContext.Row>.Ops
                {
                    ScrollAndCheckReady = idx => cache.ContainsKey(idx) ? 0 : MainThreadDispatcher.Invoke(() =>
                    {
                        if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                        int pending = item.Context.ScrollToRowPending(idx);
                        // The scroll just realized a fresh window; harvest its already-bound rows in
                        // this same dispatch so the rows after this one become cache hits.
                        if (pending >= 0) Harvest(idx + 1);
                        return pending;
                    }),
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
                item.Rows = result.Rows;
                item.Truncated = result.Truncated;
            }
        }
        catch (ContextMenuGuard.AbortException)
        {
            ServerText.LogBackground("[LeagueTableLoader] read paused: context menu active");
            item.Rows = new List<LeagueTableContext.Row>();
            item.LoadFailed = true;
        }
        catch (Exception ex)
        {
            // Background thread: Log.* would hard-crash off the main thread. An honest failure marker
            // renders in place of the rows (see HtmlRenderer.RenderLeagueTable).
            ServerText.LogBackground($"[LeagueTableLoader] League table load failed: {ex.Message}");
            item.Rows = new List<LeagueTableContext.Row>();
            item.LoadFailed = true;
        }
    }
}

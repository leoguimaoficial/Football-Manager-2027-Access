using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Spends a streamed table's batch harvest over several main-thread dispatches instead of one.
///
/// The harvest reads every already-bound row of the realized window in a single dispatch. That is
/// the right amount of WORK — it is what saves two dispatches (two frames) per row — but it is the
/// wrong amount of work for one FRAME: a 51-row squad table held the Unity main thread for 250-563ms
/// at a time, in bursts of ten or more, which is seconds of frozen game per screen visit. This cuts
/// the same work into slices, each its own dispatch, with the frame handed back in between.
///
/// Pure control logic: the dispatch, the wait and the clock are injected, so this runs and is unit
/// tested without the game (the same arrangement as StreamedTableReader).
///
/// Two things make the slicing safe rather than merely smaller:
///
/// <b>The terminator is "the scan finished", not "no new rows".</b> A slice stopped by its row cap
/// can still have returned rows, and a slice that returns none may simply have scanned a window that
/// is all still binding. Only <see cref="Slice.HitCap"/> distinguishes them. Reading "no new rows" as
/// "window exhausted" would abandon rows to the per-row scroll path, which scrolls the player's live
/// view — the thing the batch harvest exists to avoid.
///
/// <b>Slices are sized by row count, adaptively.</b> A deadline handed to the slice would be a lie:
/// it is computed here, on a background thread, and by the time the slice reaches the main thread
/// most of it has been spent queueing, so every slice would degenerate to one row. Instead each
/// slice reports the time it actually spent, and the next cap is set from the per-row cost it
/// implies. That also self-tunes across tables — a fixture row and a squad row differ by an order of
/// magnitude — where one fixed row count could only suit one of them.
/// </summary>
internal sealed class HarvestSlicer
{
    /// <summary>What one dispatched slice did.</summary>
    internal readonly struct Slice
    {
        public Slice(int newRows, long elapsedMs, int rowCount, bool hitCap)
        {
            NewRows = newRows;
            ElapsedMs = elapsedMs;
            RowCount = rowCount;
            HitCap = hitCap;
        }

        /// <summary>Rows harvested by this slice.</summary>
        public int NewRows { get; }
        /// <summary>Main-thread time the slice spent, measured inside the dispatch (never the round trip).</summary>
        public long ElapsedMs { get; }
        /// <summary>The table's row count as this slice saw it; negative when the list is unreadable.</summary>
        public int RowCount { get; }
        /// <summary>The row cap cut the scan short, so the window still holds rows worth another slice.</summary>
        public bool HitCap { get; }
    }

    internal enum Outcome
    {
        /// <summary>A slice scanned the whole window without hitting its cap: nothing further to harvest.</summary>
        Complete,
        /// <summary>The list stopped being readable mid-harvest (panel teardown, tab switch).</summary>
        ListUnreadable,
        /// <summary>Rows were inserted or removed under the harvest, so what it holds is a mosaic.</summary>
        RowCountChanged,
        /// <summary>The overall budget ran out with the window not fully harvested.</summary>
        BudgetSpent,
    }

    internal sealed class Ops
    {
        /// <summary>Dispatch one slice, capped at this many rows, and report what it did.</summary>
        public Func<int, Slice> RunSlice;
        /// <summary>Hand the frame back between slices.</summary>
        public Action Yield;
        /// <summary>Monotonic clock in milliseconds, for the overall budget only.</summary>
        public Func<long> NowMs;
    }

    private readonly int _targetSliceMs;
    private readonly int _firstRows;
    private readonly int _minRows;
    private readonly int _maxRows;
    private readonly long _budgetMs;

    public HarvestSlicer(int targetSliceMs, int firstRows, int minRows, int maxRows, long budgetMs)
    {
        _targetSliceMs = targetSliceMs;
        _firstRows = firstRows;
        _minRows = minRows;
        _maxRows = maxRows;
        _budgetMs = budgetMs;
    }

    public Outcome Run(Ops ops)
    {
        long start = ops.NowMs();
        int rows = _firstRows;
        int expectedCount = -1;
        while (true)
        {
            var slice = ops.RunSlice(rows);
            // Read before the harvest is judged complete: an empty slice from a list that has gone
            // away is indistinguishable from an exhausted window, and calling that "complete" would
            // render whatever was cached as the whole table.
            if (slice.RowCount < 0) return Outcome.ListUnreadable;
            if (expectedCount < 0) expectedCount = slice.RowCount;
            else if (slice.RowCount != expectedCount) return Outcome.RowCountChanged;
            if (!slice.HitCap) return Outcome.Complete;
            if (ops.NowMs() - start >= _budgetMs) return Outcome.BudgetSpent;
            rows = NextRows(rows, slice.ElapsedMs);
            ops.Yield();
        }
    }

    /// <summary>
    /// The cap for the next slice: the last slice's own per-row cost, applied to the target. Not a
    /// feedback gain that needs damping — <c>rows * target / elapsed</c> IS <c>target / perRowCost</c>,
    /// the count that fits the target directly.
    /// </summary>
    internal int NextRows(int rows, long elapsedMs)
    {
        long spent = Math.Max(1, elapsedMs);
        long scaled = (long)rows * _targetSliceMs / spent;
        if (scaled < _minRows) return _minRows;
        if (scaled > _maxRows) return _maxRows;
        return (int)scaled;
    }
}

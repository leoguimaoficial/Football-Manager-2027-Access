using System;
using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Drives reading a streamed (virtualised, async-bound) table one row at a time, waiting across
/// frames for each row's cells to finish binding before reading it. Pure control logic: the IL2CPP
/// operations (scroll a row in, check whether its cells are bound, read it) plus the wait and clock
/// are injected, so this runs and is unit-tested without the game. The Plugin wires the operations to
/// <c>MainThreadDispatcher.Invoke</c> + <c>Thread.Sleep</c> (see <c>StreamedTableLoader</c>): a
/// background thread issuing those short Invokes with a sleep between them lets game frames advance,
/// which is what gives the streamed data time to bind.
///
/// Readiness is the binding system's own signal, not a guess from the rendered output. When a row is
/// realized, its cell elements exist immediately but their values bind over the next few frames; each
/// bound <c>SIText</c> carries a <c>Binder</c> whose <c>IsReady</c> flips true the frame its value is
/// resolved and applied (measured to land exactly when the slowest cell — e.g. a loan expiry date —
/// finally appears; the realize-level flags like <c>IsBound</c> fire frames too early). So a row is
/// complete when it has <b>zero not-ready cells</b>. The reader scrolls a row in and polls that
/// pending count until it reaches zero, then reads once. No text-count heuristic, no settle timer, no
/// per-table full-count to infer: an empty-valued cell reports ready just like a filled one, so a row
/// with genuinely fewer values needs no special case. The per-row budget is only a floor for a binder
/// that never reports ready, so a stuck row is read at its honest current state rather than hanging.
/// </summary>
public sealed class StreamedTableReader<TRow>
{
    public sealed class Ops
    {
        public Func<int, int> ScrollAndCheckReady;            // scroll the row in; not-ready cell count (0 = ready), or < 0 if it does not exist
        // Re-check the realized row's not-ready count; the poll that observes 0 also reads the row's
        // cells in the same step. A poll observing pending > 0 may return default for the row — the
        // reader only keeps the row from a pending-0 poll, so building a model the caller would
        // discard is wasted work for the implementation.
        public Func<int, (int pending, TRow row)> PollAndRead;
        public Func<int, TRow> ReadRow;                       // read without re-checking; used for an already-ready row, the never-polled fallback, and the budget-break fallback
        public Action Sleep;                                  // wait one poll interval (lets game frames advance)
        public Func<long> NowMs;                              // monotonic clock in milliseconds
    }

    public sealed class Result
    {
        public readonly List<TRow> Rows;
        // The read stopped short of the rows it was asked for because a budget ran out: this window
        // is INCOMPLETE, and the rows missing from it are missing from the surface with no other
        // signal. Distinct from MoreRows, which is the ordinary "the table continues past here".
        public readonly bool Incomplete;
        // A row exists immediately past the window that was read. For an unpaged caller that means
        // the table was cut off; for a paged one it just means there is a next page.
        public readonly bool MoreRows;
        // Either kind of omission. Callers that present one "not everything is here" note keep
        // reading this; a paged caller wants the two apart (a next page is not an omission).
        public bool Truncated => Incomplete || MoreRows;
        public Result(List<TRow> rows, bool incomplete, bool moreRows)
        {
            Rows = rows;
            Incomplete = incomplete;
            MoreRows = moreRows;
        }
    }

    private readonly int _maxRows;
    private readonly long _rowBudgetMs;
    private readonly long _totalBudgetMs;

    public StreamedTableReader(int maxRows, long rowBudgetMs, long totalBudgetMs)
    {
        _maxRows = maxRows;
        _rowBudgetMs = rowBudgetMs;
        _totalBudgetMs = totalBudgetMs;
    }

    /// <summary>
    /// Reads up to the configured row cap starting at <paramref name="startIndex"/> (0 for an
    /// unpaged read, page*size for a paged one). The returned rows are the data indices
    /// [startIndex, startIndex + cap) that exist; a start past the last row yields none.
    /// </summary>
    public Result Read(Ops ops, int startIndex = 0)
    {
        var rows = new List<TRow>();
        long start = ops.NowMs();
        bool incomplete = false;
        int idx = startIndex;
        int end = startIndex + _maxRows;
        for (; idx < end; idx++)
        {
            if (ops.NowMs() - start >= _totalBudgetMs) { incomplete = true; break; }
            int pending0 = ops.ScrollAndCheckReady(idx);
            if (pending0 < 0) return new Result(rows, false, false); // past the last row
            var (row, captured) = SettleRow(ops, idx, pending0, start);
            // Not captured means the row was already ready at scroll, a budget was spent before a
            // poll, or a budget cut a stuck row off: all three read once, at the row's honest
            // current state.
            rows.Add(captured ? row : ops.ReadRow(idx));
        }
        // Stopped at the cap (not a missing row, not the budget): more rows only if a further row
        // actually exists, so a window ending exactly on the last row does not falsely claim more.
        bool moreRows = !incomplete && idx == end && ops.ScrollAndCheckReady(end) >= 0;
        return new Result(rows, incomplete, moreRows);
    }

    // Wait until the row has zero not-ready cells, then keep the row that pending-0 poll captured —
    // no separate read Invoke. A budget break (a binder that never reports ready) returns
    // captured=false WITHOUT a row: the caller reads the stuck row once via ReadRow at its honest
    // current state, so a poll observing pending > 0 never needs to have built a row model at all.
    private (TRow row, bool captured) SettleRow(Ops ops, int idx, int pending0, long start)
    {
        if (pending0 == 0) return (default, false); // already fully bound at scroll; caller reads it once
        long rowStart = ops.NowMs();
        TRow row = default;
        int pending = pending0;
        while (pending != 0)
        {
            long now = ops.NowMs();
            if (now - rowStart >= _rowBudgetMs) return (default, false);
            if (now - start >= _totalBudgetMs) return (default, false);
            ops.Sleep();
            (pending, row) = ops.PollAndRead(idx);
        }
        return (row, true); // the poll that observed pending 0 captured the row in its ready state
    }
}

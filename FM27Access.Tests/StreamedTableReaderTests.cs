using System.Collections.Generic;
using System.Linq;
using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class StreamedTableReaderTests
{
    // A scripted harness: a virtual clock that Sleep advances, ScrollAndCheckReady gated by a row
    // count, and per-row "not-ready cell" sequences. Each row's sequence is consumed first by
    // ScrollAndCheckReady (the initial pending count) then by PollAndRead (subsequent polls), repeating
    // the last value once exhausted. A row with no script is already ready (pending 0). PollAndRead
    // returns the row (its index) only on a pending-0 poll — a not-ready poll returns the NotReadyRow
    // sentinel instead, mirroring the live contexts (which skip the model build while cells bind);
    // a reader that wrongly kept such a row poisons the result and fails the equality asserts.
    private sealed class Harness
    {
        public const int NotReadyRow = -999;
        public long Clock;
        public int StepMs = 16;
        public int ScrollCostMs;                 // wall time charged by each scroll (timing edge tests)
        public int AvailableRows = int.MaxValue;
        public readonly Dictionary<int, Queue<int>> Pending = new();
        public readonly List<int> Polled = new();
        public readonly List<int> FallbackReads = new();
        public readonly List<int> Scrolled = new();

        public StreamedTableReader<int>.Ops Ops() => new()
        {
            NowMs = () => Clock,
            Sleep = () => Clock += StepMs,
            ScrollAndCheckReady = idx =>
            {
                Scrolled.Add(idx);
                Clock += ScrollCostMs;
                return idx < AvailableRows ? NextPending(idx) : -1;
            },
            PollAndRead = idx =>
            {
                Polled.Add(idx);
                int pending = NextPending(idx);
                return (pending, pending == 0 ? idx : NotReadyRow);
            },
            ReadRow = idx => { FallbackReads.Add(idx); return idx; },
        };

        private readonly Dictionary<int, int> _lastByIdx = new();

        private int NextPending(int idx)
        {
            // No script => already ready (0 not-ready cells). Otherwise take the next value, repeating
            // the last once the script is exhausted (the row has stopped changing).
            if (!Pending.TryGetValue(idx, out var q)) return 0;
            if (q.Count > 0) _lastByIdx[idx] = q.Dequeue();
            return _lastByIdx[idx];
        }
    }

    [Fact]
    public void ReadsAllRows_WhenEachIsAlreadyReady()
    {
        var h = new Harness { AvailableRows = 3 };
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(new[] { 0, 1, 2 }, result.Rows.ToArray());
        Assert.False(result.Truncated);
        // Ready at scroll (pending 0) => read once via ReadRow without a settle poll.
        Assert.Empty(h.Polled);
        Assert.Equal(new[] { 0, 1, 2 }, h.FallbackReads.ToArray());
    }

    [Fact]
    public void WaitsForRow_UntilNoCellIsNotReady()
    {
        var h = new Harness { AvailableRows = 1 };
        // Row 0 binds over frames: 3 not-ready cells at scroll, then 2, 1, 0 over polls. It must wait
        // until every cell reports ready (pending 0) before reading.
        h.Pending[0] = new Queue<int>(new[] { 3, 2, 1, 0 });
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 1000, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(new[] { 0 }, result.Rows.ToArray());
        // Polls at pending 2, 1, 0 — three polls, stopping the frame the last cell reports ready.
        Assert.Equal(3, h.Polled.Count);
        Assert.Empty(h.FallbackReads); // the poll that hit pending 0 captured the row
    }

    [Fact]
    public void DoesNotReadEarly_WhileALaggardCellIsStillNotReady()
    {
        var h = new Harness { AvailableRows = 1 };
        // The laggard cell (a loan expiry date) stays not-ready for several polls while the rest are
        // done: pending holds at 1 before dropping to 0. The read must not fire until it reaches 0 —
        // reading at pending 1 would drop that cell (the bug this whole signal fixes).
        h.Pending[0] = new Queue<int>(new[] { 5, 1, 1, 1, 0, 0 });
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 1000, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(new[] { 0 }, result.Rows.ToArray());
        // Polls at 1,1,1,0 — it held through the not-ready plateau and only stopped at 0 (four polls).
        Assert.Equal(4, h.Polled.Count);
        Assert.Empty(h.FallbackReads);
    }

    [Fact]
    public void StragglerReadAfterRowBudget_WhenACellNeverReportsReady()
    {
        var h = new Harness { AvailableRows = 1, StepMs = 33 };
        // A cell never reports ready (pending stuck at 1); the row budget must cut it off and read the
        // honest current state rather than hang.
        h.Pending[0] = new Queue<int>(new[] { 1 });
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 100, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(new[] { 0 }, result.Rows.ToArray());
        Assert.True(h.Clock >= 100);
        Assert.True(h.Clock < 200);
        // The budget break reads the stuck row's honest current state once via the fallback — the
        // not-ready polls carried no row (see NotReadyRow), and the row must still not be dropped.
        Assert.Equal(new[] { 0 }, h.FallbackReads.ToArray());
    }

    [Fact]
    public void NotReadyPollsCarryNoRow_AndNeverLeakIntoTheResult()
    {
        var h = new Harness { AvailableRows = 2 };
        h.Pending[0] = new Queue<int>(new[] { 3, 2, 1, 0 }); // binds over polls: captured by the pending-0 poll
        h.Pending[1] = new Queue<int>(new[] { 1 });          // never binds: budget break, fallback read
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 100, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        // Both rows present, neither is the NotReadyRow sentinel a not-ready poll returned.
        Assert.Equal(new[] { 0, 1 }, result.Rows.ToArray());
        Assert.Equal(new[] { 1 }, h.FallbackReads.ToArray()); // only the stuck row needed the fallback
    }

    [Fact]
    public void ReadsViaFallback_WhenBudgetSpentDuringScroll()
    {
        var h = new Harness { AvailableRows = 1, ScrollCostMs = 500 };
        h.Pending[0] = new Queue<int>(new[] { 5 }); // not ready, but the scroll exhausts the budget first
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 300, totalBudgetMs: 300);

        var result = reader.Read(h.Ops());

        // The scroll alone exhausts the total budget, so settle never polls; the row is still read once
        // via the fallback rather than dropped.
        Assert.Equal(new[] { 0 }, result.Rows.ToArray());
        Assert.Empty(h.Polled);
        Assert.Equal(new[] { 0 }, h.FallbackReads.ToArray());
    }

    [Fact]
    public void TruncatesEarly_WhenOverallBudgetExceeded()
    {
        var h = new Harness { AvailableRows = 1000, StepMs = 50 };
        // Every row takes a few not-ready polls, so the overall budget trips before maxRows.
        for (int i = 0; i < 1000; i++)
            h.Pending[i] = new Queue<int>(new[] { 5, 4, 3, 2, 1, 0 });
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 1000, totalBudgetMs: 300);

        var result = reader.Read(h.Ops());

        Assert.True(result.Truncated);
        Assert.True(result.Rows.Count < 50);
    }

    [Fact]
    public void NotTruncated_WhenExactlyMaxRowsAndNoMore()
    {
        var h = new Harness { AvailableRows = 50 };
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(50, result.Rows.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Truncated_WhenMoreRowsExistBeyondMax()
    {
        var h = new Harness { AvailableRows = 200 };
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(50, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void StopsAtFirstMissingRow_WithoutTruncation()
    {
        var h = new Harness { AvailableRows = 5 };
        var reader = new StreamedTableReader<int>(maxRows: 50, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops());

        Assert.Equal(5, result.Rows.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ReadsTheWindowStartingAtStartIndex()
    {
        var h = new Harness { AvailableRows = 250 };
        var reader = new StreamedTableReader<int>(maxRows: 100, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops(), startIndex: 100);

        // The rows ARE their data indices in the harness, so this asserts the window is 100..199 —
        // page 2 of a 100-row page size — not 100 rows counted from the top.
        Assert.Equal(100, result.Rows.Count);
        Assert.Equal(100, result.Rows[0]);
        Assert.Equal(199, result.Rows[99]);
    }

    [Fact]
    public void MoreRows_ButNotIncomplete_WhenAFurtherPageExists()
    {
        var h = new Harness { AvailableRows = 250 };
        var reader = new StreamedTableReader<int>(maxRows: 100, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops(), startIndex: 100);

        // A next page is not a gap in this page: the pager reports it, the truncation note must not.
        Assert.True(result.MoreRows);
        Assert.False(result.Incomplete);
    }

    [Fact]
    public void LastPage_ReportsNeitherMoreRowsNorIncomplete()
    {
        var h = new Harness { AvailableRows = 250 };
        var reader = new StreamedTableReader<int>(maxRows: 100, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops(), startIndex: 200);

        Assert.Equal(50, result.Rows.Count);
        Assert.False(result.MoreRows);
        Assert.False(result.Incomplete);
    }

    [Fact]
    public void StartPastTheEnd_ReadsNothingAndClaimsNoOmission()
    {
        var h = new Harness { AvailableRows = 250 };
        var reader = new StreamedTableReader<int>(maxRows: 100, rowBudgetMs: 300, totalBudgetMs: 8000);

        var result = reader.Read(h.Ops(), startIndex: 300);

        Assert.Empty(result.Rows);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Incomplete_WhenTheBudgetTripsPartWayThroughAPage()
    {
        var h = new Harness { AvailableRows = 1000, StepMs = 50 };
        for (int i = 0; i < 1000; i++)
            h.Pending[i] = new Queue<int>(new[] { 5, 4, 3, 2, 1, 0 });
        var reader = new StreamedTableReader<int>(maxRows: 100, rowBudgetMs: 1000, totalBudgetMs: 300);

        var result = reader.Read(h.Ops(), startIndex: 100);

        // A page the loader could not finish IS an omission with no other signal, so it stays flagged.
        Assert.True(result.Incomplete);
        Assert.True(result.Truncated);
        Assert.True(result.Rows.Count < 100);
    }
}

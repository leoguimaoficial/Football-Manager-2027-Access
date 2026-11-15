using System;
using System.Collections.Generic;
using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

public class HarvestSlicerTests
{
    // A scripted table: RowsReady rows waiting to be harvested, each costing PerRowMs of main-thread
    // time. Each slice takes up to its cap, reports the time that implies, and says whether the cap
    // cut it short. The clock only advances by the work — the driver's own yield is free here, so a
    // budget test measures work rather than harness bookkeeping.
    private sealed class Harness
    {
        public long Clock;
        public int RowsReady = 50;
        public int PerRowMs = 10;
        public int RowCount = 50;
        public int Harvested;
        public int Yields;
        public readonly List<int> Caps = new();
        public Action<int> BeforeSlice;          // fires with the slice number, to script mid-harvest change

        public HarvestSlicer.Ops Ops() => new()
        {
            NowMs = () => Clock,
            Yield = () => Yields++,
            RunSlice = cap =>
            {
                Caps.Add(cap);
                BeforeSlice?.Invoke(Caps.Count);
                if (RowCount < 0) return new HarvestSlicer.Slice(0, 0, RowCount, false);
                int take = Math.Min(cap, RowsReady);
                RowsReady -= take;
                Harvested += take;
                Clock += (long)take * PerRowMs;
                // The cap cut the scan short only if it was reached AND rows remain: a slice that
                // took exactly the last rows has still scanned the whole window.
                return new HarvestSlicer.Slice(take, (long)take * PerRowMs, RowCount,
                                               take == cap && RowsReady > 0);
            },
        };
    }

    private static HarvestSlicer Slicer(int targetMs = 30, int firstRows = 4, int min = 1,
                                        int max = 50, long budgetMs = 4000)
        => new(targetMs, firstRows, min, max, budgetMs);

    [Fact]
    public void HarvestsEveryReadyRow_AcrossSeveralSlices()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };
        var outcome = Slicer().Run(h.Ops());

        Assert.Equal(HarvestSlicer.Outcome.Complete, outcome);
        Assert.Equal(50, h.Harvested);
        Assert.True(h.Caps.Count > 1, "a 500ms harvest must not be spent in one slice");
    }

    [Fact]
    public void NoSliceSpendsFarMoreThanTheTarget_OnceItHasMeasuredTheRowCost()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };
        Slicer(targetMs: 30).Run(h.Ops());

        // The first cap is the configured starting guess; every later one is sized from measurement,
        // and 30ms of 10ms rows is 3. Without this the slicing would be nominal — a cap that keeps
        // growing puts the stall straight back.
        Assert.All(h.Caps.GetRange(1, h.Caps.Count - 1), cap => Assert.Equal(3, cap));
    }

    [Fact]
    public void FirstSliceUsesTheConfiguredStartingCap()
    {
        var h = new Harness();
        Slicer(firstRows: 4).Run(h.Ops());

        // The first slice has no measurement to size itself from; if it inherited the max instead,
        // the very first dispatch would be the whole stall.
        Assert.Equal(4, h.Caps[0]);
    }

    [Fact]
    public void KeepsGoing_WhenACappedSliceHarvestedNothing()
    {
        // The terminator must be "the scan finished", never "no new rows". A window that is all
        // still binding harvests nothing while there is plenty left to do; stopping here would drop
        // the rest onto the per-row scroll path, which scrolls the player's live view.
        int slices = 0;
        var ops = new HarvestSlicer.Ops
        {
            NowMs = () => 0,
            Yield = () => { },
            RunSlice = _ =>
            {
                slices++;
                return slices < 3
                    ? new HarvestSlicer.Slice(0, 5, 20, hitCap: true)
                    : new HarvestSlicer.Slice(0, 5, 20, hitCap: false);
            },
        };

        Assert.Equal(HarvestSlicer.Outcome.Complete, new HarvestSlicer(30, 4, 1, 50, 4000).Run(ops));
        Assert.Equal(3, slices);
    }

    [Fact]
    public void StopsAsSoonAsAScanCompletes_EvenThoughItReturnedRows()
    {
        var h = new Harness { RowsReady = 3, PerRowMs = 1 };
        var outcome = Slicer(firstRows: 4).Run(h.Ops());

        // 3 rows against a cap of 4: the scan reached the end of the window, so there is nothing to
        // come back for and a second dispatch would be a wasted frame.
        Assert.Equal(HarvestSlicer.Outcome.Complete, outcome);
        Assert.Single(h.Caps);
    }

    [Fact]
    public void ReportsListUnreadable_WhenTheListVanishesMidHarvest()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };
        h.BeforeSlice = n => { if (n == 3) h.RowCount = -1; };

        // Rendering the rows gathered so far would present part of a table that no longer exists as
        // the whole of it.
        Assert.Equal(HarvestSlicer.Outcome.ListUnreadable, Slicer().Run(h.Ops()));
    }

    [Fact]
    public void ReportsRowCountChanged_WhenRowsAreInsertedMidHarvest()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };
        h.BeforeSlice = n => { if (n == 3) h.RowCount = 51; };

        // A data index no longer means the entity it meant when the earlier slices read it, so the
        // cache is a mosaic of two tables.
        Assert.Equal(HarvestSlicer.Outcome.RowCountChanged, Slicer().Run(h.Ops()));
    }

    [Fact]
    public void ReportsBudgetSpent_WhenTheHarvestOutlastsItsBudget()
    {
        var h = new Harness { RowsReady = 10000, PerRowMs = 10 };

        Assert.Equal(HarvestSlicer.Outcome.BudgetSpent, Slicer(budgetMs: 200).Run(h.Ops()));
        Assert.True(h.Harvested > 0, "the budget bounds the harvest, it does not cancel it");
    }

    [Fact]
    public void RunsOneSlice_EvenWhenTheBudgetIsAlreadySpent()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };

        // The budget is checked after a slice, never before, so a harvest always makes progress.
        // Checked first, a busy frame could leave a table permanently unread.
        Assert.Equal(HarvestSlicer.Outcome.BudgetSpent, Slicer(budgetMs: 0).Run(h.Ops()));
        Assert.Single(h.Caps);
        Assert.Equal(4, h.Harvested);
    }

    [Fact]
    public void YieldsBetweenSlices_AndNotAfterTheLast()
    {
        var h = new Harness { RowsReady = 50, PerRowMs = 10 };
        Slicer().Run(h.Ops());

        // Without a yield between them the queued slices are drained back-to-back inside one frame
        // and the harvest is one long stall again, just spelled differently.
        Assert.Equal(h.Caps.Count - 1, h.Yields);
    }

    [Theory]
    [InlineData(4, 40, 3)]     // 10ms rows against a 30ms target
    [InlineData(4, 4, 30)]     // 1ms rows: grow, but only to what the target buys
    [InlineData(4, 400, 1)]    // 100ms rows: never below one row of progress
    [InlineData(4, 0, 50)]     // too fast to measure: grow to the cap, never divide by zero
    public void SizesTheNextSliceFromTheMeasuredPerRowCost(int rows, long elapsedMs, int expected)
        => Assert.Equal(expected, Slicer(targetMs: 30, min: 1, max: 50).NextRows(rows, elapsedMs));
}

using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class TablePagingTests
{
    [Theory]
    [InlineData(0, 1)]      // an empty table is one page (and renders no pager)
    [InlineData(1, 1)]
    [InlineData(100, 1)]    // exactly one page: the pager must not appear for a table that fits
    [InlineData(101, 2)]    // one row over is the whole reason paging exists
    [InlineData(200, 2)]
    [InlineData(201, 3)]
    public void PageCount_CountsWholePagesPlusAPartialOne(int totalRows, int expected)
    {
        Assert.Equal(expected, TablePaging.PageCount(totalRows, 100));
    }

    [Fact]
    public void PageCount_IsOne_WhenThePageSizeIsMeaningless()
    {
        // A zero/negative page size would divide by zero; one page is the safe reading and shows
        // no pager rather than an invented page count.
        Assert.Equal(1, TablePaging.PageCount(500, 0));
    }

    [Theory]
    [InlineData(-3, 5, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(4, 5, 4)]
    [InlineData(9, 5, 4)]   // the stored page outlived the rows it was chosen against
    public void Clamp_KeepsThePageInsideTheTable(int page, int pageCount, int expected)
    {
        Assert.Equal(expected, TablePaging.Clamp(page, pageCount));
    }

    [Theory]
    [InlineData(0, 0, 50)]      // the French Cup fifth round: 290 ties, 50 read per page
    [InlineData(1, 50, 50)]
    [InlineData(5, 250, 40)]    // the last page is short — a full-size window would run past row 289
    public void Window_SelectsThePagesSliceAndStopsAtTheEnd(int page, int start, int count)
    {
        Assert.Equal((start, count), TablePaging.Window(290, 50, page));
    }

    [Fact]
    public void Window_ClampsAStoredPagePastTheEnd()
    {
        // The list shrank under a page the player had already chosen (a round filter, a new
        // competition). Reading from the stale offset would return no rows at all and present an
        // emptied table as the truth; the last real page is what the pager also clamps to.
        Assert.Equal((250, 40), TablePaging.Window(290, 50, 99));
    }

    [Fact]
    public void Window_IsTheWholeListWhenItFitsOnOnePage()
    {
        // The single-page case must hand back every row: this is the path a normal-sized fixtures
        // list takes, where windowing at all would silently drop ties.
        Assert.Equal((0, 12), TablePaging.Window(12, 50, 0));
        Assert.Equal((0, 50), TablePaging.Window(50, 50, 0));
    }

    [Fact]
    public void Window_IsEmptyForAnEmptyList()
    {
        // Zero rows with a non-zero page size still has to produce a legal (start, count) — a
        // negative count would throw at the caller's GetRange rather than render an empty table.
        Assert.Equal((0, 0), TablePaging.Window(0, 50, 0));
    }

    [Fact]
    public void Label_IsOneBased()
    {
        Assert.Equal("Page 1 of 5", TablePaging.Label(0, 5));
        Assert.Equal("Page 5 of 5", TablePaging.Label(4, 5));
    }
}

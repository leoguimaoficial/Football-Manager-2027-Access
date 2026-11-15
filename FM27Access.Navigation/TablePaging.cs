namespace FM27Access.Navigation;

/// <summary>
/// Page arithmetic for the HTML surface's paged tables. Pure and game-free so the page a player
/// lands on — and the "Page 3 of 7" they hear — is unit-tested rather than inferred from a live
/// read. A table whose total row count could not be read never reaches here: the caller keeps it
/// unpaged rather than guessing a page count (see <c>StreamedTableLoader</c>).
/// </summary>
public static class TablePaging
{
    /// <summary>
    /// Pages needed to hold <paramref name="totalRows"/> at <paramref name="pageSize"/> rows each.
    /// Never less than one: an empty table is page 1 of 1, which is also the case the pager
    /// declines to render at all.
    /// </summary>
    public static int PageCount(int totalRows, int pageSize)
    {
        if (pageSize <= 0 || totalRows <= pageSize) return 1;
        return (totalRows + pageSize - 1) / pageSize;
    }

    /// <summary>
    /// The nearest real page to <paramref name="page"/>. A stored page outlives the row count it
    /// was chosen against (a filter shrinks the table under the player), so every use is clamped:
    /// the last page is shown instead of an empty window past the end.
    /// </summary>
    public static int Clamp(int page, int pageCount)
    {
        if (page < 0) return 0;
        int last = pageCount - 1;
        return page > last ? last : page;
    }

    /// <summary>
    /// The slice of an already-collected list of <paramref name="totalRows"/> items belonging to
    /// <paramref name="page"/>, as a start index and a count. Used where every row is cheap to
    /// COUNT but expensive to READ (a cup round's match tiles: collecting them is a tree walk,
    /// reading one is ~38ms of team adapters, scorers and per-match controls), so the page count
    /// stays honest while only one page is ever read.
    ///
    /// The page is clamped here rather than by the caller, so a stored page that outlived a shrunk
    /// list yields the last real page instead of a window past the end. The final page is short:
    /// its count is what remains, never a full page reaching past the last row.
    /// </summary>
    public static (int Start, int Count) Window(int totalRows, int pageSize, int page)
    {
        if (totalRows <= 0) return (0, 0);
        if (pageSize <= 0 || totalRows <= pageSize) return (0, totalRows);
        int start = Clamp(page, PageCount(totalRows, pageSize)) * pageSize;
        return (start, System.Math.Min(pageSize, totalRows - start));
    }

    /// <summary>The pager's spoken and displayed position, 1-based.</summary>
    public static string Label(int page, int pageCount) => $"Page {page + 1} of {pageCount}";
}

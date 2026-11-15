using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class TablePagerHtmlTests
{
    [Fact]
    public void SinglePage_RendersNothing()
    {
        // The user-facing rule: a table that fits on one page carries no page controls at all.
        Assert.Equal("", TablePagerHtml.Build("p", "n", page: 0, pageCount: 1));
    }

    [Fact]
    public void FirstPage_DisablesPreviousOnly()
    {
        var html = TablePagerHtml.Build("pid", "nid", page: 0, pageCount: 5);

        Assert.Contains("<button type=\"button\" id=\"pid\" disabled>Previous page</button>", html);
        Assert.Contains("id=\"nid\" onclick=\"FM.tablepager.go('nid','pid')\">Next page</button>", html);
    }

    [Fact]
    public void LastPage_DisablesNextOnly()
    {
        var html = TablePagerHtml.Build("pid", "nid", page: 4, pageCount: 5);

        Assert.Contains("id=\"pid\" onclick=\"FM.tablepager.go('pid','nid')\">Previous page</button>", html);
        Assert.Contains("<button type=\"button\" id=\"nid\" disabled>Next page</button>", html);
    }

    [Fact]
    public void MiddlePage_LeavesBothLive()
    {
        var html = TablePagerHtml.Build("pid", "nid", page: 2, pageCount: 5);

        Assert.DoesNotContain("disabled", html);
    }

    [Fact]
    public void ShowsTheCurrentPositionBetweenTheButtons_AsItsOwnBlock()
    {
        var html = TablePagerHtml.Build("pid", "nid", page: 2, pageCount: 5);

        // Between the two buttons and in a block of its own: sharing a line with Previous made the
        // position read as part of that button's label, as if the button went to that page.
        Assert.Contains("Previous page</button></p><p>Page 3 of 5</p><p><button", html);
    }
}

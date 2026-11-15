using FM27Access.Navigation;

namespace FM27Access.Tests;

using Flag = StatusFlagsHtml.StatusFlag;

/// <summary>
/// The squad table's "Inf" cell. The game hides every status flag past the first behind a hover
/// popup, so the cell used to read as complete while carrying more; these pin the shape of the
/// replacement — including that it scales past the two-flag case that is all the live game happened
/// to show when it was written.
/// </summary>
public class StatusFlagsHtmlTests
{
    private static string Build(params Flag[] flags) => StatusFlagsHtml.Build(flags, null, null);

    [Fact]
    public void NoFlagsRenderEmpty()
    {
        Assert.Equal("", StatusFlagsHtml.Build(new Flag[0], null, null));
        Assert.Equal("", StatusFlagsHtml.Build(null, null, null));
    }

    [Fact]
    public void SingleFlagStaysFlat()
    {
        // A one-item list would make a screen reader announce list structure around a single value.
        var html = Build(new Flag("Injured", "Out for 9 to 10 months"));
        Assert.Equal("Injured, Out for 9 to 10 months", html);
        Assert.DoesNotContain("<ul>", html);
    }

    [Fact]
    public void TwoFlagsRenderAsAList()
    {
        var html = Build(
            new Flag("Injured", "Returning to full training tomorrow"),
            new Flag("Fitness Test", "Failed fitness test, not recommended to be selected"));

        Assert.Equal(
            "<ul><li>Injured, Returning to full training tomorrow</li>"
            + "<li>Fitness Test, Failed fitness test, not recommended to be selected</li></ul>",
            html);
    }

    [Fact]
    public void EveryFlagPastTheSecondSurvives()
    {
        // The reason this exists: no live cell has ever shown more than one hidden flag, so nothing
        // but a test proves a five-flag player is not silently truncated.
        var html = Build(
            new Flag("Injured", "r1"), new Flag("Unregistered", "r2"), new Flag("Wanted", "r3"),
            new Flag("Transfer Listed", "r4"), new Flag("Promise", "r5"));

        Assert.Equal(5, CountOccurrences(html, "<li>"));
        Assert.Contains("Transfer Listed, r4", html);
        Assert.Contains("Promise, r5", html);
        Assert.DoesNotContain("more", html); // no cap, no "+N more" summary
    }

    [Fact]
    public void ReasonRestatingTheNameIsDropped()
    {
        // The game's own data does this: "Youth Contract" has the reason "Youth Contract".
        Assert.Equal("Youth Contract", Build(new Flag("Youth Contract", "Youth Contract")));
    }

    [Fact]
    public void EmptyReasonLeavesJustTheName()
    {
        Assert.Equal("Wanted", Build(new Flag("Wanted", "")));
        Assert.Equal("Wanted", Build(new Flag("Wanted", null)));
    }

    [Fact]
    public void ReasonTextIsEscaped()
    {
        var html = Build(new Flag("Injured", "knee <b> & \"ankle\""));
        Assert.Contains("knee &lt;b&gt; &amp; &quot;ankle&quot;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void FlagNameIsEscaped()
    {
        Assert.Contains("A &amp; B", Build(new Flag("A & B", "")));
    }

    [Fact]
    public void FirstFlagKeepsItsLiveControlAndTheRestDoNot()
    {
        // The badge button is the game's own action (it opens the flag's explanation panel); the
        // hidden flags have no element in the tree, so fabricating controls for them would lie.
        var html = StatusFlagsHtml.Build(
            new[] { new Flag("Injured", "r1"), new Flag("Fitness Test", "r2") },
            "<button id=\"e1\">Injured</button>", null);

        Assert.Equal(1, CountOccurrences(html, "<button"));
        Assert.Contains("<li><button id=\"e1\">Injured</button>, r1</li>", html);
        Assert.Contains("<li>Fitness Test, r2</li>", html);
    }

    [Fact]
    public void SingleFlagWithAControlStaysFlatAndKeepsIt()
    {
        var html = StatusFlagsHtml.Build(
            new[] { new Flag("Under 19s", "Available for the Under 19s") },
            "<button id=\"e1\">Under 19s</button>", null);

        Assert.Equal("<button id=\"e1\">Under 19s</button>, Available for the Under 19s", html);
    }

    [Fact]
    public void UnreadableMarkerReplacesTheWholeCell()
    {
        // A partial list rendered beside a marker would still present an incomplete cell as though
        // the marker were incidental; the marker has to stand alone.
        var html = StatusFlagsHtml.Build(
            new[] { new Flag("Injured", "r1") }, "<button id=\"e1\">Injured</button>",
            "status flags unreadable");

        Assert.Equal("status flags unreadable", html);
        Assert.DoesNotContain("Injured", html);
        Assert.DoesNotContain("<button", html);
    }

    [Fact]
    public void MarkerTextIsEscaped()
    {
        Assert.Equal("a &amp; b", StatusFlagsHtml.Build(null, null, "a & b"));
    }

    [Fact]
    public void MissingNameStillReadsItsReasonWithoutALeadingSeparator()
    {
        // A flag whose title failed to read must not render as ", reason" — the stray comma would
        // sound like a dropped word rather than a gap.
        Assert.Equal("Out injured", Build(new Flag(null, "Out injured")));
        Assert.Equal("Out injured", Build(new Flag("", "Out injured")));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}

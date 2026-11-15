using FM27Access.Speech;

namespace FM27Access.Tests;

public class SpeechLogTests
{
    [Fact]
    public void Render_FromZero_ReturnsAllLinesAndCursor()
    {
        var log = new SpeechLog();
        log.Add("one");
        log.Add("two");

        var text = log.Render(0, out var next);

        Assert.Equal("0: one\n1: two\n", text);
        Assert.Equal(2, next);
    }

    [Fact]
    public void Render_AdvancesCursor_SoNextPollOnlyGetsNewLines()
    {
        var log = new SpeechLog();
        log.Add("one");
        log.Render(0, out var cursor);

        log.Add("two");
        var text = log.Render(cursor, out var next);

        Assert.Equal("1: two\n", text);
        Assert.Equal(2, next);
    }

    [Fact]
    public void Render_NoNewLines_IsEmptyButCursorHolds()
    {
        var log = new SpeechLog();
        log.Add("one");
        log.Render(0, out var cursor);

        var text = log.Render(cursor, out var next);

        Assert.Equal("", text);
        Assert.Equal(cursor, next);
    }

    [Fact]
    public void Add_EmptyOrNull_IsIgnored()
    {
        var log = new SpeechLog();
        log.Add("");
        log.Add(null);

        var text = log.Render(0, out var next);

        Assert.Equal("", text);
        Assert.Equal(0, next);
    }

    [Fact]
    public void Capacity_EvictsOldestAndKeepsGlobalIndicesStable()
    {
        var log = new SpeechLog();
        // Capacity is 500; push 600 so the first 100 (indices 0..99) are evicted.
        for (int i = 0; i < 600; i++)
            log.Add("line" + i);

        // A poll from before the retained window clamps forward to the oldest kept line (index 100)
        // and the indices it renders are still the original global indices, not renumbered.
        var text = log.Render(0, out var next);

        Assert.Equal(600, next);
        Assert.StartsWith("100: line100\n", text);
        Assert.EndsWith("599: line599\n", text);
        Assert.DoesNotContain("99: line99\n", text);
    }

    [Fact]
    public void Render_AfterWraparound_FromMidWindowCursor_MapsSlotsCorrectly()
    {
        var log = new SpeechLog();
        // Capacity is 500; push 600 so the retained window wraps to global indices [100, 600).
        for (int i = 0; i < 600; i++)
            log.Add("line" + i);

        // A partial read starting inside the wrapped window must map each global index to the right
        // slot (i % Capacity), not renumber or misalign — a from-zero read would not catch that.
        var text = log.Render(550, out var next);

        Assert.StartsWith("550: line550\n", text);
        Assert.EndsWith("599: line599\n", text);
        Assert.DoesNotContain("549: line549\n", text);
        Assert.Equal(600, next);
    }
}

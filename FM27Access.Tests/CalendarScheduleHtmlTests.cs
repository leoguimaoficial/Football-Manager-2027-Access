using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class CalendarScheduleHtmlTests
{
    [Fact]
    public void NativeHeadersKeepColumnKeysAndEscapeCaptions()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay("21", "", "native Monday session");
        string[] labels = { "Segunda & <Feira>", "Terça-Feira", "Quarta-Feira", "Quinta-Feira", "Sexta-Feira", "Sábado", "Domingo" };
        string html = CalendarScheduleHtml.Build(new[] { week }, labels);
        Assert.Contains("<th scope=\"col\">Segunda &amp; &lt;Feira&gt;</th>", html);
        Assert.Contains("<th scope=\"col\">Domingo</th>", html);
        Assert.DoesNotContain("<th scope=\"col\">Monday</th>", html);
        Assert.Contains("<td><div>21</div>native Monday session</td>", html);
        Assert.Equal(0, CalendarScheduleHtml.WeekdayIndex("Monday"));
    }

    [Fact]
    public void MissingNativeCaptionKeepsOnlyItsOwnInheritedFallback()
    {
        string[] labels = { "Segunda-Feira", null!, "Quarta-Feira", "", "Sexta-Feira", "Sábado", "Domingo" };
        string html = CalendarScheduleHtml.Build(new List<CalendarScheduleHtml.ScheduleWeek>(), labels);
        Assert.Contains("<th scope=\"col\">Segunda-Feira</th>", html);
        Assert.Contains("<th scope=\"col\">Tuesday</th>", html);
        Assert.Contains("<th scope=\"col\">Thursday</th>", html);
        Assert.DoesNotContain("<th scope=\"col\">Monday</th>", html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(8)]
    public void MisalignedNativeHeaderCountIsRejected(int count)
    {
        Assert.Throws<System.ArgumentException>(() => CalendarScheduleHtml.Build(
            new List<CalendarScheduleHtml.ScheduleWeek>(), new string[count]));
    }

    [Fact]
    public void WeekdayIndex_MapsMondayFirstThroughSunday()
    {
        Assert.Equal(0, CalendarScheduleHtml.WeekdayIndex("Monday"));
        Assert.Equal(5, CalendarScheduleHtml.WeekdayIndex("Saturday"));
        Assert.Equal(6, CalendarScheduleHtml.WeekdayIndex("Sunday"));
    }

    [Fact]
    public void WeekdayIndex_ReturnsMinusOneForNonWeekday()
    {
        Assert.Equal(-1, CalendarScheduleHtml.WeekdayIndex("Day"));
        Assert.Equal(-1, CalendarScheduleHtml.WeekdayIndex(""));
    }

    [Fact]
    public void Build_EmitsSevenWeekdayColumnHeaders()
    {
        string html = CalendarScheduleHtml.Build(new List<CalendarScheduleHtml.ScheduleWeek>());
        foreach (var weekday in CalendarScheduleHtml.Weekdays)
            Assert.Contains("<th scope=\"col\">" + weekday + "</th>", html);
    }

    [Fact]
    public void Build_PaddingSlotRendersEmptyCell()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        // Only Wednesday populated; the other six slots are padding.
        week.Days[2] = new CalendarScheduleHtml.ScheduleDay("1", "", "");
        string html = CalendarScheduleHtml.Build(new[] { week });

        Assert.Contains("<td></td>", html);              // a padding cell
        Assert.Contains("<td><div>1</div></td>", html);  // the one populated cell, no state, no events
    }

    [Fact]
    public void Build_AppendsStateAfterDateWhenPresent()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay("12", "current date", "");
        string html = CalendarScheduleHtml.Build(new[] { week });
        Assert.Contains("<div>12, current date</div>", html);
    }

    [Fact]
    public void Build_OmitsStateSeparatorWhenStateEmpty()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay("12", "", "");
        string html = CalendarScheduleHtml.Build(new[] { week });
        Assert.Contains("<div>12</div>", html);
        Assert.DoesNotContain("12, ", html);
    }

    [Fact]
    public void Build_InjectsEventHtmlVerbatim()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay(
            "12", "", "<button id=\"h5\" onclick=\"act('h5')\">Match v Arsenal</button>");
        string html = CalendarScheduleHtml.Build(new[] { week });
        Assert.Contains("<button id=\"h5\" onclick=\"act('h5')\">Match v Arsenal</button>", html);
    }

    [Fact]
    public void Build_EscapesDateAndStateButNotEvents()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek();
        // A crafted date/state proves escaping; the event HTML is trusted and must pass through.
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay("<b>", "a & b", "<button>ok</button>");
        string html = CalendarScheduleHtml.Build(new[] { week });

        Assert.Contains("&lt;b&gt;, a &amp; b", html);
        Assert.Contains("<button>ok</button>", html);
    }

    [Fact]
    public void Build_EmitsOneBodyRowPerWeek()
    {
        var weeks = new List<CalendarScheduleHtml.ScheduleWeek>
        {
            new CalendarScheduleHtml.ScheduleWeek(),
            new CalendarScheduleHtml.ScheduleWeek(),
            new CalendarScheduleHtml.ScheduleWeek(),
        };
        string html = CalendarScheduleHtml.Build(weeks);

        // Three week rows in the body, plus the single header row = four "<tr>" in total.
        int bodyStart = html.IndexOf("<tbody>", System.StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(html.Substring(bodyStart), "<tr>"));
        Assert.Equal(4, CountOccurrences(html, "<tr>"));
    }

    [Fact]
    public void Build_OmitsFocusColumnWhenNoWeekHasOne()
    {
        // The General and Fixtures tabs draw no focus cards. They must keep the plain seven-column
        // table rather than gaining a column of empty header cells.
        var week = new CalendarScheduleHtml.ScheduleWeek();
        week.Days[0] = new CalendarScheduleHtml.ScheduleDay("12", "", "");
        string html = CalendarScheduleHtml.Build(new[] { week });

        Assert.DoesNotContain(CalendarScheduleHtml.FocusColumnHeader, html);
        Assert.DoesNotContain("scope=\"row\"", html);
        Assert.Equal(7, CountOccurrences(html, "<th scope=\"col\">"));
    }

    [Fact]
    public void Build_EmitsFocusColumnHeaderBeforeTheWeekdays()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek { FocusHtml = "<button>Set Piece</button>" };
        string html = CalendarScheduleHtml.Build(new[] { week });

        Assert.Equal(8, CountOccurrences(html, "<th scope=\"col\">"));
        int focus = html.IndexOf(CalendarScheduleHtml.FocusColumnHeader, System.StringComparison.Ordinal);
        int monday = html.IndexOf(">Monday<", System.StringComparison.Ordinal);
        Assert.True(focus >= 0 && focus < monday, "the focus column must lead the weekday columns");
    }

    [Fact]
    public void Build_InjectsFocusHtmlVerbatimAsTheRowHeader()
    {
        var week = new CalendarScheduleHtml.ScheduleWeek
        {
            FocusHtml = "<button id=\"h9\" onclick=\"act('h9')\">Set Piece</button>",
        };
        string html = CalendarScheduleHtml.Build(new[] { week });

        Assert.Contains("<th scope=\"row\"><button id=\"h9\" onclick=\"act('h9')\">Set Piece</button></th>", html);
    }

    [Fact]
    public void Build_KeepsRowHeaderCellForAWeekWithoutAFocus()
    {
        // Once any week has a focus the column exists, so a week lacking one still needs its header
        // cell or that row's seven days shift one column left of their weekday headers.
        var withFocus = new CalendarScheduleHtml.ScheduleWeek { FocusHtml = "<button>Defense</button>" };
        var withoutFocus = new CalendarScheduleHtml.ScheduleWeek();
        withoutFocus.Days[0] = new CalendarScheduleHtml.ScheduleDay("8", "", "");
        string html = CalendarScheduleHtml.Build(new[] { withFocus, withoutFocus });

        Assert.Equal(2, CountOccurrences(html, "<th scope=\"row\">"));
        Assert.Contains("<th scope=\"row\"></th>", html);
    }

    [Fact]
    public void Build_FocusRowStillCarriesAllSevenWeekdaySlots()
    {
        // Regression guard on cell alignment: the header cell must be additional to the seven days,
        // never in place of the first one.
        var week = new CalendarScheduleHtml.ScheduleWeek { FocusHtml = "<button>Defense</button>" };
        week.Days[3] = new CalendarScheduleHtml.ScheduleDay("4", "", "");
        string html = CalendarScheduleHtml.Build(new[] { week });

        int bodyStart = html.IndexOf("<tbody>", System.StringComparison.Ordinal);
        string body = html.Substring(bodyStart);
        Assert.Equal(6, CountOccurrences(body, "<td></td>"));       // the six padding days
        Assert.Contains("<td><div>4</div></td>", body);             // the populated day
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}

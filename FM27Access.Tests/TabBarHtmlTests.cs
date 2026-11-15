using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class TabBarHtmlTests
{
    private static IReadOnlyList<TabBarHtml.TabEntry> Tabs(params (string id, string label, bool sel)[] tabs)
    {
        var list = new List<TabBarHtml.TabEntry>();
        foreach (var t in tabs)
            list.Add(new TabBarHtml.TabEntry(t.id, t.label, t.sel));
        return list;
    }

    [Fact]
    public void WrapsTabsInTablist()
    {
        // The tablist role is what makes a screen reader treat the group as tabs rather than
        // loose buttons. (The region landmark around it is added by the Plugin's renderer.)
        var html = TabBarHtml.Build(Tabs(("t0", "Combined", false), ("t1", "In Possession", true)));
        Assert.StartsWith("<div role=\"tablist\">", html);
        Assert.EndsWith("</div>", html);
    }

    [Fact]
    public void SelectedTab_GetsAriaSelectedTrue_OthersFalse()
    {
        // The whole point of the feature: the selected tab must read as selected, and only it.
        var html = TabBarHtml.Build(Tabs(
            ("t0", "Combined", false),
            ("t1", "In Possession", true),
            ("t2", "Out of Possession", false)));

        Assert.Contains("role=\"tab\" id=\"t1\" aria-selected=\"true\"", html);
        Assert.Contains("role=\"tab\" id=\"t0\" aria-selected=\"false\"", html);
        Assert.Contains("role=\"tab\" id=\"t2\" aria-selected=\"false\"", html);
    }

    [Fact]
    public void EachTab_ActivatesByItsId()
    {
        // Activation goes through the shared act() path; a wrong id would activate the wrong tab.
        var html = TabBarHtml.Build(Tabs(("t0", "Combined", false), ("t1", "Both", true)));
        Assert.Contains("onclick=\"act('t0')\"", html);
        Assert.Contains("onclick=\"act('t1')\"", html);
    }

    [Fact]
    public void DisabledTab_RendersDisabled_AndCannotBeActivated()
    {
        // The Transfer Offer panel's Loan Offer tab when the board blocks loans: the game
        // ignores the click, so an enabled-looking tab is a lie about what will happen.
        var html = TabBarHtml.Build(new List<TabBarHtml.TabEntry>
        {
            new("t0", "Transfer Offer", true),
            new("t1", "Loan Offer", false, disabled: true),
        });

        Assert.Contains("id=\"t1\" aria-selected=\"false\" disabled aria-disabled=\"true\"", html);
        Assert.DoesNotContain("act('t1')", html);
        Assert.Contains("onclick=\"act('t0')\"", html);
    }

    [Fact]
    public void Labels_AreHtmlEscaped()
    {
        // Tab labels are game text; an unescaped angle bracket or quote would break the markup.
        var html = TabBarHtml.Build(Tabs(("t0", "A & B <x> \"q\"", false)));
        Assert.Contains("A &amp; B &lt;x&gt; &quot;q&quot;", html);
        Assert.DoesNotContain("<x>", html);
    }
}

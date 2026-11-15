using FM27Access.Navigation;

namespace FM27Access.Tests;

public class DropdownHtmlTests
{
    [Fact]
    public void OrdinaryNativeDisabledLeafAndFolderRemainReadableAndUnavailable()
    {
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Leaf", "Folder", "Available" },
            new[] { false, true, true }, -1, 3, null, new[] { true, true, false });
        Assert.Contains("<option value=\"0\" disabled>Leaf</option>", html);
        Assert.Contains("<option value=\"1\" disabled data-sub=\"1\">Folder, submenu</option>", html);
        Assert.Contains("<option value=\"2\" data-sub=\"1\">Available, submenu</option>", html);
    }

    [Fact]
    public void DisabledCurrentValueRetainsNativeSelection()
    {
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Current", "Other" },
            new[] { false, false }, 0, 2, null, new[] { true, false });
        Assert.Contains("value=\"0\" disabled selected>Current", html);
        Assert.DoesNotContain("value=\"1\" disabled", html);
    }

    [Fact]
    public void DisabledContinuationUsesAbsoluteIndicesAndKeepsLoadMoreEnabled()
    {
        var html = DropdownHtml.BuildMoreItems(new[] { "Old", "First", "Second" },
            1, new[] { false, false, true }, -1, 4, null, new[] { true, false, true });
        Assert.DoesNotContain(">Old", html);
        Assert.Contains("value=\"1\">First", html);
        Assert.Contains("value=\"2\" disabled data-sub=\"1\">Second", html);
        Assert.Contains("<option data-more=\"1\">Load more", html);
    }

    [Fact]
    public void DisabledToggleRetainsItsCheckedState()
    {
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Rule" }, new[] { false },
            0, 1, new[] { true }, new[] { true });
        Assert.Contains("disabled data-toggle=\"1\">Rule, checked", html);
        Assert.DoesNotContain(" selected", html);
    }

    [Fact]
    public void LegacyBuilderAndExplicitEnabledStatesHaveIdenticalMarkup()
    {
        var labels = new[] { "A", "B" };
        var children = new[] { false, true };
        Assert.Equal(DropdownHtml.BuildOptionsFragment(labels, children, 0, 3),
            DropdownHtml.BuildOptionsFragment(labels, children, 0, 3, null, new[] { false, false }));
        Assert.Equal(DropdownHtml.BuildMoreItems(labels, 1, children, -1, 3),
            DropdownHtml.BuildMoreItems(labels, 1, children, -1, 3, null, new[] { false, false }));
    }

    [Fact]
    public void Labels_AreHtmlEscaped()
    {
        // Unknown markup is stripped (matching the speech surface), and the surviving
        // text is escaped so game content can't break out of the option markup.
        var html = DropdownHtml.BuildOptionsFragment(new[] { "A & B <x> \"q\"" }, new[] { false }, -1, 1);
        Assert.Contains("A &amp; B &quot;q&quot;", html);
        Assert.DoesNotContain("<x>", html);
    }

    [Fact]
    public void StripRichText_RemovesUnityMarkup()
    {
        // Real markup observed in player-name dropdowns; left in, the screen reader
        // would read the tags aloud.
        var s = DropdownHtml.StripRichText("<color=\"#FFFFFF\"><link=\"0\"><style=\"Norml\">O. MacDonald</style></link></color>");
        Assert.Equal("O. MacDonald", s);
    }

    [Fact]
    public void StripRichText_LeavesUnknownAngleBracketsAlone()
    {
        // Only known rich-text tags are stripped; game text containing a literal
        // comparison must survive intact (and be escaped downstream).
        Assert.Equal("a <b 5 stays", DropdownHtml.StripRichText("a <b 5 stays"));
    }

    [Fact]
    public void CurrentItem_GetsSelectedAttribute()
    {
        // Without it the select would announce and commit-resolve the wrong value.
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Attacking", "Balanced", "Defensive" },
            new[] { false, false, false }, 1, 3);
        Assert.Contains("<option value=\"1\" selected>Balanced</option>", html);
        Assert.Contains("<option value=\"0\">Attacking</option>", html);
    }

    [Fact]
    public void SubmenuItem_IsMarkedWithDataSub()
    {
        // Missing data-sub would commit a selection instead of chaining the submenu.
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Leaf", "Stats" }, new[] { false, true }, -1, 2);
        Assert.Contains("<option value=\"1\" data-sub=\"1\">Stats, submenu</option>", html);
        Assert.Contains("<option value=\"0\">Leaf</option>", html);
    }

    [Fact]
    public void Truncated_RendersLoadMoreOptionWithCounts()
    {
        var html = DropdownHtml.BuildOptionsFragment(new[] { "a", "b" }, new[] { false, false }, -1, 50);
        Assert.Contains("<option data-more=\"1\">Load more, showing 2 of 50</option>", html);
    }

    [Fact]
    public void NotTruncated_HasNoLoadMore()
    {
        var html = DropdownHtml.BuildOptionsFragment(new[] { "a", "b" }, new[] { false, false }, -1, 2);
        Assert.DoesNotContain("data-more", html);
    }

    [Fact]
    public void MoreItems_UseAbsoluteIndices()
    {
        // The continuation renders from startIndex; relative values here would make
        // the server act on a different option than the one shown.
        var labels = new[] { "a", "b", "c", "d" };
        var html = DropdownHtml.BuildMoreItems(labels, 2, new[] { false, false, false, false }, -1, 4);
        Assert.Contains("<option value=\"2\">c</option>", html);
        Assert.Contains("<option value=\"3\">d</option>", html);
        Assert.DoesNotContain("value=\"0\"", html);
        Assert.DoesNotContain("data-more", html);
    }

    [Fact]
    public void PlayerPanel_InfoIndicatorIsTextLine_SublistIsSelect()
    {
        // Status indicators (body language, condition) render as plain text; selectable
        // sub-menus render as their own select boxes tagged with the card id and the root
        // index they expand.
        var html = DropdownHtml.BuildPlayerPanel("card9",
            new[] { "Excellent", "In possession role" }, new[] { false, true }, new[] { true, false });
        Assert.Contains("<p>Excellent</p>", html);
        Assert.Contains("data-player-sub=\"1\"", html);
        Assert.Contains("data-card=\"card9\"", html);
        Assert.Contains("data-index=\"1\"", html);
        Assert.Contains("<option selected>In possession role</option>", html);
        // The info row must not become a select or a button.
        Assert.DoesNotContain("Excellent</option>", html);
        Assert.DoesNotContain("<button", html);
    }

    [Fact]
    public void PlayerPanel_DisabledSubmenuIsNotOpenable()
    {
        // A greyed-out submenu (e.g. an individual touchline shout on cooldown after a team shout)
        // must render as a disabled select with no data-player-sub, so the client cannot open the
        // game's disabled menu (which would NRE in BindChildRoot). The other shout stays openable.
        var html = DropdownHtml.BuildPlayerPanel("card9",
            new[] { "Encourage shout", "In possession role" },
            new[] { true, true }, new[] { false, false }, new[] { true, false });

        // The disabled choice: a disabled select, no data-player-sub / data-index handle.
        Assert.Contains("<select disabled aria-label=\"Encourage shout\">", html);
        Assert.DoesNotContain("data-index=\"0\"", html);
        // The enabled choice still opens normally.
        Assert.Contains("data-player-sub=\"1\"", html);
        Assert.Contains("data-index=\"1\"", html);
    }

    [Fact]
    public void PlayerPanel_ActionableLeafIsButton()
    {
        // A leaf that is not an info indicator (the player's name, Player Instructions) is an
        // action: a button that commits the root selection at its choice index. Rendering it
        // as plain text (the old behaviour) left it dead on the surface.
        var html = DropdownHtml.BuildPlayerPanel("c",
            new[] { "MacDonald", "Player Instructions" }, new[] { false, false }, new[] { false, false });
        Assert.Contains("<button type=\"button\" data-player-leaf=\"1\" onclick=\"fmpselect(0)\">MacDonald</button>", html);
        Assert.Contains("onclick=\"fmpselect(1)\">Player Instructions</button>", html);
    }

    [Fact]
    public void SearchListbox_UsesFilteredIndicesAndEscapes()
    {
        // data-index indexes the filtered read (what ConfirmSelection expects), each row is a
        // listbox option, and game text is rich-text-stripped then escaped so a player name can't
        // break out of the element.
        var html = DropdownHtml.BuildSearchListbox(new[] { "Arsenal", "A & \"B\" <x>" });
        Assert.Contains("<li role=\"option\" data-index=\"0\">Arsenal</li>", html);
        Assert.Contains("<li role=\"option\" data-index=\"1\">A &amp; &quot;B&quot;</li>", html);
        Assert.DoesNotContain("<x>", html);
    }

    [Fact]
    public void SearchListbox_EmptyListIsEmptyString()
    {
        // No results must produce no options (the session reports "no results" as status text
        // instead); a stray option would be a selectable phantom.
        Assert.Equal("", DropdownHtml.BuildSearchListbox(System.Array.Empty<string>()));
    }

    [Fact]
    public void PlayerPanel_EscapesLabels()
    {
        // Player names reach the sub-select's option and aria-label; unescaped, a name with
        // markup could break out of the attribute or element.
        var html = DropdownHtml.BuildPlayerPanel("c",
            new[] { "Sub: A & \"B\" <x>" }, new[] { true }, new[] { false });
        Assert.Contains("A &amp; &quot;B&quot;", html);
        Assert.DoesNotContain("<x>", html);
    }

    [Fact]
    public void ToggleList_OptionsCarryCheckedState()
    {
        // A multi-select filter (the staff search's exclude rules): the caption only says HOW MANY
        // rules are on, so without per-option state the player cannot tell WHICH.
        var html = DropdownHtml.BuildOptionsFragment(
            new[] { "First Team", "Reserves", "Youth Team" },
            new[] { false, false, false }, -1, 3,
            new[] { false, false, true });
        Assert.Contains(">First Team, unchecked</option>", html);
        Assert.Contains(">Youth Team, checked</option>", html);
    }

    [Fact]
    public void ToggleList_NeverMarksAnOptionSelected()
    {
        // The game leaves SelectedIndex at -1 on a toggle list; a stray "selected" would announce a
        // current value the control does not have and mis-resolve the commit.
        var html = DropdownHtml.BuildOptionsFragment(
            new[] { "First Team", "Reserves" }, new[] { false, false }, 1, 2,
            new[] { true, false });
        Assert.DoesNotContain("selected", html);
        Assert.Contains("data-toggle=\"1\"", html);
    }

    [Fact]
    public void OrdinaryDropdown_HasNoToggleWording()
    {
        // Null state means "not a toggle list": a select-one dropdown must keep its single
        // selected option and gain no checkbox wording.
        var html = DropdownHtml.BuildOptionsFragment(new[] { "Attacking", "Balanced" },
            new[] { false, false }, 0, 2);
        Assert.Contains("<option value=\"0\" selected>Attacking</option>", html);
        Assert.DoesNotContain("checked", html);
        Assert.DoesNotContain("data-toggle", html);
    }

    [Fact]
    public void ToggleList_MoreItemsKeepState()
    {
        // The continuation fragment renders from an offset; its state must stay index-aligned with
        // the full choices list, not restart at zero.
        var html = DropdownHtml.BuildMoreItems(
            new[] { "a", "b", "c" }, 2, new[] { false, false, false }, -1, 3,
            new[] { false, false, true });
        Assert.Contains("<option value=\"2\" data-toggle=\"1\">c, checked</option>", html);
        Assert.DoesNotContain(">a", html);
    }
}

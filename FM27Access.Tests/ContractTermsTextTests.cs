using FM27Access.Navigation;

namespace FM27Access.Tests;

public class ContractTermsTextTests
{
    // A value-named choice dropdown whose SelectedIndex tracks reports "value, index of count";
    // prefixing the value again would double it.
    [Fact]
    public void ComposeValueOption_StateLeadingWithValue_PassesStateThrough()
    {
        Assert.Equal("Immediate - 7/7/2025, 1 of 2",
            ContractTermsText.ComposeValueOption("Immediate - 7/7/2025", "Immediate - 7/7/2025, 1 of 2"));
    }

    // A value-named dropdown whose SelectedIndex does not track (-1) reports an empty state; the
    // displayed value must still reach the page once the table relabels the control by its term.
    [Fact]
    public void ComposeValueOption_EmptyState_UsesValue()
    {
        Assert.Equal("Emergency Backup", ContractTermsText.ComposeValueOption("Emergency Backup", ""));
        Assert.Equal("Emergency Backup", ContractTermsText.ComposeValueOption("Emergency Backup", null));
    }

    // An index-only state ("2 of 4") gains the displayed value in front of it.
    [Fact]
    public void ComposeValueOption_IndexOnlyState_PrefixesValue()
    {
        Assert.Equal("10%, 2 of 4", ContractTermsText.ComposeValueOption("10%", "2 of 4"));
    }

    [Fact]
    public void ComposeValueOption_NoValue_KeepsState()
    {
        Assert.Equal("2 of 4", ContractTermsText.ComposeValueOption(null, "2 of 4"));
        Assert.Equal("", ContractTermsText.ComposeValueOption("", ""));
    }

    // Promises phase: title, club and player all present.
    [Fact]
    public void ComposeBlockHeading_AllParts()
    {
        Assert.Equal("Promises: Chelsea (Teddy Sharman-Lowe)",
            ContractTermsText.ComposeBlockHeading("Promises", "Chelsea", "(Teddy Sharman-Lowe)"));
    }

    // Offer phase: the title slot holds the offer-view dropdown, so there is no title text.
    [Fact]
    public void ComposeBlockHeading_NoTitle_UsesClubAndPlayer()
    {
        Assert.Equal("Chelsea (Mike Penders)",
            ContractTermsText.ComposeBlockHeading(null, "Chelsea", "(Mike Penders)"));
    }

    [Fact]
    public void ComposeBlockHeading_TitleOnly()
    {
        Assert.Equal("Promises", ContractTermsText.ComposeBlockHeading("Promises", null, null));
    }

    // Nothing readable inserts nothing (the caller keeps the raw stops).
    [Fact]
    public void ComposeBlockHeading_NothingReadable_ReturnsNull()
    {
        Assert.Null(ContractTermsText.ComposeBlockHeading(null, "", null));
    }
}

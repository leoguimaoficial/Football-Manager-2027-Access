namespace FM27Access.Navigation;

/// <summary>
/// Pure text composition for the contract negotiation terms table (HTML surface). The table's
/// cell values and section heading are assembled from live element reads by the Plugin walker;
/// the string rules live here, game-free, so the test project covers them.
/// </summary>
public static class ContractTermsText
{
    /// <summary>
    /// The option text for a term's value dropdown. A value-named dropdown (its accessible name
    /// IS its displayed value) can report an empty or index-only state; the displayed value must
    /// still reach the page once the table relabels the control by its term. State strings that
    /// already lead with the value are passed through so the value is never doubled.
    /// </summary>
    public static string ComposeValueOption(string valueLabel, string state)
    {
        bool hasValue = !string.IsNullOrEmpty(valueLabel);
        bool hasState = !string.IsNullOrEmpty(state);
        if (!hasValue) return hasState ? state : "";
        if (!hasState) return valueLabel;
        if (state.StartsWith(valueLabel, System.StringComparison.Ordinal)) return state;
        return valueLabel + ", " + state;
    }

    /// <summary>
    /// The negotiation block's heading: the block title when present ("Promises"), the club and
    /// the game's parenthesised player name ("Promises: Chelsea (Teddy Sharman-Lowe)"). The offer
    /// phase has no static title (the title slot holds the offer-view dropdown), so the heading is
    /// just club and player. All parts optional; null when nothing is readable.
    /// </summary>
    public static string ComposeBlockHeading(string title, string club, string player)
    {
        string subject = JoinNonEmpty(club, player, " ");
        if (string.IsNullOrEmpty(title)) return string.IsNullOrEmpty(subject) ? null : subject;
        return string.IsNullOrEmpty(subject) ? title : title + ": " + subject;
    }

    private static string JoinNonEmpty(string a, string b, string sep)
    {
        if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? "" : b;
        return string.IsNullOrEmpty(b) ? a : a + sep + b;
    }
}

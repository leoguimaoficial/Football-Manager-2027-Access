namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Layout profile for a single panel type. Defines rules that adjust how
/// navigation stops are presented to the player.
/// </summary>
public class PanelProfile
{
    /// <summary>
    /// The panel's PanelID.name (e.g. "PlayerReport", "SquadScreen").
    /// This is the stable code identifier, not the translated title.
    /// </summary>
    public string PanelName { get; set; }

    public List<HeadingRule> Headings { get; set; } = new();
    public List<RenameRule> Renames { get; set; } = new();
    public List<HideRule> Hides { get; set; } = new();

    public bool IsHeading(ElementMatchInfo info)
    {
        for (int i = 0; i < Headings.Count; i++)
        {
            if (Headings[i].Match.Matches(info))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The heading level of the first matching heading rule, or 0 when no rule matches.
    /// Levels shape the HTML surface's outline (level 2 renders h3, 3+ renders h4);
    /// the speech surface's H-jump treats every heading alike via IsHeading.
    /// </summary>
    public int TryGetHeadingLevel(ElementMatchInfo info)
    {
        for (int i = 0; i < Headings.Count; i++)
        {
            if (Headings[i].Match.Matches(info))
                return Headings[i].Level;
        }
        return 0;
    }

    public string TryGetRename(ElementMatchInfo info)
    {
        for (int i = 0; i < Renames.Count; i++)
        {
            if (Renames[i].Match.Matches(info))
                return Renames[i].Label;
        }
        return null;
    }

    public bool IsHidden(ElementMatchInfo info)
    {
        for (int i = 0; i < Hides.Count; i++)
        {
            if (Hides[i].Match.Matches(info))
                return true;
        }
        return false;
    }
}

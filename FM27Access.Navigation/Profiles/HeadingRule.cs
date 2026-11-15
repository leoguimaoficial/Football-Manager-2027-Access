namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Marks a matched navigation stop as a heading.
/// Headings are structural landmarks the player can jump between with H/Shift+H.
/// </summary>
public class HeadingRule
{
    public StopMatcher Match { get; set; }
    public int Level { get; set; } = 1;
}

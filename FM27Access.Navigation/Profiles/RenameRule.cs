namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Replaces the label portion of a matched stop's announcement.
/// The role and state still come live from the adapter.
/// </summary>
public class RenameRule
{
    public StopMatcher Match { get; set; }
    public string Label { get; set; }
}

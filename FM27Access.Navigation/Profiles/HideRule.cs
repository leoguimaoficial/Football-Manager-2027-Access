namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Removes a matched navigation stop from the stop list entirely.
/// The element remains in the game's UI but the player cannot land on it.
/// </summary>
public class HideRule
{
    public StopMatcher Match { get; set; }
}

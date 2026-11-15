namespace FM27Access.Plugin.Server;

/// <summary>
/// Distinguishes inactive navigation-model placeholders from real destinations whose label could
/// not be read. The game reserves empty, condition-off rows for squads and competitions that may
/// become available later; those are not accessibility gaps and must not become tree items.
/// </summary>
internal static class NavigatorEntryPolicy
{
    internal static bool IsInactivePlaceholder(bool conditionMet, string label)
        => !conditionMet && string.IsNullOrWhiteSpace(label);
}

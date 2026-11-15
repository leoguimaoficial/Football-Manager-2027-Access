using FM27Access.Speech;

namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Holds all loaded panel profiles and tracks which profile is active for the current panel.
/// </summary>
public static class ProfileRegistry
{
    private static readonly Dictionary<string, PanelProfile> _profiles = new(StringComparer.Ordinal);
    private static PanelProfile _activeProfile;

    public static void Register(PanelProfile profile)
    {
        _profiles[profile.PanelName] = profile;
        Log.Info($"[ProfileRegistry] Registered profile for panel \"{profile.PanelName}\" ({profile.Headings.Count} heading, {profile.Renames.Count} rename, {profile.Hides.Count} hide rules)");
    }

    public static void ActivateForPanel(string panelName)
    {
        _profiles.TryGetValue(panelName, out _activeProfile);

        // Debug, not Info: the multi-panel HTML body build re-activates each panel's profile every
        // /body render (several per build), so at Info this trace floods the log. Genuine navigation
        // is still traceable here at Debug when needed.
        if (_activeProfile != null)
            Log.Debug($"[ProfileRegistry] Activated profile for \"{panelName}\"");
    }

    public static void ClearActiveProfile()
    {
        _activeProfile = null;
    }

    public static PanelProfile GetActiveProfile() => _activeProfile;

    public static bool IsHeading(ElementMatchInfo info)
    {
        return _activeProfile != null && _activeProfile.IsHeading(info);
    }

    public static int GetHeadingLevel(ElementMatchInfo info)
    {
        return _activeProfile?.TryGetHeadingLevel(info) ?? 0;
    }

    public static string TryGetRename(ElementMatchInfo info)
    {
        return _activeProfile?.TryGetRename(info);
    }

    public static bool IsHidden(ElementMatchInfo info)
    {
        return _activeProfile != null && _activeProfile.IsHidden(info);
    }

    internal static void Clear()
    {
        _profiles.Clear();
        _activeProfile = null;
    }
}

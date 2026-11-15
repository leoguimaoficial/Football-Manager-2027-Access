using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Composes what the cursor speaks: the display announcement (profile rename + role +
/// state, degrading to an honest marker on a failed read) and the container enter/exit
/// transition prefixes. Owns the per-kind "was inside" flags those transitions diff
/// against, keyed by kind name so they survive a host re-Initialize.
/// </summary>
internal sealed class Announcer
{
    private readonly NavigationSession _session;

    private readonly Dictionary<string, bool> _wasIn = new();

    internal Announcer(NavigationSession session) => _session = session;

    /// <summary>
    /// Whether the cursor was inside this kind as of the last announcement — the pre-move
    /// snapshot the cursor's entry wrapping consults.
    /// </summary>
    internal bool WasIn(string kindName) => _wasIn.TryGetValue(kindName, out var was) && was;

    internal string GetDisplayAnnouncement(IAccessibleElement element)
    {
        try
        {
            var rename = NavigationManager.GetRenameLabel(element);
            if (rename != null)
            {
                var state = element.GetState();
                if (string.IsNullOrEmpty(state))
                    return $"{rename}, {element.Role}";
                return $"{rename}, {element.Role}, {state}";
            }
            return element.GetAnnouncement();
        }
        catch (Exception ex)
        {
            // The element we're about to speak could not be read live. Degrade to an honest marker
            // so the player hears "couldn't read" rather than silence (a swallowed announcement is
            // indistinguishable from "nothing here") or a mod-wide crash. The deep read site already
            // logged the specific cause; this records that it reached the speech surface.
            return Degrade.Read($"announcement read failed for {SafeRole(element)}: {ex.Message}");
        }
    }

    private static string SafeRole(IAccessibleElement element)
    {
        try { return element?.Role ?? "element"; }
        catch { return "element"; }
    }

    internal void AnnounceWithTransitions(string announcement)
    {
        var host = NavigationManager.Host;
        var current = _session.Cursor.Current;

        // Update live field with the element's own announcement (before transitions prepend text)
        if (current != null)
            _session.Memory.RecordPosition(current);

        // Each transition prepends its prefix, so the spoken order is the reverse of
        // registration order — the last-registered kind's prefix is heard first.
        foreach (var kind in host.ContainerKinds)
        {
            bool isNow = kind.FindContext?.Invoke(current) != null;
            bool was = WasIn(kind.Name);
            if (isNow && !was)
            {
                announcement = $"{kind.EnterAnnouncement}. {announcement}";
                Log.Info($"[NavigationManager] Entered {kind.EnterAnnouncement}.");
            }
            else if (!isNow && was)
            {
                announcement = $"{kind.ExitAnnouncement}. {announcement}";
                Log.Info($"[NavigationManager] Left {kind.EnterAnnouncement}.");
            }
            _wasIn[kind.Name] = isNow;
        }

        host.Speak(announcement);
    }
}

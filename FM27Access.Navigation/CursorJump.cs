namespace FM27Access.Navigation;

/// <summary>
/// Cursor jump helpers that walk the element tree to find targets by role or container.
/// Called from NavigationManager; separated for clarity.
/// </summary>
public static class CursorJump
{
    public static void ToMatch(
        Func<IAccessibleElement, bool> match,
        bool forward,
        string notFoundMessage)
    {
        var current = NavigationManager.GetCurrentElement();
        if (current == null || NavigationManager.GetActivePanel() == null) return;

        var direction = forward ? NavigationDirection.Down : NavigationDirection.Up;
        var candidate = NavigationManager.FindNextStop(current, direction);

        var panelCheck = NavigationManager.Host?.Stops.IsInActivePanel;

        while (candidate != null)
        {
            if (panelCheck != null && !panelCheck(candidate))
                break;

            if (match(candidate))
            {
                NavigationManager.SetCursorAndAnnounce(candidate);
                return;
            }
            candidate = NavigationManager.FindNextStop(candidate, direction);
        }

        if (notFoundMessage != null)
            NavigationManager.Speak(notFoundMessage);
    }

    public static void ToNextContainer(
        Func<IAccessibleElement, IContainerContext> findContainerContext,
        bool forward,
        string notFoundMessage)
    {
        var current = NavigationManager.GetCurrentElement();
        if (current == null || NavigationManager.GetActivePanel() == null) return;

        var direction = forward ? NavigationDirection.Down : NavigationDirection.Up;
        var currentContext = findContainerContext(current);
        var currentScope = currentContext?.Scope;
        bool exitedCurrent = currentScope == null;
        var panelCheck = NavigationManager.Host?.Stops.IsInActivePanel;

        var candidate = NavigationManager.FindNextStop(current, direction);
        while (candidate != null)
        {
            if (panelCheck != null && !panelCheck(candidate))
                break;

            var ctx = findContainerContext(candidate);
            if (!exitedCurrent)
            {
                if (ctx == null || ctx.Scope != currentScope)
                    exitedCurrent = true;
            }

            if (exitedCurrent && ctx != null)
            {
                NavigationManager.SetCursorAndAnnounce(candidate);
                return;
            }

            candidate = NavigationManager.FindNextStop(candidate, direction);
        }

        if (notFoundMessage != null)
            NavigationManager.Speak(notFoundMessage);
    }
}

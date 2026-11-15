using FM27Access.Navigation;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Drives the mod's OWN accessibility cursor - the one the blind player moves with the arrow keys
/// and hears through Prism - as opposed to <see cref="GameDriver"/>, which drives FM26's native
/// focus. This is how an agent verifies the in-game speech surface: each verb maps to exactly the
/// <see cref="NavigationManager"/> call the matching arrow key makes in DefaultScreenHandler, so
/// the move speaks through Prism and lands in /speech just as it would for the player. The verb's
/// own response also returns the resulting cursor announcement for an immediate read.
///
/// Covers the plain (unmodified) arrow keys plus Home/End. The Ctrl/Alt table-cell and history
/// variants are not exposed - /eval remains the fallback for those. All verbs run on the main
/// thread via DevEndpoints' Invoke.
/// </summary>
internal static class CursorDriver
{
    public static string Drive(string verb)
    {
        bool inTable = NavigationManager.IsCurrentInTable();
        bool inCalendar = NavigationManager.IsCurrentInCalendar();

        switch ((verb ?? "").Trim().ToLowerInvariant())
        {
            case "up":
                if (inTable) NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Up);
                else if (inCalendar) NavigationManager.MoveCalendarDay(NavigationDirection.Up);
                else NavigationManager.MoveCursor(NavigationDirection.Up);
                break;
            case "down":
                if (inTable) NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Down);
                else if (inCalendar) NavigationManager.MoveCalendarDay(NavigationDirection.Down);
                else NavigationManager.MoveCursor(NavigationDirection.Down);
                break;
            case "left":
                if (inTable) NavigationManager.MoveTableInnerCell(NavigationDirection.Left);
                else if (inCalendar) NavigationManager.MoveCalendarInnerStop(NavigationDirection.Left);
                else NavigationManager.MoveCursor(NavigationDirection.Left);
                break;
            case "right":
                if (inTable) NavigationManager.MoveTableInnerCell(NavigationDirection.Right);
                else if (inCalendar) NavigationManager.MoveCalendarInnerStop(NavigationDirection.Right);
                else NavigationManager.MoveCursor(NavigationDirection.Right);
                break;
            case "first":
            case "home":
                NavigationManager.JumpToFirst();
                break;
            case "last":
            case "end":
                NavigationManager.JumpToLast();
                break;
            case "read":
                break; // report the current cursor without moving
            default:
                return $"[unknown verb] '{verb}' - up|down|left|right|first|last|read\n";
        }

        var cur = NavigationManager.GetCurrentElement();
        if (cur == null) return $"{verb} -> no cursor\n";
        // Strip rich-text markup so this echo matches what the player actually hears: the speech
        // pipeline cleans before Prism, so a raw GetAnnouncement here would show markup the player
        // never gets, misleading an agent reading the cursor back.
        return $"{verb} -> {cur.Role}: {ServerText.Clean(cur.GetAnnouncement())}\n";
    }
}

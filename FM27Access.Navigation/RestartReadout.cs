namespace FM27Access.Navigation;

/// <summary>
/// Turns a restart the engine reported (from the plugin's <c>MatchBallLocation.TryLatestRestart</c>) into the
/// spoken name of a dead ball for the on-demand readouts (the Alt+5 possession and location line), where geometry
/// alone cannot tell a corner from a goal kick or which team is restarting. Pure and cross-checked against the
/// line geometry so a stale event can never mislabel:
/// <list type="bullet">
///   <item>A side-line exit is always a throw-in, so a <see cref="OutOfPlayKind.Touchline"/> ball reads
///   "throw-in" even with no matching event; a throw-in event that agrees adds the team.</item>
///   <item>A <see cref="OutOfPlayKind.GoalLine"/> ball reads a corner or goal kick (by team) only when the
///   event agrees it is one; otherwise the caller keeps its geometry-only phrasing rather than guess.</item>
///   <item>A ball in play or in the net (a goal) has no restart label.</item>
/// </list>
/// </summary>
public static class RestartReadout
{
    /// <summary>
    /// The spoken restart name for a dead ball, or null when the caller should keep its geometry phrasing
    /// (an in-play/net ball, or a goal-line exit the event does not agree is a corner or goal kick).
    /// </summary>
    public static string Label(OutOfPlayKind geom, RestartKind restart, bool forRef)
    {
        switch (geom)
        {
            case OutOfPlayKind.Touchline:
                // Certain from geometry; the event only adds the team when it agrees it is a throw-in.
                return restart == RestartKind.ThrowIn ? Team(forRef, "throw-in") : "throw-in";
            case OutOfPlayKind.GoalLine:
                // Geometry cannot separate a corner from a goal kick, so only a matching event names it.
                if (restart == RestartKind.Corner) return Team(forRef, "corner");
                if (restart == RestartKind.GoalKick) return Team(forRef, "goal kick");
                return null;
            default:
                return null;   // InPlay or Net (a goal): no restart label
        }
    }

    private static string Team(bool forRef, string restart) => (forRef ? "your " : "opposition ") + restart;
}

namespace FM27Access.Plugin.Server;

/// <summary>
/// FM27 can temporarily rewind its commentary index while rebinding a period.
/// Keep the inherited event cursors untouched until live playback catches up.
/// An actual replay has its own bookmark and deliberately permits rewinds.
/// </summary>
internal sealed class MatchEventWindow
{
    private System.IntPtr match;
    internal System.IntPtr Match => match;
    internal FM27Access.Navigation.OutOfPlayOneShot Out;
    internal FM27Access.Navigation.PutInPlayOneShot PutInPlay;
    internal FM27Access.Navigation.GoalOneShot Goal;
    internal FM27Access.Navigation.DefenceOneShot Defence;
    internal FM27Access.Navigation.ShotOneShot Shot;
    internal FM27Access.Navigation.ShotOneShot Header;

    // An SSE connection survives leaving and loading careers. Its bookmarks
    // belong to the actual native match instance, not to the connection.
    internal bool Bind(System.IntPtr currentMatch)
    {
        if (match == currentMatch) return false;
        match = currentMatch;
        Out = default;
        PutInPlay = default;
        Goal = default;
        Defence = default;
        Shot = default;
        Header = default;
        return true;
    }

    internal static bool CanRead(int afterIndex, int lastIndex, bool inReplay)
        => lastIndex >= 0 && (afterIndex < 0 || inReplay || lastIndex >= afterIndex);

    // FM27 offers highlights above the inherited 4x viewing-speed floor.
    // Keep that floor at ordinary speeds; above it, allow one integer slice
    // of sampling quantization. The native fast-forward flag is a separate gate.
    internal static bool CrossedSkippedGround(int previous, int current,
        double seconds, float highlightSpeed, int minimumSkippedSlices = 0)
    {
        if (previous < 0 || current <= previous || seconds <= 0
            || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
        if (float.IsNaN(highlightSpeed) || float.IsInfinity(highlightSpeed)
            || highlightSpeed <= 0) highlightSpeed = 4;
        double speed = System.Math.Max(4, highlightSpeed);
        // At high viewing speeds the native playback stream can deliver a short
        // batch of frames together. It cannot contain an entire unobserved gap
        // if it is shorter than the native minimum between-highlight duration.
        // The independent native fast-forward flag still mutes sampled gaps.
        if (speed > 4 && minimumSkippedSlices > 0
            && (long)current - previous < minimumSkippedSlices) return false;
        double allowance = speed > 4 ? 1 : 0;
        return (long)current - previous > 4 * speed * seconds + allowance;
    }
}

using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Read-back buffer of the SSE events the server pushes to the companion (screen, restructure,
/// values, commentary, score). The live SSE stream is fire-and-forget; this records it so the dev
/// driver can read back what the player was told - in particular the match "commentary" the
/// companion announces through its aria-live region, which never goes through Prism and so is
/// invisible to /speech. Read it via /events/log?since=N, the same cursor protocol as /speech.
///
/// Wraps a <see cref="SpeechLog"/> for the ring buffer and cursor protocol (one already unit-tested
/// implementation, not a second copy). Recording is global (events come from per-connection
/// streamer threads), so consecutive identical entries are suppressed: with one companion connected
/// there are no duplicates, and with several, their near-simultaneous identical pushes collapse to
/// one. The dedupe is best-effort (an occasional racing duplicate is harmless for a debug buffer).
/// </summary>
internal static class EventLog
{
#if FM27_DEVELOPER_TOOLS
    private static readonly SpeechLog _buffer = new();
    private static string _last;
#endif

    [System.Diagnostics.Conditional("FM27_DEVELOPER_TOOLS")]
    public static void Record(string type, string data)
    {
#if FM27_DEVELOPER_TOOLS
        var line = string.IsNullOrEmpty(data) ? type : type + ": " + data;
        if (line == _last) return;
        _last = line;
        _buffer.Add(line);
#endif
    }

#if FM27_DEVELOPER_TOOLS
    public static string Render(long since, out long next) => _buffer.Render(since, out next);
#endif
}

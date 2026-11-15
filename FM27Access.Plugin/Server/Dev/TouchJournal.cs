using System;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only journal of the last-touch possession system's evidence and judgements: every detected
/// touch with its attribution geometry, every authority event or geometric witness that moved the
/// holder, and every reset and replay-pass change. Each line is stamped
/// with <see cref="Environment.TickCount64"/>, the clock the SSE loop and <see cref="PossessionTrace"/>
/// use, so a touch lines up against the commentary and the ticks the player heard around it.
///
/// Read via GET /touchlog?since=N (the /speech cursor protocol). Wraps a <see cref="SpeechLog"/> for
/// the ring and cursor (one tested implementation, not a second copy); thread-safe, since touches
/// arrive from the main thread and the authority feeds from the SSE loop.
/// </summary>
internal static class TouchJournal
{
    private static readonly SpeechLog Buffer = new();

    internal static void Record(string line) => Buffer.Add(Environment.TickCount64 + " " + line);

    internal static string Render(long since, out long next) => Buffer.Render(since, out next);

    internal static string Name(PossessionHolder h) => h switch
    {
        PossessionHolder.Reference => "ref",
        PossessionHolder.Opposition => "opp",
        _ => "none",
    };
}

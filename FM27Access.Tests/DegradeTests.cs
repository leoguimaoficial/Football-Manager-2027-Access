using System;
using System.Collections.Generic;
using FM27Access.Speech;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The "degrade in place, never lie" helper. These guard the contract every degrade site relies on:
/// the marker returned must be the honest gap word, and the reason must always be logged (a silent
/// degrade is the one thing worse than a crash).
/// </summary>
public class DegradeTests
{
    private static List<string> CaptureWarn(Action body)
    {
        var logged = new List<string>();
        var original = Log.WarnFn;
        Log.WarnFn = m => logged.Add(m);
        try { body(); }
        finally { Log.WarnFn = original; }
        return logged;
    }

    [Fact]
    public void Control_ReturnsUnknownControlMarker_AndLogsReason()
    {
        string result = null;
        var logged = CaptureWarn(() => result = Degrade.Control("dropdown reflection contract missing"));

        Assert.Equal(Degrade.UnknownControl, result);
        Assert.Contains(logged, m => m.Contains("[degrade]") && m.Contains("dropdown reflection contract missing"));
    }

    [Fact]
    public void Value_ReturnsUnavailableMarker_AndLogsReason()
    {
        string result = null;
        var logged = CaptureWarn(() => result = Degrade.Value("score read failed"));

        Assert.Equal(Degrade.Unavailable, result);
        Assert.Contains(logged, m => m.Contains("[degrade]") && m.Contains("score read failed"));
    }

    [Fact]
    public void Read_ReturnsUnreadableMarker_AndLogsReason()
    {
        string result = null;
        var logged = CaptureWarn(() => result = Degrade.Read("announcement read failed"));

        Assert.Equal(Degrade.Unreadable, result);
        Assert.Contains(logged, m => m.Contains("[degrade]") && m.Contains("announcement read failed"));
    }

    [Fact]
    public void Note_LogsReasonWithDegradePrefix()
    {
        var logged = CaptureWarn(() => Degrade.Note("an unknown widget appeared"));

        Assert.Contains(logged, m => m.Contains("[degrade]") && m.Contains("an unknown widget appeared"));
    }

    [Fact]
    public void Markers_AreDistinctNonEmptyGaps()
    {
        var markers = new[]
        {
            Degrade.UnknownControl, Degrade.Unavailable, Degrade.Unreadable,
        };

        Assert.All(markers, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(markers.Length, new HashSet<string>(markers).Count);
    }
}

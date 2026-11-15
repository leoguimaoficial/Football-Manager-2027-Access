using System;
using System.Globalization;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The arrival report: one spoken line when a calendar simulation ends, replacing the bare
/// "Simulation complete" — "Advanced 16 hours to Wed, 4/23/2025, 08:00. Overview. Unread
/// message." Composed from the title-bar clock captured when the sim started (a historical
/// record of where the player left, never re-read) against a live read at arrival, the landing
/// panel's title from the interactive set, and the stop reason named by the game's own
/// continue-state machine.
///
/// The state right after Processing is a transient bridge (Waiting, Saving — verified live),
/// so the report arms at Processing-exit and speaks when the flow settles into a non-transient
/// state; a tick-driven fallback speaks after <see cref="SettleFallbackMs"/> regardless, so a
/// flow that parks on a transient state still reports. Every clause degrades independently:
/// an unreadable clock or failed parse omits the elapsed phrase, a missing panel title omits
/// the landing clause, an unmapped state omits the reason — and a report with nothing to say
/// falls back to the plain "Simulation complete". Main thread only.
/// </summary>
internal static class ArrivalReport
{
    private const int SettleFallbackMs = 2000;
    // Speaking a beat after the flow settles lets the game's own follow-ups land first — the
    // continue button relabel ("Response Required") and an opening overlay both arrive within
    // this window, and the report reads them live at compose time.
    private const int ComposeDelayMs = 300;
    // The quiet window outlives the report by this much, so a straggler (the deferred cursor
    // restore, a late-loading tool panel) cannot cut the report off mid-sentence.
    private const int QuietTailMs = 1000;

    private static string _preClock;
    private static string _preToken;
    private static bool _pending;
    private static long _speakAtMs;
    private static long _quietTailUntilMs;
    private static FM.UI.ContinueManagerModule.ContinueFlowState _lastState;

    /// <summary>
    /// The arrival quiet window: true from the moment a report arms at Processing-exit until
    /// <see cref="QuietTailMs"/> after it speaks. Call sites that produce arrival chatter
    /// (panel entries, panel-close restores, the deferred cursor restore) mute their navigation
    /// speech while this is true — the report names the landing itself. Modal dialogs are the
    /// callers' documented exception. Fails open: a reload resets the statics, and the tick
    /// fallback guarantees a pending report always speaks.
    /// </summary>
    internal static bool QuietWindowActive
        => _pending || Environment.TickCount64 < _quietTailUntilMs;

    /// <summary>
    /// True when the report spoke while a deferred cursor restore was still pending. That
    /// restore's retries can outlive the quiet tail (they run on a frame interval), and its
    /// success announcement would cut the report off — so it resolves silently instead; the
    /// report already named a position, and an arrow key reveals the restored one. CoreLoop
    /// clears this once no restore is pending.
    /// </summary>
    internal static bool MuteActiveRestore;

    /// <summary>
    /// Same latch for the empty-panel rescan: a panel that landed with no stops announces its
    /// first stop when a later rescan finds content, and that too can outlive the quiet tail
    /// and cut the report off. Set when the report speaks with a rescan still pending; cleared
    /// by CoreLoop once no rescan is pending.
    /// </summary>
    internal static bool MuteActiveRescan;

    /// <summary>Every continue-state transition, forwarded by ContinueStatePatch.</summary>
    internal static void OnContinueState(
        FM.UI.ContinueManagerModule.ContinueFlowState prev,
        FM.UI.ContinueManagerModule.ContinueFlowState next)
    {
        _lastState = next;

        bool entering = next == FM.UI.ContinueManagerModule.ContinueFlowState.Processing
                        && prev != FM.UI.ContinueManagerModule.ContinueFlowState.Processing;
        bool exiting = prev == FM.UI.ContinueManagerModule.ContinueFlowState.Processing
                       && next != FM.UI.ContinueManagerModule.ContinueFlowState.Processing;

        if (entering)
        {
            // A sim resuming while a report is still pending (chained stops) keeps the original
            // capture, so the eventual report spans the whole chain.
            if (!_pending)
            {
                _preClock = ReadClock();
                _preToken = ReadScreenToken();
            }
            return;
        }

        if (exiting)
        {
            _pending = true;
            _speakAtMs = Environment.TickCount64 + SettleFallbackMs;
            return;
        }

        // Settled: pull the speak time in to the short compose delay (never pushing the
        // fallback deadline out).
        if (_pending && !IsTransient(next))
            _speakAtMs = Math.Min(_speakAtMs, Environment.TickCount64 + ComposeDelayMs);
    }

    /// <summary>
    /// Per-frame: speaks the armed report once its moment arrives — the compose delay after
    /// the flow settled, or the fallback deadline when it parked on a transient state.
    /// </summary>
    internal static void Tick()
    {
        if (_pending && Environment.TickCount64 >= _speakAtMs)
            Speak(_lastState);
    }

    private static bool IsTransient(FM.UI.ContinueManagerModule.ContinueFlowState s)
        => s is FM.UI.ContinueManagerModule.ContinueFlowState.Waiting
             or FM.UI.ContinueManagerModule.ContinueFlowState.Saving
             or FM.UI.ContinueManagerModule.ContinueFlowState.NeedHumanManagerAndSaving
             or FM.UI.ContinueManagerModule.ContinueFlowState.Processing;

    private static void Speak(FM.UI.ContinueManagerModule.ContinueFlowState settled)
    {
        _pending = false;
        string report;
        try
        {
            report = Compose(settled);
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] compose failed: " + ex.Message);
            report = null;
        }
        _quietTailUntilMs = Environment.TickCount64 + QuietTailMs;
        MuteActiveRestore = NavigationManager.NeedsCursorRestore;
        MuteActiveRescan = NavigationManager.NeedsRescan;
        SpeechPipeline.SpeakInterrupt(string.IsNullOrEmpty(report) ? "Simulation complete" : report);
    }

    private static string Compose(FM.UI.ContinueManagerModule.ContinueFlowState settled)
    {
        var parts = new System.Collections.Generic.List<string>(3);

        string nowClock = ReadClock();
        string elapsed = ElapsedPhrase(_preClock, nowClock);
        if (!string.IsNullOrEmpty(nowClock))
            parts.Add(elapsed != null ? $"Advanced {elapsed} to {nowClock}" : $"Now {nowClock}");

        string title = LandingTitle();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);

        string reason = ReasonPhrase(settled) ?? ContinueLabelReason();
        if (reason != null && !string.Equals(reason, title, StringComparison.OrdinalIgnoreCase))
            parts.Add(reason);

        // Where the cursor landed — the clause the muted panel-entry announcement would have
        // spoken. Read live at compose time; a cursor the deferred restore hasn't placed yet
        // contributes nothing (arrow keys reveal the position, as everywhere else).
        string position = CursorPosition();
        if (!string.IsNullOrEmpty(position)
            && !string.Equals(position, title, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(position, reason, StringComparison.OrdinalIgnoreCase))
            parts.Add(position);

        // Deterministic landing on the HTML surface, only when the sim changed screens: the
        // companion focuses the content heading of the page whose token this is.
        string nowToken = ReadScreenToken();
        if (nowToken != null && _preToken != null && nowToken != _preToken)
            SseStreamer.NotifySimArrival(nowToken);

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    // The title-bar clock beside the continue button ("Tue, 4/22/2025, 15:45") — the same text
    // Alt+\ reads. Null when absent (match HUD) or unreadable; the report omits the clause.
    private static string ReadClock()
    {
        try
        {
            var hit = ContinueButtonLocator.Find();
            if (hit == null) return null;
            var clock = ContinueSurface.ReadDayWidgetFor((SI.Bindable.Panel)hit.Value.Panel);
            return string.IsNullOrEmpty(clock) ? null : clock;
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] clock read failed: " + ex.Message);
            return null;
        }
    }

    // The continue button's live label, when the game has relabelled it with the pending step
    // ("Response Required", "Advance to Latest Results"). The game's own text, read at compose
    // time; a plain "Continue" carries no reason and contributes nothing.
    private static string ContinueLabelReason()
    {
        try
        {
            var hit = ContinueButtonLocator.Find();
            if (hit == null) return null;
            var label = DisplayText.Of(hit.Value.Element);
            if (string.IsNullOrEmpty(label)) return null;
            if (string.Equals(label, "Continue", StringComparison.OrdinalIgnoreCase)) return null;
            return label;
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] continue label read failed: " + ex.Message);
            return null;
        }
    }

    // The speech cursor's current announcement, or null when there is no cursor or the read
    // fails — the report omits the clause rather than guessing at a position.
    private static string CursorPosition()
    {
        try
        {
            return NavigationManager.GetCurrentElement()?.GetAnnouncement();
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] cursor position read failed: " + ex.Message);
            return null;
        }
    }

    private static string ReadScreenToken()
    {
        try
        {
            var set = InteractivePanels.CurrentOrNull();
            return set == null ? null : ServerText.ScreenToken(set);
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] screen token read failed: " + ex.Message);
            return null;
        }
    }

    // The content panel the HTML page leads with: the topmost non-title-bar panel of the
    // interactive set.
    private static string LandingTitle()
    {
        try
        {
            var set = InteractivePanels.CurrentOrNull();
            if (set == null) return null;
            for (int i = set.Count - 1; i >= 0; i--)
                if (!set[i].IsTitleBar && !string.IsNullOrEmpty(set[i].Title))
                    return set[i].Title;
            return null;
        }
        catch (Exception ex)
        {
            Degrade.Note("[ArrivalReport] landing title read failed: " + ex.Message);
            return null;
        }
    }

    // Rounded single-unit elapsed ("45 minutes", "16 hours", "3 days") from the two clock
    // strings, or null when either side is missing or unparseable — the report must never
    // guess at time. The widget joins weekday, date and time with ", "; the date and time
    // parts parse with the current culture (the game formats for the player's language),
    // invariant as fallback.
    private static string ElapsedPhrase(string before, string after)
    {
        if (ParseClock(before) is not DateTime b || ParseClock(after) is not DateTime a) return null;
        var span = a - b;
        if (span <= TimeSpan.Zero) return null;

        if (span.TotalMinutes < 60)
        {
            int m = Math.Max(1, (int)Math.Round(span.TotalMinutes));
            return m == 1 ? "1 minute" : $"{m} minutes";
        }
        if (span.TotalHours < 48)
        {
            int h = (int)Math.Round(span.TotalHours);
            return h == 1 ? "1 hour" : $"{h} hours";
        }
        int d = (int)Math.Round(span.TotalDays);
        return d == 1 ? "1 day" : $"{d} days";
    }

    private static DateTime? ParseClock(string clock)
    {
        if (string.IsNullOrEmpty(clock)) return null;
        var parts = clock.Split(new[] { ", " }, StringSplitOptions.None);
        // WeekDay, date, time — but tolerate a missing weekday: try the last two parts.
        if (parts.Length < 2) return null;
        string candidate = parts[^2] + " " + parts[^1];
        if (DateTime.TryParse(candidate, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;
        Degrade.Note($"[ArrivalReport] clock parse failed: \"{clock}\"");
        return null;
    }

    // The stop reason as the game's state machine names it. Unmapped states (including the
    // ordinary CanContinue stop) add no clause.
    private static string ReasonPhrase(FM.UI.ContinueManagerModule.ContinueFlowState s) => s switch
    {
        FM.UI.ContinueManagerModule.ContinueFlowState.NextUnreadMessage => "Unread message",
        FM.UI.ContinueManagerModule.ContinueFlowState.Messages => "Unread message",
        FM.UI.ContinueManagerModule.ContinueFlowState.NeedAction => "Action required",
        FM.UI.ContinueManagerModule.ContinueFlowState.SubmitTeam => "Submit your team",
        FM.UI.ContinueManagerModule.ContinueFlowState.PressConference => "Press conference",
        FM.UI.ContinueManagerModule.ContinueFlowState.TeamTalk => "Team talk",
        FM.UI.ContinueManagerModule.ContinueFlowState.FullTimeTeamTalk => "Full time team talk",
        FM.UI.ContinueManagerModule.ContinueFlowState.TunnelInterview => "Tunnel interview",
        FM.UI.ContinueManagerModule.ContinueFlowState.Tactics => "Tactics",
        FM.UI.ContinueManagerModule.ContinueFlowState.TeamNews => "Team news",
        FM.UI.ContinueManagerModule.ContinueFlowState.Results => "Results",
        FM.UI.ContinueManagerModule.ContinueFlowState.LatestScores => "Latest scores",
        FM.UI.ContinueManagerModule.ContinueFlowState.Fixtures => "Fixtures",
        FM.UI.ContinueManagerModule.ContinueFlowState.NewsAggregator => "News",
        FM.UI.ContinueManagerModule.ContinueFlowState.SquadNumbers => "Squad numbers",
        FM.UI.ContinueManagerModule.ContinueFlowState.PenaltyTakers => "Penalty takers",
        FM.UI.ContinueManagerModule.ContinueFlowState.PostMatchReaction => "Post match reaction",
        FM.UI.ContinueManagerModule.ContinueFlowState.FullTimeMatchReport => "Match report",
        FM.UI.ContinueManagerModule.ContinueFlowState.MatchLoading => "Match loading",
        FM.UI.ContinueManagerModule.ContinueFlowState.OnHoliday => "On holiday",
        _ => null,
    };
}

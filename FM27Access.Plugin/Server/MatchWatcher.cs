using System.Reflection;
using HarmonyLib;
using SI.Match;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Live match data reads for the HTML surface's SSE stream. The play-by-play line comes
/// from the UI (CommentaryBar), which is bound on the active content panel and updates
/// live. The score and clock come from the match ENGINE (FM.Match.Match.m_matchState),
/// not the scoreboard UI: the scoreboard lives on the OverviewHeader overlay panel, whose
/// bindings only re-evaluate on navigation, so its SIText.text is stale between panel
/// switches. The engine struct is always current. Team short names still come from the
/// scoreboard (they are static for the match, so staleness is irrelevant).
///
/// The scoreboard itself is a PER-COMPETITION template: the ScoreBug slot's DynamicItem
/// loads a UXML chosen by the game's ScoreboardFactory (Scoreboard.uxml,
/// Scoreboard_UEFAChampionsLeague.uxml, Scoreboard_Bundesliga.uxml, ...), and the element
/// names inside differ per variant — "scorebar" with ClubHomeTeamText/ClubAwayTeamText is
/// the UCL family; the league template names them HomeTeamText/AwayTeamText. Detection
/// therefore anchors on the stable outer ScoreBug wrapper and tolerates both name pairs.
///
/// Main thread only — callers wrap in MainThreadDispatcher.Invoke. Stateless: every call
/// re-finds the live match object and re-reads its state.
/// </summary>
internal static class MatchWatcher
{
    private static PropertyInfo _matchStateProp;

    // Match reads run on every SSE snapshot (~2 Hz). A genuinely broken scorebar or engine would
    // otherwise fire the same degrade every snapshot for the whole match, flooding the /log ring
    // buffer and evicting the diagnostic context an agent needs. Log a given degrade reason once
    // until the state changes or a clean read clears it; the returned marker is still emitted every
    // call so the page stays honest.
    private static string _lastDegradeReason;

    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    private static PropertyInfo MatchStateProp
    {
        get
        {
            if (_matchStateProp == null)
            {
                _matchStateProp = AccessTools.Property(typeof(FM.Match.Match), "m_matchState");
                if (_matchStateProp == null)
                {
                    // Caught by ComposeScoreLine, which degrades the score to "unavailable" rather
                    // than crashing — a missing engine field breaks the score read, not the mod.
                    Speech.Log.Error("[MatchWatcher] FM.Match.Match.m_matchState property not found via AccessTools.");
                    throw new System.MissingMemberException("FM.Match.Match", "m_matchState");
                }
            }
            return _matchStateProp;
        }
    }

    private static PropertyInfo _matchFrameProp;

    private static PropertyInfo MatchFrameProp
    {
        get
        {
            if (_matchFrameProp == null)
            {
                _matchFrameProp = AccessTools.Property(typeof(FM.Match.Match), "m_matchFrame");
                if (_matchFrameProp == null)
                {
                    // Caught by TryReadPeriodEndTimes, which degrades the clock to "unavailable"
                    // — a missing engine field breaks the clock read, not the mod.
                    Speech.Log.Error("[MatchWatcher] FM.Match.Match.m_matchFrame property not found via AccessTools.");
                    throw new System.MissingMemberException("FM.Match.Match", "m_matchFrame");
                }
            }
            return _matchFrameProp;
        }
    }

    /// <summary>
    /// Reads the current play-by-play line and score line. Returns false when neither a
    /// CommentaryBar nor a composable score exists (not in a match); each is null when its own
    /// source is absent (supplementary display, absence is visible on the page rather than
    /// a lie). The match Overview screen shows a scoreboard with no CommentaryBar, so the
    /// two are independent — a missing commentary bar must not mute the score ticker.
    /// </summary>
    public static bool TryRead(out string commentary, out string scoreLine)
    {
        commentary = null;

        // Off-match early-out: this runs on every SSE full tick on every screen in the game, and
        // without it each tick pays up to three full panel-tree walks looking for match UI that
        // is not there. One (frame-memoised) engine probe answers it instead; a commentary bar or
        // scoreboard never outlives the engine instance (the doc contract below already promises
        // a null score line with no engine).
        if (MatchBallLocation.CurrentMatch() == null)
        {
            scoreLine = ComposeCurrentScoreLine();   // clears the remembered team names
            return false;
        }

        var bar = FindInOpenPanels("CommentaryBar");
        scoreLine = ComposeCurrentScoreLine();
        if (bar == null && scoreLine == null) return false;

        if (bar != null)
        {
            commentary = ReadFirstText(bar);
            if (commentary == null)
            {
                // A blind player treats a silent commentary stream as "nothing is happening", so a dead
                // read during a live match must not mute. Degrade to an explicit marker — honest and
                // unmistakable for real commentary — rather than crashing the mod.
                NoteDegradeOnce("[MatchWatcher] CommentaryBar present but its text could not be read.");
                commentary = $"commentary {Speech.Degrade.Unavailable}";
            }
            else
            {
                commentary = Navigation.DropdownHtml.StripRichText(commentary).Trim();
            }
        }

        return true;
    }

    // Team short names are STATIC for a match, so the last live read this match fills the gap
    // when the scoreboard UI is transiently hidden (cutscenes, half-time transitions, panel
    // swaps) — the flicker the player reported as the score line "appearing and disappearing
    // on its own". Cleared the moment no engine instance exists and refreshed by every
    // successful live read, so a new match can never inherit the previous match's names. This
    // is the one blessed name cache (see the class doc: name staleness is irrelevant); score
    // and clock are still read live from the engine on every compose.
    private static string _lastHomeText, _lastAwayText;

    /// <summary>
    /// The current score line, composed from whatever source is available: the live scoreboard
    /// when its UI is up, else the engine plus this match's remembered team names. Null only
    /// when no match engine exists (between matches) or the names have not been readable yet
    /// this match — so mid-match the line is constant, and at match end it goes away.
    /// </summary>
    internal static string ComposeCurrentScoreLine()
    {
        // Engine first: with no match instance there is no score line by contract (see summary),
        // and checking that before FindScoreboard saves two full panel-tree walks per call on
        // every non-match screen (this also runs once per /body compose). In a match the
        // scoreboard UI stays the preferred source, exactly as before.
        var match = MatchBallLocation.CurrentMatch();
        if (match == null)
        {
            _lastHomeText = null;
            _lastAwayText = null;
            return null;
        }

        var scoreboard = FindScoreboard();
        if (scoreboard != null) return ComposeScoreLine(scoreboard);

        // A penalty shootout tears the scoreboard template out of the header entirely, so on a core
        // that has not seen this match's scoreboard yet (a mid-match hot reload) there are no
        // remembered names and the score line would vanish for the whole shootout — exactly when the
        // player most wants it. The shootout card names both teams itself; learn them from there.
        if (_lastHomeText == null || _lastAwayText == null) RememberShootoutTeams();

        if (_lastHomeText == null || _lastAwayText == null) return null;
        return ComposeFromEngine(match, _lastHomeText, _lastAwayText);
    }

    // Fills the remembered team names from the penalty shootout card's own scoreboard block, the one
    // place both names are still on screen once the header's scoreboard template is gone. Leaves them
    // untouched when the card is absent or unreadable, so the caller omits the line rather than
    // composing a score against a guess.
    private static void RememberShootoutTeams()
    {
        var block = FindInOpenPanels(PenaltyShootoutReader.ScoreboardName);
        if (block == null) return;

        var board = PenaltyShootoutReader.TryReadScoreboard(block);
        if (board == null || string.IsNullOrEmpty(board.HomeTeam) || string.IsNullOrEmpty(board.AwayTeam))
        {
            NoteDegradeOnce("[MatchWatcher] shootout card present but its team names could not be read.");
            return;
        }

        _lastHomeText = board.HomeTeam;
        _lastAwayText = board.AwayTeam;
    }

    /// <summary>
    /// Fingerprint of the match header's control availability — the flips the page renders but no
    /// other refresh channel sees: the pending-tactical-changes toast swapping back to the tactics
    /// strip at the next break in play, and the shouts dropdown re-enabling when its cooldown
    /// ends. Both change no live text (invisible to the values diff) and happen minutes after the
    /// last user action (outside any settle window), and the shape hash is deliberately blind to
    /// the enabled bit — so without this the page showed them only after a manual refresh. Null
    /// when the match header is not on screen. The SSE loop only calls this while a score line is
    /// live, so screens outside a match pay nothing; in a match it is two named-node walks per
    /// tick, deliberately that cheap. A read failure surfaces to the caller, which keeps its last
    /// token (no spurious refresh, no lost baseline).
    /// </summary>
    public static string HeaderStateToken()
    {
        var pendingSwitch = FindInOpenPanels("PendingChangesSwitch");
        if (pendingSwitch == null) return null;
        // The switch shows exactly one side: the tactics strip ("NoPendingChanges") or the
        // pending toast. The strip child reports the state directly; its absence means the
        // template changed shape, which still reads consistently tick-to-tick (no flapping).
        var strip = FindByName(pendingSwitch, "NoPendingChanges");
        bool toastUp = strip == null || !ElementBridge.IsVisible(strip);
        var shouts = FindInOpenPanels("shouts-dropdown")
            ?? FindByName(pendingSwitch, "shouts-cooldown-disable");
        bool shoutsOn = shouts != null && ElementBridge.IsVisibleInHierarchy(shouts)
            && shouts.enabledInHierarchy;
        return (toastUp ? "toast" : "strip") + (shoutsOn ? "|shouts-on" : "|shouts-off");
    }

    // The scoreboard root: the legacy "scorebar" name (UCL-family template), else the stable
    // ScoreBug wrapper when the competition template loaded inside it carries a recognised
    // team-name pair. A ScoreBug without team names (template not yet loaded) is not a
    // scoreboard — returning it would degrade every SSE tick to "score unavailable".
    private static VisualElement FindScoreboard()
    {
        var bar = FindInOpenPanels("scorebar");
        if (bar != null) return bar;
        var bug = FindInOpenPanels("ScoreBug");
        return bug != null && HasTeamNames(bug) ? bug : null;
    }

    // The team-name elements are named differently across the per-competition scoreboard
    // templates: ClubHomeTeamText/ClubAwayTeamText (UCL family) vs HomeTeamText/AwayTeamText
    // (league template). Try each known pair.
    private static VisualElement FindHomeNameEl(VisualElement root)
        => FindByName(root, "ClubHomeTeamText") ?? FindByName(root, "HomeTeamText");

    private static VisualElement FindAwayNameEl(VisualElement root)
        => FindByName(root, "ClubAwayTeamText") ?? FindByName(root, "AwayTeamText");

    // The native scorebug displays the last played frame while a different
    // passage is selected on the paused timeline. Do not mislabel this clock
    // as the selected position or derive official minutes from slice numbers.
    internal static string TryTimelinePlaybackClock()
    {
        var root=FindScoreboard();
        var clock=root==null?null:FindByName(root,"playbacktime");
        return clock!=null&&ElementBridge.IsVisibleInHierarchy(clock)?ReadText(clock):null;
    }

    /// <summary>
    /// Whether this subtree holds a recognised team-name pair — the signal that it is a match
    /// scoreboard regardless of which competition's template loaded into it.
    /// </summary>
    internal static bool HasTeamNames(VisualElement root)
        => FindHomeNameEl(root) != null && FindAwayNameEl(root) != null;

    // Composes "{home} {h} - {a} {away}, {clock}" with score and clock read live from the
    // engine. Returns null when the engine instance is absent (score is supplementary;
    // a missing line is honest). Also used by the HTML body to render the in-place
    // scoreboard stop, so the body and the SSE stream share one composition.
    internal static string ComposeScoreLine(VisualElement scoreboard)
    {
        // The scoreboard supplies only the static team short names. Score and clock are read
        // live from the engine so they tick without a panel switch.
        var home = FindHomeNameEl(scoreboard);
        var away = FindAwayNameEl(scoreboard);
        if (home == null || away == null)
        {
            // The scoreboard exists but no known team-name pair does: an unrecognised competition
            // template. A wrong/garbled score spoken as truth would be the real lie — degrade to an
            // honest "score unavailable" the player cannot mistake for a real scoreline.
            NoteDegradeOnce("[MatchWatcher] scoreboard present but team name children are missing "
                + $"(home={home != null} away={away != null}).");
            return $"score {Speech.Degrade.Unavailable}";
        }

        string homeText = ReadText(home);
        string awayText = ReadText(away);
        if (homeText == null || awayText == null)
        {
            // The team-name elements exist but their text is still empty: the scoreboard has
            // just loaded (e.g. at kick-off) and its bindings have not populated yet. Fall back
            // to this match's remembered names when a prior read captured them; at kick-off
            // (nothing remembered yet) omit the line until the names bind rather than degrade.
            // This is honest (the page shows no score) where the missing-children case above
            // means the structure changed and degrades to an unmistakable marker.
            if (_lastHomeText != null && _lastAwayText != null)
            {
                homeText = _lastHomeText;
                awayText = _lastAwayText;
            }
            else
            {
                Speech.Log.Warn("[MatchWatcher] scoreboard team names not yet populated "
                    + $"(home={homeText != null} away={awayText != null}); omitting score line.");
                return null;
            }
        }

        var match = MatchBallLocation.CurrentMatch();
        if (match == null)
        {
            // The header is on screen but the engine is gone (between matches?). Score is
            // supplementary: a missing line is honest, a stale UI read would be a lie.
            Speech.Log.Warn("[MatchWatcher] scoreboard present but no FM.Match.Match instance found; omitting score line.");
            _lastHomeText = null;
            _lastAwayText = null;
            return null;
        }

        _lastHomeText = homeText;
        _lastAwayText = awayText;
        return ComposeFromEngine(match, homeText, awayText);
    }

    // Score and clock read live from the engine on every call, composed with the given team
    // names. Shared by the scoreboard-present and scoreboard-hidden paths so the line is
    // byte-identical whichever source supplied the names.
    private static string ComposeFromEngine(FM.Match.Match match, string homeText, string awayText)
    {
        MatchState state;
        try
        {
            state = (MatchState)MatchStateProp.GetValue(match);
        }
        catch (System.Exception ex)
        {
            // The engine field is gone or unreadable. Degrade rather than speak a stale/garbled score.
            NoteDegradeOnce($"[MatchWatcher] match state read failed: {ex.Message}");
            return $"score {Speech.Degrade.Unavailable}";
        }
        string score = $"{state.HomeGoals} - {state.AwayGoals}";
        string clock = FormatClock(match, state, out bool clockDegraded);

        // A clean compose clears the dedup latch so a later failure logs fresh rather than being
        // suppressed as a repeat; a degraded clock keeps it latched (its read is still failing
        // on every tick).
        if (!clockDegraded) _lastDegradeReason = null;
        return Navigation.DropdownHtml.StripRichText($"{homeText} {score} {awayText}, {clock}");
    }

    /// <summary>
    /// The live engine state struct. False when the field is unreadable; the caller degrades
    /// rather than reporting stale values.
    /// </summary>
    internal static bool TryReadState(FM.Match.Match match, out MatchState state)
    {
        try
        {
            state = (MatchState)MatchStateProp.GetValue(match);
            return true;
        }
        catch (System.Exception ex)
        {
            NoteDegradeOnce($"[MatchWatcher] match state read failed: {ex.Message}");
            state = default;
            return false;
        }
    }

    /// <summary>
    /// The displayed minute an engine time falls in, framed by the same period boundaries the
    /// scorebar uses — so a second-half event reads as the game shows it rather than at the raw
    /// continuous clock, which runs minutes ahead from the second half on. Football numbers a
    /// minute from its first second (0:30 is the 1st minute), hence the ceiling. Null when the
    /// boundaries are unreadable; the caller must omit the minute rather than show a raw one.
    /// </summary>
    internal static int? TryDisplayMinute(FM.Match.Match match, MatchState state, double engineSeconds)
    {
        var boundaries = TryReadPeriodBoundaries(match, state);
        if (boundaries == null) return null;
        var display = Navigation.MatchClock.DisplaySeconds(engineSeconds, boundaries.Value.Starts, boundaries.Value.Ends);
        if (display == null) return null;
        return System.Math.Max(1, (int)System.Math.Ceiling(display.Value / 60.0));
    }

    /// <summary>
    /// This match's team names as the scorebar shows them, home then away, or null when they have
    /// not been readable yet. The engine carries no club names at all (its match setup names only
    /// players and officials), so the scoreboard is the only source; these are the competition
    /// template's short names.
    /// </summary>
    internal static (string Home, string Away)? TryTeamNames(bool requireLive = false)
    {
        var scoreboard = FindScoreboard();
        var homeEl = scoreboard == null ? null : FindHomeNameEl(scoreboard);
        var awayEl = scoreboard == null ? null : FindAwayNameEl(scoreboard);
        if (homeEl != null && awayEl != null)
        {
            var home = ReadText(homeEl);
            var away = ReadText(awayEl);
            if (home != null && away != null) return (home, away);
        }
        return !requireLive && _lastHomeText != null && _lastAwayText != null ? (_lastHomeText, _lastAwayText) : null;
    }

    // Maps the live engine clock to the game's MM:SS display via the pure formatter.
    // state.Time is the playback clock and runs continuously across the whole match —
    // first-half stoppage carries into the second half's raw value (a 2-minute overrun
    // makes the second half open at 47:00) — so play periods first translate it to the
    // period-relative display value the game's own scorebar shows, framed purely by the
    // kick-off/whistle boundaries (state.Period tracks the SIM FRONTIER, which runs ahead
    // of playback mid-highlight, so it only picks the break/full-time state words).
    // Missing boundaries degrade the clock, never show the raw value: raw reads minutes
    // ahead of the screen from the second half on.
    private static string FormatClock(FM.Match.Match match, MatchState state, out bool degraded)
    {
        degraded = false;
        var clockState = MapPeriod(match.MatchPeriod);
        if (clockState != Navigation.MatchClockState.Playing)
            return Navigation.MatchClock.Format(0, 0, clockState);

        var boundaries = TryReadPeriodBoundaries(match, state);
        double? display = boundaries == null
            ? null
            : Navigation.MatchClock.DisplaySeconds(state.Time, boundaries.Value.Starts, boundaries.Value.Ends);
        if (display == null)
        {
            if (boundaries != null)
                NoteDegradeOnce("[MatchWatcher] period boundaries do not frame the display clock; see /eval StopTimeInfos.");
            degraded = true;
            return $"clock {Speech.Degrade.Unavailable}";
        }
        return Navigation.MatchClock.Format(display.Value, state.InjuryTime, clockState);
    }

    private static Navigation.MatchClockState MapPeriod(MatchPeriod period)
    {
        switch (period)
        {
            case MatchPeriod.HalfTime:
            case MatchPeriod.ExtraTimeHalfTime:
                return Navigation.MatchClockState.HalfTime;
            case MatchPeriod.FullTime:
                return Navigation.MatchClockState.FullTime;
            case MatchPeriod.Penalties:
                return Navigation.MatchClockState.Penalties;
            default:
                return Navigation.MatchClockState.Playing;
        }
    }

    // Consecutive start entries closer than this are the same kick-off recorded twice
    // (StartOfGame plus StartOfPeriod); real periods are 45 minutes apart.
    private const double SameKickoffSeconds = 60;

    // Engine times of the period kick-off and period-end whistle stops, ascending, from
    // MatchFrame.StopTimeInfos (each stop records its time slice). Entries ahead of the
    // playback position are included deliberately: the sim frontier writes them early, and
    // DisplaySeconds selects by the playback time, so they never apply prematurely. Slices
    // convert to seconds via the live rate state.Time / state.TimeSlice (~0.25 s per slice
    // — both run continuously from kick-off, verified against the scorebar in a live
    // match). Null when the frame or the rate cannot be read; the caller degrades the clock.
    private static (System.Collections.Generic.List<double> Starts, System.Collections.Generic.List<double> Ends)?
        TryReadPeriodBoundaries(FM.Match.Match match, MatchState state)
    {
        try
        {
            if (state.TimeSlice <= 0)
            {
                // Pre-kickoff: no slice rate yet, and nothing to translate either.
                NoteDegradeOnce("[MatchWatcher] no slice rate for the display clock yet (playback at slice 0).");
                return null;
            }
            var frame = (FM.Match.MatchFrame)MatchFrameProp.GetValue(match);
            var stops = frame.StopTimeInfos;
            double secondsPerSlice = state.Time / state.TimeSlice;
            var starts = new System.Collections.Generic.List<double>();
            var ends = new System.Collections.Generic.List<double>();
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                double time = stop.StopTimeSlice * secondsPerSlice;
                switch (stop.NextStopReason)
                {
                    case MatchStopReason.StartOfGame:
                    case MatchStopReason.StartOfPeriod:
                        starts.Add(time);
                        break;
                    case MatchStopReason.EndOfPeriod:
                    // FM27 records the final whistle as EndOfGame rather than
                    // another EndOfPeriod. Feed that actual boundary to the
                    // original clock formatter so only the finished period
                    // freezes; live stoppage time must continue ticking.
                    case MatchStopReason.EndOfGame:
                        ends.Add(time);
                        break;
                }
            }
            starts.Sort();
            ends.Sort();
            for (int i = starts.Count - 1; i > 0; i--)
                if (starts[i] - starts[i - 1] < SameKickoffSeconds) starts.RemoveAt(i);
            return (starts, ends);
        }
        catch (System.Exception ex)
        {
            NoteDegradeOnce($"[MatchWatcher] period boundary read failed: {ex.Message}");
            return null;
        }
    }

    private static string ReadText(VisualElement el)
    {
        var textEl = el.TryCast<TextElement>();
        return textEl != null && !string.IsNullOrEmpty(textEl.text) ? textEl.text : null;
    }

    private static string ReadFirstText(VisualElement container)
    {
        var text = ReadText(container);
        if (text != null) return text.Trim();
        int count = container.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = ReadFirstText(container.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindInOpenPanels(string name)
    {
        if (!SI.Bindable.PanelManager.Exists()) return null;
        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        for (int i = 0; i < layers.Count; i++)
        {
            var open = layers[i].Open;
            if (open == null) continue;
            for (int p = 0; p < open.Count; p++)
            {
                var panel = open[p];
                if (panel == null) continue;
                var found = FindByName(panel, name);
                if (found != null && ElementBridge.IsVisible(found)) return found;
            }
        }
        return null;
    }

    private static VisualElement FindByName(VisualElement el, string name)
    {
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindByName(el.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }
}

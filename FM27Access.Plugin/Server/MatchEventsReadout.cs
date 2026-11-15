using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation;
using HarmonyLib;
using SI.Match;
using SI.MatchTypes;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Alt+` double tap: the match's goals, cards and injuries so far, read from the engine's own
/// event log (<c>FM.Match.Match.m_commentaryEvents</c>). Each <c>MatchCommentaryEvent</c> carries a
/// classified <c>CommentaryEventID</c>, the unique ids of everyone involved and the time slice it
/// happened on; <c>MatchPlaybackController.Setup.Entities</c> resolves those ids to names, shirt
/// numbers and team index. Nothing is parsed out of commentary prose.
///
/// SPOILERS ARE THE HAZARD HERE. The simulation runs ahead of the playback the player is watching
/// — verified live: 16 events existed past the playback slice mid-match — so every event past
/// <c>MatchState.TimeSlice</c> is treated as not having happened. Reading the list raw would
/// announce a goal minutes before the player hears it.
///
/// Main thread only; the entry point marshals. Stateless: every call re-finds the engine and
/// re-reads the log, and the entity table is rebuilt per call rather than cached.
/// </summary>
internal static class MatchEventsReadout
{
    /// <summary>The spoken events report, or an honest marker when the engine cannot be read.</summary>
    public static string Describe()
    {
        try
        {
            return MainThreadDispatcher.Invoke(Compose);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchEventsReadout] events read failed: {ex.Message}");
            return "match events " + Speech.Degrade.Unavailable;
        }
    }

    private static string _lastDegradeReason;

    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    private static string Compose()
    {
        var match = MatchBallLocation.CurrentMatch();
        if (match == null) return "Not in a match.";

        if (!MatchWatcher.TryReadState(match, out var state))
            return "match events " + Speech.Degrade.Unavailable;

        // Before the first slice ticks there is no playback position to gate on, and by
        // definition nothing has happened yet.
        if (state.TimeSlice <= 0) return MatchEventsReport.NothingYet;

        var events = ReadEvents(match);
        var entities = ReadEntities(match);
        if (events == null || entities == null)
            return "match events " + Speech.Degrade.Unavailable;

        double secondsPerSlice = state.Time / state.TimeSlice;
        var teams = MatchWatcher.TryTeamNames();

        var goals = new List<GoalEvent>();
        // Cards are carried with the player id that earned them so the on-pitch filter below keys
        // off the same id the event named, never off a display name.
        var bookings = new List<(CardEvent Card, int Owner)>();
        var injuries = new List<InjuryEvent>();
        var offPitch = new HashSet<int>();

        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (evt.TimeSlice > state.TimeSlice) continue; // not watched yet — does not exist

            var id = evt.CommentaryEventID;
            int? minute = MatchWatcher.TryDisplayMinute(match, state, evt.TimeSlice * secondsPerSlice);

            if (FM.Match.CommentaryEventDefinitionsExtensions.IsAnyTypeOfGoal(id))
            {
                // A disallowed goal (EventIllegalGoalScored — offside, foul, VAR) is classified as
                // a goal by the engine but never counted and carries no paired scoring event, so it
                // has no scorer to resolve. Announcing it would report a goal that did not happen.
                if (FM.Match.CommentaryEventDefinitionsExtensions.IsIllegalGoal(id)) continue;
                goals.Add(BuildGoal(events, i, entities, teams, minute));
                continue;
            }

            if (FM.Match.CommentaryEventDefinitionsExtensions.IsSentOffEvent(id))
            {
                int player = FirstPlayer(evt);
                offPitch.Add(player);
                bookings.Add((new CardEvent(PlayerName(entities, player), TeamName(entities, teams, player),
                    minute, sentOff: true), player));
                continue;
            }

            if (YellowCardEvents.Contains(id))
            {
                int player = FirstPlayer(evt);
                bookings.Add((new CardEvent(PlayerName(entities, player), TeamName(entities, teams, player),
                    minute, sentOff: false), player));
                continue;
            }

            if (InjuryEvents.Contains(id))
            {
                int player = FirstPlayer(evt);
                if (ForcedOffEvents.Contains(id)) offPitch.Add(player);
                injuries.Add(new InjuryEvent(PlayerName(entities, player), TeamName(entities, teams, player)));
                continue;
            }

            CollectSubstitutedOff(evt, id, offPitch);
        }

        // Per the player: a yellow only matters while its owner is still on the pitch — a booking
        // for someone already withdrawn is history, not a risk. Dismissals stay listed either way.
        var cards = new List<CardEvent>();
        foreach (var (card, owner) in bookings)
            if (card.SentOff || !offPitch.Contains(owner)) cards.Add(card);

        return MatchEventsReport.Compose(goals, cards, injuries);
    }

    /// <summary>
    /// The goal at <paramref name="anchorIndex"/>. The anchor (<c>EventGoalScored</c>) holds the
    /// participants but not, reliably, which slot is the scorer: live sampling found both
    /// [scorer, assist] and [-1, scorer] orderings. The scorer therefore comes from the paired
    /// scoring event on the same slice (<c>EventGoalOne</c> / <c>EventOwnGoal</c>), whose first
    /// slot is unambiguous, and the assist is whatever other participant the anchor names. When no
    /// paired event is found the scorer is unknown and says so rather than guessing a slot.
    /// </summary>
    private static GoalEvent BuildGoal(
        Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent> events, int anchorIndex,
        Dictionary<int, EntityInfo> entities, (string Home, string Away)? teams, int? minute)
    {
        var anchor = events[anchorIndex];
        int scorer = -1;
        bool ownGoal = false;

        for (int i = anchorIndex; i < events.Count; i++)
        {
            var evt = events[i];
            if (evt.TimeSlice != anchor.TimeSlice) break;
            var id = evt.CommentaryEventID;
            if (FM.Match.CommentaryEventDefinitionsExtensions.IsAnyTypeOfGoal(id)) continue;
            if (!FM.Match.CommentaryEventDefinitionsExtensions.ScoredGoal(id)) continue;
            scorer = FirstPlayer(evt);
            ownGoal = id == CommentaryEventDefinitions.EventOwnGoal;
            break;
        }

        // With no scorer resolved there is no way to tell which participant assisted and which
        // scored, so naming one as the assist would attribute the goal to the wrong player.
        if (scorer < 0)
            NoteDegradeOnce($"[MatchEventsReadout] goal at slice {anchor.TimeSlice} ({anchor.CommentaryEventID}) has no paired scoring event; scorer unresolved");

        string assist = null;
        var ids = anchor.PlayerUniqueIDs;
        if (ids != null && !ownGoal && scorer >= 0)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] < 0 || ids[i] == scorer) continue;
                assist = PlayerName(entities, ids[i]);
                break;
            }
        }

        // The team named is the one the goal counted FOR, which for an own goal is the side the
        // scorer does not play for. Derived from the scorer's own entity record rather than the
        // event's home/away tag: the entity table is the exact per-player attribution, and live
        // sampling showed the tag pointing at the conceding side on an own goal.
        string team = entities.TryGetValue(scorer, out var info)
            ? TeamLabel(teams, (info.TeamIndex == 0) != ownGoal)
            : Speech.Degrade.Unreadable;
        return new GoalEvent(PlayerName(entities, scorer), team, minute, assist, ownGoal);
    }

    // Read the FM27 event's verified participant layout. Ordinary events interleave
    // (on, off); the halftime triple groups incoming then outgoing players. Treating
    // that one as pairs incorrectly removes an incoming player's booking from the
    // on-pitch risk report. Only the withdrawn player matters here.
    private static void CollectSubstitutedOff(FM.Match.MatchCommentaryEvent evt,
        CommentaryEventDefinitions id, HashSet<int> offPitch)
    {
        int count=FM27MatchEvents.SubstitutionPairs(id);
        if(count==0)return;
        var ids = evt.PlayerUniqueIDs;
        if (ids == null) return;

        for (int pair = 0; pair < count; pair++)
        {
            int offSlot = FM27MatchEvents.SubstitutedOffSlot(id, pair);
            if (offSlot < ids.Length && ids[offSlot] >= 0) offPitch.Add(ids[offSlot]);
        }
    }

    private static int FirstPlayer(FM.Match.MatchCommentaryEvent evt)
    {
        var ids = evt.PlayerUniqueIDs;
        if (ids == null) return -1;
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] >= 0) return ids[i];
        return -1;
    }

    private readonly struct EntityInfo
    {
        public readonly string Name;
        public readonly sbyte TeamIndex;

        public EntityInfo(string name, sbyte teamIndex)
        {
            Name = name;
            TeamIndex = teamIndex;
        }
    }

    // The shirt name is the surname the game prints on the kit ("Baudry"); the billboard name is
    // the initialled form ("M. Baudry"). The surname is what commentary and a manager use, so it
    // is preferred, with the billboard name as the fallback for entities that carry only that.
    private static Dictionary<int, EntityInfo> ReadEntities(FM.Match.Match match)
    {
        try
        {
            var controller = (FM.Match.MatchPlaybackController)ControllerProp.GetValue(match);
            var entities = controller.Setup.Entities;
            var table = new Dictionary<int, EntityInfo>();
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity.uniqueID < 0) continue;
                string name = entity.shirtName.ToString();
                if (string.IsNullOrWhiteSpace(name)) name = entity.billboardName.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                table[entity.uniqueID] = new EntityInfo(name, entity.teamIndex);
            }
            return table;
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchEventsReadout] match entity table read failed: {ex.Message}");
            return null;
        }
    }

    private static Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent> ReadEvents(FM.Match.Match match)
    {
        try
        {
            return EventsProp.GetValue(match)
                as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        }
        catch (Exception ex)
        {
            NoteDegradeOnce($"[MatchEventsReadout] commentary event log read failed: {ex.Message}");
            return null;
        }
    }

    private static string PlayerName(Dictionary<int, EntityInfo> entities, int uniqueId) =>
        entities.TryGetValue(uniqueId, out var info) ? info.Name : Speech.Degrade.Unreadable;

    // The player's own entity record is the exact attribution. A participant missing from the table
    // has no resolvable name either, so his side is marked unreadable rather than inferred from the
    // event's home/away tag, which describes the incident and not necessarily this player.
    private static string TeamName(Dictionary<int, EntityInfo> entities, (string Home, string Away)? teams,
        int uniqueId) =>
        entities.TryGetValue(uniqueId, out var info)
            ? TeamLabel(teams, info.TeamIndex == 0)
            : Speech.Degrade.Unreadable;

    // The engine's match setup names players and officials but carries no club names at all, so
    // the scorebar is the only source; unreadable names degrade rather than becoming "home"/"away",
    // which a player could mistake for the club's actual name.
    private static string TeamLabel((string Home, string Away)? teams, bool home) =>
        teams == null ? Speech.Degrade.Unreadable : (home ? teams.Value.Home : teams.Value.Away);

    private static PropertyInfo _eventsProp;
    private static PropertyInfo _controllerProp;

    private static PropertyInfo EventsProp => _eventsProp ??= RequireProperty("m_commentaryEvents");
    private static PropertyInfo ControllerProp => _controllerProp ??= RequireProperty("m_controller");

    private static PropertyInfo RequireProperty(string name)
    {
        var prop = AccessTools.Property(typeof(FM.Match.Match), name);
        if (prop == null)
        {
            Speech.Log.Error($"[MatchEventsReadout] FM.Match.Match.{name} property not found via AccessTools.");
            throw new MissingMemberException("FM.Match.Match", name);
        }
        return prop;
    }

    // Yellows have no game-provided classifier (unlike goals and dismissals), so they are named.
    // A second yellow is a dismissal and is caught by IsSentOffEvent before this set is consulted.
    private static readonly HashSet<CommentaryEventDefinitions> YellowCardEvents = new()
    {
        CommentaryEventDefinitions.EventYellowCard,
        CommentaryEventDefinitions.EventVarResultRefGivesYellowCard,
    };

    // Deliberately narrow: only events that mean THIS player has been hurt. The log also carries
    // injury-adjacent lines — EventPlayerTimeWastingByFakingInjury, ball put out of play for an
    // injury, a goal scored after returning from one — that a looser name match would report as
    // injuries that never happened.
    private static readonly HashSet<CommentaryEventDefinitions> InjuryEvents = new()
    {
        CommentaryEventDefinitions.EventPlayerInjuredForcedOff,
        CommentaryEventDefinitions.EventPlayerInjuredForcedOffNoCommentaryDiagnosis,
        CommentaryEventDefinitions.EventPlayerInjuredSentOff,
        CommentaryEventDefinitions.EventPlayerInjuredGoesOn,
        CommentaryEventDefinitions.EventPlayerInjuredGoesOnNoCommentaryDiagnosis,
        CommentaryEventDefinitions.EventPlayerInjuredByOpponent,
        CommentaryEventDefinitions.EventPlayerInjuredFoulingOpponent,
    };

    // The injuries that take the player off — they clear his yellow from the on-pitch list.
    private static readonly HashSet<CommentaryEventDefinitions> ForcedOffEvents = new()
    {
        CommentaryEventDefinitions.EventPlayerInjuredForcedOff,
        CommentaryEventDefinitions.EventPlayerInjuredForcedOffNoCommentaryDiagnosis,
        CommentaryEventDefinitions.EventPlayerInjuredSentOff,
    };
}

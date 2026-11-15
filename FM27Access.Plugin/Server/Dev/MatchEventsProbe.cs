using System;
using System.Reflection;
using System.Text;
using FM27Access.Plugin.Server;
using HarmonyLib;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only dump of the engine's commentary event log (<c>FM.Match.Match.m_commentaryEvents</c>),
/// the source behind the Alt+` double-tap events readout. Prints each event's slice, id, the
/// classifier answers the readout keys off (IsAnyTypeOfGoal / ScoredGoal / IsSentOffEvent /
/// IsSubstituteEvent) and its participant ids resolved to names, so a goal that reads back as
/// "couldn't read" can be traced to the event ordering that produced it.
///
/// GET /eventsprobe[?slice=N&amp;window=N&amp;all=1]. Read-only, main thread.
/// </summary>
internal static class MatchEventsProbe
{
    internal static string Run(int slice, int window, bool all)
    {
        var match = MatchBallLocation.CurrentMatch();
        if (match == null) return "not in a match\n";

        var eventsProp = AccessTools.Property(typeof(FM.Match.Match), "m_commentaryEvents");
        var controllerProp = AccessTools.Property(typeof(FM.Match.Match), "m_controller");
        var events = eventsProp.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (events == null) return "commentary event log unreadable\n";

        var controller = (FM.Match.MatchPlaybackController)controllerProp.GetValue(match);
        var setup = controller.Setup.Entities;
        var entities = new System.Collections.Generic.Dictionary<int, string>();
        for (int i = 0; i < setup.Length; i++)
        {
            var e = setup[i];
            if (e.uniqueID < 0) continue;
            string name = e.shirtName.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = e.billboardName.ToString();
            entities[e.uniqueID] = $"{name}/t{e.teamIndex}";
        }

        var sb = new StringBuilder();
        sb.Append("events=").Append(events.Count);
        if (MatchWatcher.TryReadState(match, out var state))
            sb.Append("  playback slice=").Append(state.TimeSlice).Append(" time=").Append(state.Time);
        sb.Append('\n');

        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (!all && window > 0 && Math.Abs(evt.TimeSlice - slice) > window) continue;

            var id = evt.CommentaryEventID;
            bool goal = FM.Match.CommentaryEventDefinitionsExtensions.IsAnyTypeOfGoal(id);
            bool scored = FM.Match.CommentaryEventDefinitionsExtensions.ScoredGoal(id);
            bool sentOff = FM.Match.CommentaryEventDefinitionsExtensions.IsSentOffEvent(id);
            var sub = FM27MatchEvents.SubstitutionPairs(id);

            if (!all && window <= 0 && !(goal || scored || sentOff)) continue;

            sb.Append('[').Append(i).Append("] slice=").Append(evt.TimeSlice)
              .Append(' ').Append(id).Append(" (").Append((int)id).Append(')');
            if (goal) sb.Append(" GOAL");
            if (FM.Match.CommentaryEventDefinitionsExtensions.IsIllegalGoal(id)) sb.Append(" ILLEGAL");
            if (scored) sb.Append(" SCORED");
            if (sentOff) sb.Append(" SENTOFF");
            if (sub != 0) sb.Append(" SUB=").Append(sub);
            sb.Append(" ids=");
            var ids = evt.PlayerUniqueIDs;
            if (ids == null) sb.Append("null");
            else
                for (int p = 0; p < ids.Length; p++)
                    sb.Append(ids[p]).Append('(').Append(Name(entities, ids[p])).Append(") ");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Name(System.Collections.Generic.Dictionary<int, string> entities, int uniqueId) =>
        uniqueId < 0 ? "-" : entities.TryGetValue(uniqueId, out var n) ? n : "NOT-IN-SETUP";
}

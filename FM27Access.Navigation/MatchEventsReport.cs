using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>A goal, already resolved to names by the plugin. Minute is null when unreadable.</summary>
public readonly struct GoalEvent
{
    public readonly string Scorer;
    public readonly string Team;      // the team the goal counted FOR, own goals included
    public readonly int? Minute;
    public readonly string Assist;    // null when the goal was unassisted or the assist is ambiguous
    public readonly bool OwnGoal;

    public GoalEvent(string scorer, string team, int? minute, string assist, bool ownGoal)
    {
        Scorer = scorer;
        Team = team;
        Minute = minute;
        Assist = assist;
        OwnGoal = ownGoal;
    }
}

/// <summary>A booking or dismissal, already resolved to names by the plugin.</summary>
public readonly struct CardEvent
{
    public readonly string Player;
    public readonly string Team;
    public readonly int? Minute;
    public readonly bool SentOff;

    public CardEvent(string player, string team, int? minute, bool sentOff)
    {
        Player = player;
        Team = team;
        Minute = minute;
        SentOff = sentOff;
    }
}

/// <summary>An injury. Minutes are deliberately omitted per the player's request.</summary>
public readonly struct InjuryEvent
{
    public readonly string Player;
    public readonly string Team;

    public InjuryEvent(string player, string team)
    {
        Player = player;
        Team = team;
    }
}

/// <summary>
/// Phrasing for the Alt+` double-tap match-events readout: goals, then cards, then injuries, each
/// group in the order they happened. Pure and game-independent so it is unit-testable; the plugin
/// resolves the engine's event log to these entries and speaks the composed line.
///
/// A group with nothing in it is left out entirely rather than announced as empty — the player
/// asked for the events, not a checklist — but a match with no events at all must still say
/// something, or silence would be indistinguishable from a failed read.
/// </summary>
public static class MatchEventsReport
{
    public const string NothingYet = "No goals, cards or injuries yet.";

    public static string Compose(
        IReadOnlyList<GoalEvent> goals,
        IReadOnlyList<CardEvent> cards,
        IReadOnlyList<InjuryEvent> injuries)
    {
        var sb = new StringBuilder();
        AppendGroup(sb, "Goals", goals, DescribeGoal);
        AppendGroup(sb, "Cards", cards, DescribeCard);
        AppendGroup(sb, "Injuries", injuries, DescribeInjury);
        return sb.Length == 0 ? NothingYet : sb.ToString();
    }

    private static void AppendGroup<T>(StringBuilder sb, string heading, IReadOnlyList<T> items,
        System.Func<T, string> describe)
    {
        if (items == null || items.Count == 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(heading).Append('.');
        foreach (var item in items)
            sb.Append(' ').Append(describe(item)).Append('.');
    }

    // "Khous, USO, 34 minutes, assisted by Diako" — scorer first, since that is what the player
    // is listening for. The team named is the one the goal counted for, so an own goal reads as
    // the scoring side's goal with the scorer flagged, never as a goal for his own team.
    private static string DescribeGoal(GoalEvent goal)
    {
        var sb = new StringBuilder(goal.Scorer);
        if (goal.OwnGoal) sb.Append(" own goal");
        sb.Append(", ").Append(goal.Team);
        AppendMinute(sb, goal.Minute);
        if (!string.IsNullOrEmpty(goal.Assist)) sb.Append(", assisted by ").Append(goal.Assist);
        return sb.ToString();
    }

    private static string DescribeCard(CardEvent card)
    {
        var sb = new StringBuilder(card.Player);
        sb.Append(", ").Append(card.Team);
        AppendMinute(sb, card.Minute);
        sb.Append(card.SentOff ? ", sent off" : ", booked");
        return sb.ToString();
    }

    private static string DescribeInjury(InjuryEvent injury) => injury.Player + ", " + injury.Team;

    private static void AppendMinute(StringBuilder sb, int? minute)
    {
        if (minute != null) sb.Append(", ").Append(minute.Value).Append(" minutes");
    }
}

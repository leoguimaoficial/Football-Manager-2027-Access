using FM27Access.Navigation;

namespace FM27Access.Tests;

public class MatchEventsReportTests
{
    private static readonly GoalEvent[] NoGoals = [];
    private static readonly CardEvent[] NoCards = [];
    private static readonly InjuryEvent[] NoInjuries = [];

    [Fact]
    public void Compose_NothingHappened_SaysSo()
    {
        Assert.Equal(MatchEventsReport.NothingYet,
            MatchEventsReport.Compose(NoGoals, NoCards, NoInjuries));
    }

    [Fact]
    public void Compose_Goal_ReadsScorerTeamMinuteThenAssist()
    {
        var goals = new[] { new GoalEvent("Khous", "USO", 34, "Diako", ownGoal: false) };
        Assert.Equal("Goals. Khous, USO, 34 minutes, assisted by Diako.",
            MatchEventsReport.Compose(goals, NoCards, NoInjuries));
    }

    [Fact]
    public void Compose_UnassistedGoal_OmitsTheAssistClause()
    {
        var goals = new[] { new GoalEvent("Botella", "CAEN", 10, null, ownGoal: false) };
        Assert.Equal("Goals. Botella, CAEN, 10 minutes.",
            MatchEventsReport.Compose(goals, NoCards, NoInjuries));
    }

    [Fact]
    public void Compose_OwnGoal_FlagsItAndCreditsTheTeamGiven()
    {
        var goals = new[] { new GoalEvent("Giraudon", "CAEN", 22, null, ownGoal: true) };
        Assert.Equal("Goals. Giraudon own goal, CAEN, 22 minutes.",
            MatchEventsReport.Compose(goals, NoCards, NoInjuries));
    }

    [Fact]
    public void Compose_UnreadableMinute_OmitsTheMinuteRatherThanGuessing()
    {
        var goals = new[] { new GoalEvent("Khous", "USO", null, "Diako", ownGoal: false) };
        Assert.Equal("Goals. Khous, USO, assisted by Diako.",
            MatchEventsReport.Compose(goals, NoCards, NoInjuries));
    }

    [Fact]
    public void Compose_Cards_DistinguishBookingFromDismissal()
    {
        var cards = new[]
        {
            new CardEvent("Baudry", "USO", 29, sentOff: false),
            new CardEvent("Rajot", "CAEN", 61, sentOff: true),
        };
        Assert.Equal("Cards. Baudry, USO, 29 minutes, booked. Rajot, CAEN, 61 minutes, sent off.",
            MatchEventsReport.Compose(NoGoals, cards, NoInjuries));
    }

    [Fact]
    public void Compose_Injuries_CarryNoMinute()
    {
        var injuries = new[] { new InjuryEvent("Morel", "USO") };
        Assert.Equal("Injuries. Morel, USO.",
            MatchEventsReport.Compose(NoGoals, NoCards, injuries));
    }

    [Fact]
    public void Compose_AllThree_OrdersGoalsThenCardsThenInjuries()
    {
        var goals = new[] { new GoalEvent("Khous", "USO", 34, null, ownGoal: false) };
        var cards = new[] { new CardEvent("Baudry", "USO", 29, sentOff: false) };
        var injuries = new[] { new InjuryEvent("Morel", "USO") };
        Assert.Equal("Goals. Khous, USO, 34 minutes. Cards. Baudry, USO, 29 minutes, booked. Injuries. Morel, USO.",
            MatchEventsReport.Compose(goals, cards, injuries));
    }

    [Fact]
    public void Compose_EmptyGroup_IsLeftOutEntirely()
    {
        var injuries = new[] { new InjuryEvent("Morel", "USO") };
        var line = MatchEventsReport.Compose(NoGoals, NoCards, injuries);
        Assert.DoesNotContain("Goals", line);
        Assert.DoesNotContain("Cards", line);
    }
}

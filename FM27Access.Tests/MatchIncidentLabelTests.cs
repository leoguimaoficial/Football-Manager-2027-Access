using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public sealed class MatchIncidentLabelTests
{
    [Theory]
    [InlineData("goal_small_3x", "Goal")]
    [InlineData("own_goal_small_3x", "Own goal")]
    [InlineData("goal down_small_2x", "Own goal")]
    [InlineData("goal-disallowed_small_3x", "Goal disallowed")]
    [InlineData("goal-allowed_small_3x", "Goal allowed")]
    [InlineData("penalty awarded_small_3x", "Penalty awarded")]
    [InlineData("penalty scored_small_3x", "Penalty scored")]
    [InlineData("bundesliga_penalty_missed_3x", "Penalty missed")]
    [InlineData("penalties_small_3x", "Penalty shootout")]
    [InlineData("penelities_small_3x", "Penalty kick")]
    [InlineData("yellow card_small_3x", "Yellow card")]
    [InlineData("red card_small_3x", "Red card")]
    [InlineData("sub on_small_3x", "Substitution on")]
    [InlineData("sub off_small_3x", "Substitution off")]
    [InlineData("sub on and off_small_3x", "Substituted on and off")]
    [InlineData("sub on and injured_small_3x", "Substitute injured")]
    [InlineData("sub on and sent off_small_3x", "Substitute sent off")]
    [InlineData("special-injury-default_3x", "Injury")]
    [InlineData("foul_small_3x", "Foul")]
    [InlineData("free_kick_small_3x", "Free kick")]
    [InlineData("corner-kick_small_3x", "Corner kick")]
    [InlineData("throw_in_small_3x", "Throw-in")]
    [InlineData("kickoff_small_3x", "Kick-off")]
    [InlineData("half_time_small_3x", "Half-time")]
    [InlineData("end_of_match_small_3x", "Full-time")]
    [InlineData("overtime_small_3x", "Extra time")]
    [InlineData("shootout_small_3x", "Penalty shootout")]
    [InlineData("start_of_match_small_3x", "Start of match")]
    [InlineData("play_resumed_small_3x", "Play resumed")]
    public void FromSprite_MapsKnownIncidentFamilies(string sprite, string expected)
    {
        Assert.Equal(expected, MatchIncidentLabel.FromSprite(sprite));
    }

    [Fact]
    public void FromSprite_HumanisesReadableFutureIncident()
    {
        Assert.Equal("Video review complete",
            MatchIncidentLabel.FromSprite("video_review_complete_small_3x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("level=4, size=small 16px_3x")]
    public void FromSprite_RejectsEmptyOrBindingShapedNames(string sprite)
    {
        Assert.Null(MatchIncidentLabel.FromSprite(sprite));
    }

    [Fact]
    public void FromSprite_RejectsMissingName()
    {
        Assert.Null(MatchIncidentLabel.FromSprite(null));
    }

    [Fact]
    public void FormatPlayers_LabelsARegularGoalsSecondPlayerAsTheAssist()
    {
        Assert.Equal("Bruno Gomes, assisted by Vitinho",
            MatchIncidentLabel.FormatPlayers("Goal", new[] { "Bruno Gomes", "Vitinho" }));
    }

    [Fact]
    public void FormatPlayers_GoalWithoutSecondPlayerAddsNoAssistPhrase()
    {
        Assert.Equal("Bruno Gomes",
            MatchIncidentLabel.FormatPlayers("Goal", new[] { "Bruno Gomes" }));
    }

    [Theory]
    [InlineData("Own goal")]
    [InlineData("Penalty scored")]
    [InlineData("Red card")]
    public void FormatPlayers_DoesNotGuessTheSecondPlayersRoleForOtherIncidents(string eventType)
    {
        Assert.Equal("Player one, Player two",
            MatchIncidentLabel.FormatPlayers(eventType, new[] { "Player one", "Player two" }));
    }
}

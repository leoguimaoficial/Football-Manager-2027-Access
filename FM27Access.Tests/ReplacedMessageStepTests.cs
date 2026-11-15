using FM27Access.Plugin.Server;
using Move = FM27Access.Plugin.Server.ReplacedMessageStep.Move;
using Position = FM27Access.Plugin.Server.ReplacedMessageStep.Position;

namespace FM27Access.Tests;

public class ReplacedMessageStepTests
{
    [Theory]
    [InlineData(10u, 10u, 2u, 2u, 16u, 16u, true)]
    [InlineData(10u, 10u, 2u, 2u, 16u, 20u, true)]
    [InlineData(10u, 11u, 2u, 2u, 16u, 20u, false)]
    [InlineData(10u, 10u, 2u, 4u, 16u, 20u, false)]
    [InlineData(10u, 10u, 4u, 2u, 16u, 20u, false)]
    public void NativeReuseRenewsOnlyTheDestinationVisitWhileRetainingItsPanelAndPrecedingIdentity(
        uint beforeParent, uint afterParent, uint beforePanel, uint afterPanel, uint beforeVisit, uint afterVisit, bool matches)
        => Assert.Equal(matches, ReplacedMessageStep.EarlierPrefixMatches(
            new[]{(1u,beforeParent),(beforePanel,beforeVisit),(3u,19u)},
            new[]{(1u,afterParent),(afterPanel,afterVisit)},1));

    [Theory]
    [InlineData(3,2,1,true)]
    [InlineData(3,2,2,false)]
    [InlineData(3,2,-1,false)]
    [InlineData(0,1,0,false)]
    [InlineData(1,0,0,false)]
    public void ReuseNeverReadsAMissingOrInvalidNativePrefix(int sourceCount,int currentCount,int destination,bool matches)
        => Assert.Equal(matches, ReplacedMessageStep.EarlierPrefixMatches(
            Enumerable.Repeat((1u,10u),sourceCount).ToArray(),Enumerable.Repeat((1u,10u),currentCount).ToArray(),destination));

    [Theory]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 3, "Portal, Offer, Inbox", true, true)]
    [InlineData(2, 4, "Portal, Offer, Inbox", 1, 2, "Portal, Offer", true, true)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 2, "Portal, Offer", false, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 2, "Other, Offer", true, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 2, "Portal, Profile", true, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 2, 3, "Portal, Offer, Other", true, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 4, "Portal, Offer, A, B", true, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", 1, 1, "Portal", true, false)]
    [InlineData(2, 2, "Portal, Offer, Inbox", 1, 2, "Portal, Offer", true, false)]
    [InlineData(2, 3, "Portal, Offer, Inbox", -1, 2, "Portal, Offer", true, false)]
    public void ReusingAnEarlierNativeDestinationRequiresRetainedPrefixAndActualNodeIdentity(
        int source, int sourceCount, string before, int target, int count, string after, bool sameNodes, bool allowed)
        => Assert.Equal(allowed, ReplacedMessageStep.CanReuseEarlierDestination(source, sourceCount, before, target, count, after, sameNodes));

    [Theory]
    [InlineData(1, 3, "Portal, Inbox", 1, 2, "Portal, Offer", true)]
    [InlineData(1, 3, "Portal, Inbox", 1, 3, "Portal, Offer, Profile", true)]
    [InlineData(1, 3, "Portal, Inbox", 2, 3, "Portal, Inbox, Offer", false)]
    [InlineData(1, 3, "Portal, Inbox", 1, 2, "Other, Offer", false)]
    [InlineData(1, 3, "Portal, Inbox", 1, 4, "Portal, Offer, A, B", false)]
    [InlineData(1, 3, "Portal, Inbox", 1, 1, "Portal", false)]
    [InlineData(-1, 3, "", -1, 2, "Portal, Offer", false)]
    [InlineData(0, 1, "Inbox", 0, 1, "Offer", true)]
    public void ReplacementRetainsOnlyTheSameSlotAndPrefixWhileAllowingNativeForwardTruncation(
        int sourceIndex, int sourceCount, string prefix, int index, int count, string shape, bool allowed)
        => Assert.Equal(allowed, ReplacedMessageStep.CanReplaceSource(sourceIndex, sourceCount, prefix, index, count, shape));

    [Fact]
    public void BackAndForwardTraverseAllThreeEntriesWithoutNativeIndexMoves()
    {
        var steps = new ReplacedMessageStep();
        Assert.Equal(Move.Message, steps.Plan(true));
        steps.Arrive(Position.Message);
        Assert.Equal(Move.Inbox, steps.Plan(true));
        steps.Arrive(Position.Inbox);
        Assert.Equal(Move.Game, steps.Plan(true));
        Assert.Equal(Move.Message, steps.Plan(false));
        steps.Arrive(Position.Message);
        Assert.Equal(Move.Destination, steps.Plan(false));
        steps.Arrive(Position.Destination);
        Assert.Equal(Move.Game, steps.Plan(false));
    }

    [Fact]
    public void PlanningOrFailedNavigationDoesNotCommitAPosition()
    {
        var steps = new ReplacedMessageStep();
        Assert.Equal(Move.Message, steps.Plan(true));
        Assert.Equal(Position.Destination, steps.Current);
        Assert.Equal(Move.Message, steps.Plan(true));
    }

    [Fact]
    public void ExplicitDismissalRetiresOnlyTheMessageAndPreservesActualDestination()
    {
        var steps = new ReplacedMessageStep();
        steps.Arrive(Position.Message);
        steps.DismissMessage();
        Assert.False(steps.HasMessage);
        Assert.Equal(Position.Inbox, steps.Current);
        Assert.Equal(Move.Destination, steps.Plan(false));
        steps.Arrive(Position.Destination);
        Assert.Equal(Move.Inbox, steps.Plan(true));
    }

    [Fact]
    public void NativeForwardLandsAtInboxAndNativeBackAtDestination()
    {
        var steps = new ReplacedMessageStep();
        steps.Arrive(Position.Inbox);
        Assert.Equal(Move.Message, steps.Plan(false));
        steps.Arrive(Position.Destination);
        Assert.Equal(Move.Message, steps.Plan(true));
    }

    [Fact]
    public void AnExplicitVerifiedMessageOpenCanFollowAnEarlierDismissal()
    {
        var steps = new ReplacedMessageStep();
        steps.DismissMessage();
        steps.OpenMessage(restoring: true);
        Assert.True(steps.HasMessage);
        Assert.Equal(Move.Inbox, steps.Plan(true));
        Assert.Equal(Move.Destination, steps.Plan(false));
    }

    [Fact]
    public void AnExplicitNewMessageTruncatesTheOldDestinationsVirtualForwardBranch()
    {
        var steps = new ReplacedMessageStep();
        steps.Arrive(Position.Inbox);
        steps.OpenMessage(restoring: false);
        Assert.False(steps.HasDestination);
        Assert.Equal(Move.Game, steps.Plan(false));
        steps.Arrive(Position.Inbox);
        Assert.Equal(Move.Message, steps.Plan(false));
        steps.DismissMessage();
        Assert.Equal(Move.Game, steps.Plan(false));
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(0, false, false)]
    [InlineData(2, true, true)]
    public void ANewNativeDestinationPreservesOnlyTheVirtualEntriesBelowItsOrigin(
        int origin, bool message, bool destination)
    {
        var steps = new ReplacedMessageStep();
        steps.Arrive((Position)origin);
        steps.LeftForNativeDestination();
        Assert.Equal(message, steps.HasMessage);
        Assert.Equal(destination, steps.HasDestination);
    }
}

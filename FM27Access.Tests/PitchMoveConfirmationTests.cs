using FM27Access.Plugin.Tactics;

namespace FM27Access.Tests;

public class PitchMoveConfirmationTests
{
    private static Dictionary<string, string> Formation(params (string Slot, string Player)[] rows)
        => rows.ToDictionary(x => x.Slot, x => x.Player);

    [Fact]
    public void EmptyDestination_RequiresArrivalAndTheMoverGoneFromTheSource()
    {
        var before = Formation(("mc", "Ana"));

        Assert.False(PitchMoveConfirmation.IsApplied(before, Formation(("mc", "Ana"), ("amc", "Ana")), "mc", "amc"));
        Assert.True(PitchMoveConfirmation.IsApplied(before, Formation(("amc", "Ana")), "mc", "amc"));
    }

    [Fact]
    public void Swap_DoesNotAcceptIntermediateEmptySourceFrame()
    {
        var before = Formation(("mc", "Ana"), ("amc", "Bia"));

        Assert.False(PitchMoveConfirmation.IsApplied(before, Formation(("amc", "Ana")), "mc", "amc"));
        Assert.True(PitchMoveConfirmation.IsApplied(before, Formation(("mc", "Bia"), ("amc", "Ana")), "mc", "amc"));
    }

    // Choosing a position can make the game re-fit the rest of the lineup, so a move into an empty
    // slot can end with somebody ELSE standing where the mover started. That is the move having
    // landed, not having failed — holding out for an empty source would time out and tell the
    // player their change did not happen.
    [Fact]
    public void EmptyDestination_AcceptsASourceBackfilledByAReFit()
    {
        var before = Formation(("mc", "Ana"), ("dm", "Cleo"));
        var current = Formation(("amc", "Ana"), ("mc", "Cleo"));

        Assert.True(PitchMoveConfirmation.IsApplied(before, current, "mc", "amc"));
    }

    [Fact]
    public void UnreadableSource_CanNeverBeReportedAsConfirmed()
    {
        var before = Formation(("amc", "Bia"));
        var current = Formation(("mc", "Bia"));

        Assert.False(PitchMoveConfirmation.IsApplied(before, current, "mc", "amc"));
    }
}

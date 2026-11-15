using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// Locks the multi-panel refresh combiner (Phase 3, docs/archive/plans/removing panel cycling plan.md §7.4): the
/// SSE loop watches one value built from the interactive set's per-panel tokens/shapes, so the
/// combiner must move whenever the set's membership, order, or any member's signature moves, and
/// must be stable otherwise.
/// </summary>
public class AggregateSignatureTests
{
    [Fact]
    public void Stable_ForEqualInputs()
    {
        Assert.Equal(
            AggregateSignature.Combine(new[] { "a", "b" }),
            AggregateSignature.Combine(new[] { "a", "b" }));
    }

    [Fact]
    public void OrderSensitive_ReorderingMovesTheValue()
    {
        Assert.NotEqual(
            AggregateSignature.Combine(new[] { "a", "b" }),
            AggregateSignature.Combine(new[] { "b", "a" }));
    }

    [Fact]
    public void MembershipChange_AddingAPanelMovesTheValue()
    {
        // A card opening (the set gains a panel) must change the value even when the existing
        // panel's own token is unchanged — this is what triggers the refresh without a manual F5.
        Assert.NotEqual(
            AggregateSignature.Combine(new[] { "a" }),
            AggregateSignature.Combine(new[] { "a", "b" }));
    }

    [Fact]
    public void Delimited_DifferentPartitionsDoNotCollide()
    {
        // Without a delimiter, ["a","b"] and ["ab"] would both concatenate to "ab" and a real set
        // change would read as no change.
        Assert.NotEqual(
            AggregateSignature.Combine(new[] { "a", "b" }),
            AggregateSignature.Combine(new[] { "ab" }));
    }

    [Fact]
    public void MemberChange_OnePanelsTokenMovingMovesTheValue()
    {
        Assert.NotEqual(
            AggregateSignature.Combine(new[] { "a", "b" }),
            AggregateSignature.Combine(new[] { "a", "b2" }));
    }

    [Fact]
    public void Empty_IsEmptyString()
    {
        Assert.Equal("", AggregateSignature.Combine(new string[0]));
    }

    [Fact]
    public void Single_IsThePartItself()
    {
        Assert.Equal("x", AggregateSignature.Combine(new[] { "x" }));
    }
}

using System.Collections.Generic;
using System.Linq;
using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class PitchNavigationTests
{
    // A 4-3-3-ish formation: GK, a back four (no DC), a lone DMC, two central mids, two attacking
    // wingers, a lone ST. Several rows are sparse, which is what makes the nearest/skip rules matter.
    private static readonly HashSet<string> Formation = new()
    {
        "GK", "DL", "DCL", "DCR", "DR", "DMC", "MCL", "MCR", "AML", "AMR", "ST",
    };

    private static HashSet<string> AllSlots() =>
        new(PitchNavigation.Rows.SelectMany(r => r));

    [Theory]
    [InlineData("GK", "Goalkeeper")]           // single-slot row: no flank suffix
    [InlineData("DCR", "Defence centre right")]
    [InlineData("WBL", "Left Wing Back")]      // the game's own name for the wide DM-row slots
    [InlineData("WBR", "Right Wing Back")]
    [InlineData("DML", "Defensive Midfield centre left")]
    [InlineData("MC", "Midfield centre")]
    [InlineData("AMR", "Attacking Midfield right")]
    [InlineData("STL", "Attack centre left")]  // wide strikers sit inside the winger lanes
    public void SlotPhrase_NamesRowAndFlankInPlainWords(string slot, string expected)
    {
        Assert.Equal(expected, PitchNavigation.SlotPhrase(slot));
    }

    [Fact]
    public void SlotPhrase_UnrecognisedSlot_ReturnsNullNotAGuess()
    {
        Assert.Null(PitchNavigation.SlotPhrase("BOGUS"));
        Assert.Null(PitchNavigation.SlotPhrase(null));
    }

    [Fact]
    public void Right_SkipsEmptySlotsWithinRow()
    {
        // DC is empty, so right from DCL lands on DCR, not DC.
        Assert.Equal("DCR", PitchNavigation.Neighbor(Formation, "DCL", PitchNavDir.Right));
    }

    [Fact]
    public void Left_MovesToAdjacentPresentSlot()
    {
        Assert.Equal("DL", PitchNavigation.Neighbor(Formation, "DCL", PitchNavDir.Left));
    }

    [Fact]
    public void Horizontal_DoesNotWrap()
    {
        Assert.Null(PitchNavigation.Neighbor(Formation, "DL", PitchNavDir.Left));
        Assert.Null(PitchNavigation.Neighbor(Formation, "DR", PitchNavDir.Right));
    }

    [Fact]
    public void Up_FromKeeper_LandsNearestDefender_TieGoesLeft()
    {
        // GK is centre (col 2); DC is empty so the nearest defenders are DCL (col 1) and DCR (col 3),
        // an equal distance — the tie resolves left.
        Assert.Equal("DCL", PitchNavigation.Neighbor(Formation, "GK", PitchNavDir.Up));
    }

    [Fact]
    public void Down_FromStriker_LandsNearestAttackingMid()
    {
        // ST centre; AML (col 0) and AMR (col 4) are equidistant, tie left.
        Assert.Equal("AML", PitchNavigation.Neighbor(Formation, "ST", PitchNavDir.Down));
    }

    [Fact]
    public void Up_SkipsAnEmptyRow()
    {
        // Drop the lone DMC so the defensive-midfield row is empty; up from DCL must skip it and
        // reach the midfield row (MCL nearest to col 1).
        var noDm = new HashSet<string>(Formation);
        noDm.Remove("DMC");
        Assert.Equal("MCL", PitchNavigation.Neighbor(noDm, "DCL", PitchNavDir.Up));
    }

    [Fact]
    public void Down_FromKeeper_IsNoOp()
    {
        Assert.Null(PitchNavigation.Neighbor(Formation, "GK", PitchNavDir.Down));
    }

    [Fact]
    public void Up_FromStriker_IsNoOp()
    {
        Assert.Null(PitchNavigation.Neighbor(Formation, "ST", PitchNavDir.Up));
    }

    [Fact]
    public void MoveMode_AllSlotsPresent_UpFromKeeperHitsCentreBack()
    {
        var all = AllSlots();
        Assert.Equal("DC", PitchNavigation.Neighbor(all, "GK", PitchNavDir.Up));
    }

    [Fact]
    public void MoveMode_RightTraversesEveryColumn()
    {
        var all = AllSlots();
        Assert.Equal("DCL", PitchNavigation.Neighbor(all, "DL", PitchNavDir.Right));
        Assert.Equal("DC", PitchNavigation.Neighbor(all, "DCL", PitchNavDir.Right));
    }

    [Fact]
    public void UnknownSlot_ReturnsNull()
    {
        Assert.Null(PitchNavigation.Neighbor(Formation, "NOPE", PitchNavDir.Up));
    }
}

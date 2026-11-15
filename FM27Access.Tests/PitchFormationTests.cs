using System.Collections.Generic;
using System.Linq;
using FM27Access.Navigation;
using FM27Access.Speech;
using Xunit;

namespace FM27Access.Tests;

public class PitchFormationTests
{
    [Fact]
    public void NativeShapeOnlyPositions_PreserveGeometryWithoutInventingPlayerIdentity()
    {
        var formation = PitchFormation.FromPlayers(new[]
        {
            new PitchPlayer("DR", null, null, null, positionOnly: true),
            new PitchPlayer("GK", null, null, null, positionOnly: true),
            new PitchPlayer("DL", null, null, null, positionOnly: true),
        });
        Assert.True(formation.IsPositionOnly);
        Assert.Equal(new[] { "GK", "DL", "DR" }, formation.Players.Select(p => p.Slot));
        Assert.Equal("Goalkeeper", PitchFormation.Describe(formation.Players[0]));
        Assert.Equal("Defence left", PitchFormation.Describe(formation.Players[1]));
        Assert.Equal(new[] { "Goalkeeper.", "Defence, left to right: left, right." }, formation.OverviewLines());
        Assert.Equal("DL", PitchNavigation.Neighbor(formation.OccupiedSlots(), "GK", PitchNavDir.Up));
    }

    [Fact]
    public void OrdinaryUnreadablePlayer_IsNotReclassifiedAsAnAnonymousPosition()
    {
        var formation = PitchFormation.FromPlayers(new[] { new PitchPlayer("GK", null, null, null) });
        Assert.False(formation.IsPositionOnly);
        Assert.NotEqual("Goalkeeper", PitchFormation.Describe(formation.Players[0]));
    }

    private static PitchPlayer P(string slot, string name = "Name", string role = "ROLE", string number = "1")
        => new PitchPlayer(slot, role, number, name);

    [Fact]
    public void FromPlayers_OrdersBackToFrontThenLeftToRight()
    {
        // Fed in deliberately scrambled order; canonical order is keeper, then each row left to right.
        var formation = PitchFormation.FromPlayers(new[]
        {
            P("ST"), P("DR"), P("GK"), P("AML"), P("DL"), P("MC"),
        });

        Assert.Equal(
            new[] { "GK", "DL", "DR", "MC", "AML", "ST" },
            formation.Players.Select(p => p.Slot));
    }

    [Fact]
    public void FromPlayers_KeepsUnrecognisedSlot_AppendedLast()
    {
        // A slot name not in the 24-slot model must not be dropped (that would hide a player); it is
        // appended after the recognised ones rather than crashing the sort or vanishing.
        var formation = PitchFormation.FromPlayers(new[]
        {
            P("BOGUS"), P("GK"), P("DR"),
        });

        Assert.Equal(new[] { "GK", "DR", "BOGUS" }, formation.Players.Select(p => p.Slot));
    }

    [Fact]
    public void OccupiedSlots_ExcludesUnrecognised_AndDrivesNeighbourGeometry()
    {
        var formation = PitchFormation.FromPlayers(new[] { P("GK"), P("DCL"), P("DCR"), P("BOGUS") });
        var occupied = formation.OccupiedSlots();

        Assert.DoesNotContain("BOGUS", occupied);
        // The occupied set is exactly what PitchNavigation consumes: with DC empty, up from GK
        // (centre) resolves to the nearest present defender, tie going left.
        Assert.Equal("DCL", PitchNavigation.Neighbor(occupied, "GK", PitchNavDir.Up));
    }

    [Fact]
    public void ByRow_GroupsOccupiedOnly_WithLabels_SkippingEmptyRows()
    {
        var formation = PitchFormation.FromPlayers(new[] { P("GK"), P("DL"), P("DR"), P("ST") });
        var rows = formation.ByRow();

        // Defensive Midfield / Midfield / Attacking Midfield are empty, so only three rows surface.
        Assert.Equal(new[] { "Goalkeeper", "Defence", "Attack" }, rows.Select(r => r.RowLabel));
        Assert.Equal(new[] { "DL", "DR" }, rows[1].Players.Select(p => p.Slot));
    }

    [Fact]
    public void IsEmpty_TrueWhenNoSlotsRead()
    {
        Assert.True(PitchFormation.FromPlayers(System.Array.Empty<PitchPlayer>()).IsEmpty);
        Assert.False(PitchFormation.FromPlayers(new[] { P("GK") }).IsEmpty);
    }

    [Fact]
    public void Describe_ComposesPositionNameRoleNumber()
    {
        // The position leads in plain words, not the raw slot code.
        Assert.Equal("Defence centre right, Worrall, BCB, 4",
            PitchFormation.Describe(new PitchPlayer("DCR", "BCB", "4", "Worrall")));
    }

    [Fact]
    public void Describe_UnrecognisedSlot_FallsBackToRawCode()
    {
        // An unknown slot keeps its raw code — honest, and it still identifies the element.
        Assert.Equal("BOGUS, Worrall",
            PitchFormation.Describe(new PitchPlayer("BOGUS", role: null, number: null, name: "Worrall")));
    }

    [Fact]
    public void Describe_NumberOnlySlot_SpeaksNumberIdentity_NoMarker()
    {
        // Numbers-only pitches (the Squad Overview tile) bind no name; that is the game's own
        // presentation, so the identity is the number — an honest read, not a degrade.
        Assert.Equal("Goalkeeper, number 13, GK",
            PitchFormation.Describe(new PitchPlayer("GK", "GK", "13", name: null)));
    }

    [Fact]
    public void Describe_NoNameNoNumber_EmitsMarkerNeverBlank()
    {
        // A slot with no readable identity at all must announce a gap, not a blank that reads as
        // "no player".
        var label = PitchFormation.Describe(new PitchPlayer("GK", "GK", number: null, name: null));

        Assert.Contains(Degrade.Unreadable, label);
        Assert.StartsWith("Goalkeeper,", label);
    }

    [Fact]
    public void Describe_OmitsUnreadOptionalFields_ButKeepsName()
    {
        // Role and number unread: they drop out cleanly, leaving position + name with no stray commas.
        Assert.Equal("Attacking Midfield left, Anthony",
            PitchFormation.Describe(new PitchPlayer("AML", role: null, number: null, name: "Anthony")));
    }

    [Fact]
    public void OverviewLines_OneProseLinePerRow_OwnGoalOutward()
    {
        // A 4-1-2-1-2: every occupied row becomes one line, keeper first, flanks in plain words.
        var formation = PitchFormation.FromPlayers(new[]
        {
            new PitchPlayer("GK", "GK", "15", "Beresford"),
            new PitchPlayer("DL", "FB", "19", "Briscoe"),
            new PitchPlayer("DCL", "CB", "23", "McGregor"),
            new PitchPlayer("DCR", "CB", "2", "West"),
            new PitchPlayer("DR", "FB", "6", "Armstrong"),
            new PitchPlayer("DMC", "DM", "12", "Grant"),
            new PitchPlayer("MCL", "CM", "18", "Weller"),
            new PitchPlayer("MCR", "CM", "7", "Little"),
            new PitchPlayer("AMC", "AP", "8", "Cook"),
            new PitchPlayer("STL", "AF", "27", "Blake"),
            new PitchPlayer("STR", "AF", "13", "Branch"),
        });

        Assert.Equal(new[]
        {
            "Goalkeeper: 15 Beresford.",
            "Defence, left to right: 19 Briscoe (left), 23 McGregor (centre left), 2 West (centre right), 6 Armstrong (right).",
            "Defensive Midfield: 12 Grant (centre).",
            "Midfield, left to right: 18 Weller (centre left), 7 Little (centre right).",
            "Attacking Midfield: 8 Cook (centre).",
            "Attack, left to right: 27 Blake (centre left), 13 Branch (centre right).",
        }, formation.OverviewLines());
    }

    [Fact]
    public void OverviewLines_UnrecognisedSlot_GetsPositionUnknownLine()
    {
        // A player in a slot outside the 24-slot model must still appear in the summary.
        var formation = PitchFormation.FromPlayers(new[]
        {
            new PitchPlayer("GK", "GK", "1", "Keeper"),
            new PitchPlayer("BOGUS", null, "9", "Stray"),
        });

        Assert.Equal(new[]
        {
            "Goalkeeper: 1 Keeper.",
            "Position unknown: 9 Stray.",
        }, formation.OverviewLines());
    }

    [Fact]
    public void OverviewLines_NumberOnlyFormation_ReadsAsNumbersWithPositions()
    {
        // The Squad Overview tile binds numbers but no names: the overview still gives every
        // number its position — the context the bare tile lacks — with no degrade markers.
        var formation = PitchFormation.FromPlayers(new[]
        {
            new PitchPlayer("GK", null, "1", null),
            new PitchPlayer("DCL", null, "5", null),
            new PitchPlayer("DCR", null, "12", null),
        });

        Assert.Equal(new[]
        {
            "Goalkeeper: number 1.",
            "Defence, left to right: number 5 (centre left), number 12 (centre right).",
        }, formation.OverviewLines());
    }

    [Fact]
    public void OverviewLines_MissingIdentity_DegradesInPlace()
    {
        // No number: the name stands alone. Neither name nor number: the marker appears, never a
        // blank.
        var formation = PitchFormation.FromPlayers(new[]
        {
            new PitchPlayer("GK", "GK", null, "Keeper"),
            new PitchPlayer("ST", null, null, null),
        });

        var lines = formation.OverviewLines();
        Assert.Equal("Goalkeeper: Keeper.", lines[0]);
        Assert.Equal($"Attack: {Degrade.Unreadable} (centre).", lines[1]);
    }
}

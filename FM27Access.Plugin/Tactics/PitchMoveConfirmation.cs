using System.Collections.Generic;

#nullable disable

namespace FM27Access.Plugin.Tactics;

/// <summary>
/// Pure confirmation policy for an asynchronously-applied tactics pitch move: given the pitch
/// occupants read before the move and the occupants read now, has the whole move landed?
///
/// The engine applies a move in halves across frames, and both halves matter. Into an EMPTY slot
/// the mover must have arrived and no longer be standing in the source as well — the frame where
/// it reads in both is the first half. Into an OCCUPIED slot it is a swap, and the partner must
/// have reached the source too: the frame showing the mover at the destination over an empty
/// source is still intermediate, and because a swap leaves the panel shape identical, replying
/// there leaves the companion holding a formation the game has already moved past.
/// </summary>
internal static class PitchMoveConfirmation
{
    internal static bool IsApplied(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> current,
        string source,
        string destination)
    {
        string movedPlayer = Occupant(before, source);
        if (string.IsNullOrEmpty(movedPlayer)) return false;

        string previousDestinationPlayer = Occupant(before, destination);
        if (Occupant(current, destination) != movedPlayer) return false;

        // Still in the slot it left: the destination half applied, the source half has not.
        string currentSourcePlayer = Occupant(current, source);
        if (currentSourcePlayer == movedPlayer) return false;

        // A swap is only whole once the partner has taken the vacated slot. A move into an empty
        // slot asks nothing more of the source: choosing a position can make the game re-fit the
        // rest of the lineup, and demanding the source stay empty would read that backfill as a
        // move that never landed — reporting failure for a change the player did in fact make.
        return string.IsNullOrEmpty(previousDestinationPlayer)
            || currentSourcePlayer == previousDestinationPlayer;
    }

    private static string Occupant(IReadOnlyDictionary<string, string> occupants, string slot)
        => occupants != null && occupants.TryGetValue(slot, out var value) ? value : null;
}

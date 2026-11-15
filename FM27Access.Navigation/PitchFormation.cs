using System.Collections.Generic;
using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// One player occupying a pitch slot, read from a single FMPitchIndicator. Every field is plain
/// text the reader has already stripped of rich-text markup; a field the reader could not read is
/// null, and <see cref="PitchFormation.Describe"/> turns that into an honest gap marker rather than
/// dropping the player. This is a game-free value type so the formation logic stays unit-testable —
/// the VisualElement read that produces it lives in the Plugin.
/// </summary>
public readonly struct PitchPlayer
{
    public PitchPlayer(string slot, string role, string number, string name, bool positionOnly = false)
    {
        Slot = slot;
        Role = role;
        Number = number;
        Name = name;
        IsPositionOnly = positionOnly;
    }

    /// <summary>FMPitchIndicator.name — one of the 24 slot codes ("GK", "DCR", "AML").</summary>
    public string Slot { get; }

    /// <summary>Tactical role code ("GK", "BCB", "CFD"); null when unread.</summary>
    public string Role { get; }

    /// <summary>Shirt number; null when unread.</summary>
    public string Number { get; }

    /// <summary>Player surname; null when unread.</summary>
    public string Name { get; }

    /// <summary>A native shape marker describes a position, without assigning a player.</summary>
    public bool IsPositionOnly { get; }
}

/// <summary>
/// The occupied slots of one pitch formation, ordered into the canonical back-to-front, left-to-right
/// reading order shared by both surfaces. Built from raw per-slot reads (see the Plugin's pitch
/// reader); holds no live game state, so it is freely passed across the speech and HTML walks. The
/// 24-slot geometry and the row labels come from <see cref="PitchNavigation"/> — this type adds only
/// the occupied-player model on top, so the layout lives in exactly one place.
/// </summary>
public sealed class PitchFormation
{
    private readonly List<PitchPlayer> _players;

    private PitchFormation(List<PitchPlayer> players) => _players = players;

    /// <summary>Players in canonical reading order (keeper first, then each row left to right).</summary>
    public IReadOnlyList<PitchPlayer> Players => _players;

    /// <summary>No occupied slot could be read — the caller degrades to "formation unavailable".</summary>
    public bool IsEmpty => _players.Count == 0;

    public bool IsPositionOnly => _players.Count > 0 && _players.TrueForAll(p => p.IsPositionOnly);

    /// <summary>
    /// Order raw per-slot reads into the canonical formation. Players whose slot is one of the 24
    /// known positions sort by row (back to front) then column (left to right); a player whose slot
    /// is unrecognised is kept — appended after the recognised ones in read order, with a degrade
    /// note — so an unexpected slot name surfaces to the player rather than silently vanishing.
    /// </summary>
    public static PitchFormation FromPlayers(IEnumerable<PitchPlayer> players)
    {
        var known = new List<(int Row, int Col, PitchPlayer Player)>();
        var unknown = new List<PitchPlayer>();
        foreach (var p in players)
        {
            if (p.Slot != null && PitchNavigation.TryGetCoord(p.Slot, out int row, out int col))
                known.Add((row, col, p));
            else
            {
                Degrade.Note($"pitch: unrecognised slot '{p.Slot}', appended out of order");
                unknown.Add(p);
            }
        }

        known.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));

        var ordered = new List<PitchPlayer>(known.Count + unknown.Count);
        foreach (var k in known) ordered.Add(k.Player);
        ordered.AddRange(unknown);
        return new PitchFormation(ordered);
    }

    /// <summary>
    /// The recognised occupied slots, for feeding <see cref="PitchNavigation.Neighbor"/> (the HTML
    /// grid's arrow geometry) and rendering occupancy. Unrecognised slots are excluded — the geometry
    /// only knows the 24 known positions.
    /// </summary>
    public IReadOnlyCollection<string> OccupiedSlots()
    {
        var set = new HashSet<string>();
        foreach (var p in _players)
            if (p.Slot != null && PitchNavigation.TryGetCoord(p.Slot, out _, out _))
                set.Add(p.Slot);
        return set;
    }

    /// <summary>
    /// The occupied players grouped into the canonical rows, each with its row label
    /// ("Goalkeeper", "Defence", …), for a row-structured presentation: speech headings that break
    /// up the flat blob, and the HTML grid's rows. Rows with no occupied slot are omitted, and
    /// unrecognised slots (which belong to no row) do not appear here.
    /// </summary>
    public IReadOnlyList<(string RowLabel, IReadOnlyList<PitchPlayer> Players)> ByRow()
    {
        var result = new List<(string, IReadOnlyList<PitchPlayer>)>();
        for (int r = 0; r < PitchNavigation.Rows.Length; r++)
        {
            var inRow = new List<PitchPlayer>();
            foreach (var p in _players)
                if (p.Slot != null && PitchNavigation.TryGetCoord(p.Slot, out int pr, out _) && pr == r)
                    inRow.Add(p);
            if (inRow.Count > 0)
                result.Add((PitchNavigation.RowLabels[r], inRow));
        }
        return result;
    }

    /// <summary>
    /// One player's label for both surfaces: position in plain words ("Defence centre right"; the
    /// raw slot code when unrecognised), then identity, role, and shirt number in that order,
    /// joined with ", ". The identity is the name when the surface shows one; on numbers-only
    /// pitches (the Squad Overview tile) it is "number 27" — the game's own presentation, not a
    /// gap. Only a slot with neither name nor number degrades to the explicit marker, so the
    /// player hears a gap, never silence. Mod-authored joining only; the field text is the game's own.
    /// </summary>
    public static string Describe(PitchPlayer p)
    {
        var parts = new List<string>(4);
        string position = PitchNavigation.SlotPhrase(p.Slot);
        if (position != null) parts.Add(position);
        else if (!string.IsNullOrEmpty(p.Slot)) parts.Add(p.Slot);
        if (!p.IsPositionOnly) parts.Add(Identity(p));
        if (!string.IsNullOrEmpty(p.Role)) parts.Add(p.Role);
        if (!string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.Number)) parts.Add(p.Number);
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The formation as linear prose, one line per occupied row, own goal outward — "Defence, left
    /// to right: 19 Briscoe (left), 2 West (centre right)." — the at-a-glance shape the
    /// arrow-navigable grid cannot give. Flanks come from the slot geometry; depth is conveyed by
    /// the row names alone, because the discrete slots carry no truthful percentage. A player in an
    /// unrecognised slot gets a "Position unknown" line rather than vanishing from the summary.
    /// </summary>
    public IReadOnlyList<string> OverviewLines()
    {
        var lines = new List<string>();
        foreach (var (label, players) in ByRow())
        {
            if (IsPositionOnly)
            {
                var flanks = new List<string>();
                foreach (var p in players)
                {
                    string flank = FlankOf(p);
                    if (flank != null) flanks.Add(flank);
                }
                lines.Add(flanks.Count == 0 ? label + "."
                    : label + ", left to right: " + string.Join(", ", flanks) + ".");
                continue;
            }
            if (players.Count == 1)
            {
                string flank = FlankOf(players[0]);
                lines.Add(flank == null
                    ? $"{label}: {Who(players[0])}."
                    : $"{label}: {Who(players[0])} ({flank}).");
            }
            else
            {
                var described = new List<string>(players.Count);
                foreach (var p in players) described.Add($"{Who(p)} ({FlankOf(p)})");
                lines.Add($"{label}, left to right: {string.Join(", ", described)}.");
            }
        }
        foreach (var p in _players)
            if (p.Slot == null || !PitchNavigation.TryGetCoord(p.Slot, out _, out _))
                lines.Add($"Position unknown: {Who(p)}.");
        return lines;
    }

    // Flank for a player already placed by ByRow (so the slot is always recognised); null on a
    // single-slot row, where a flank would be noise.
    private static string FlankOf(PitchPlayer p)
    {
        PitchNavigation.TryGetCoord(p.Slot, out int row, out int col);
        return PitchNavigation.Rows[row].Length == 1 ? null : PitchNavigation.FlankLabels[col];
    }

    // Shirt number and surname ("23 McGregor") — the overview's per-player identity. On a
    // numbers-only pitch the identity is "number 23"; a slot with neither degrades to the marker,
    // same as Describe.
    private static string Who(PitchPlayer p)
    {
        if (!string.IsNullOrEmpty(p.Name))
            return string.IsNullOrEmpty(p.Number) ? p.Name : p.Number + " " + p.Name;
        return Identity(p);
    }

    // The nameless identity: "number 27" when the surface shows only shirt numbers (truthful — the
    // game binds no name there), the unreadable marker when the slot offers nothing at all.
    private static string Identity(PitchPlayer p)
    {
        if (p.IsPositionOnly) return PitchNavigation.SlotPhrase(p.Slot) ?? p.Slot;
        if (!string.IsNullOrEmpty(p.Name)) return p.Name;
        if (!string.IsNullOrEmpty(p.Number)) return "number " + p.Number;
        return Degrade.Read("pitch: player identity unreadable");
    }
}

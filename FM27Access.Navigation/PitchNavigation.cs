using System.Collections.Generic;

namespace FM27Access.Navigation;

public enum PitchNavDir { Up, Down, Left, Right }

/// <summary>
/// Pure spatial model of the tactics pitch's 24 fixed slots, used to drive 2D arrow navigation of
/// the HTML pitch grid. Slots sit on six rows (goalkeeper at the back through attack at the front)
/// and five columns (left, left-centre, centre, right-centre, right). Navigation is computed over a
/// caller-supplied set of "present" slots — the eleven occupied positions when browsing the
/// formation, or all twenty-four when choosing a new position — so the same geometry serves both
/// modes. Game-free, so the row/column layout and the neighbour rules are unit-testable.
/// </summary>
public static class PitchNavigation
{
    // Rows back-to-front; within a row, slots left-to-right. The column each slot sits in is its
    // index into a five-wide lane (0=left … 4=right); GK and the strikers are centred.
    public static readonly string[][] Rows =
    {
        new[] { "GK" },
        new[] { "DL", "DCL", "DC", "DCR", "DR" },
        new[] { "WBL", "DML", "DMC", "DMR", "WBR" },
        new[] { "ML", "MCL", "MC", "MCR", "MR" },
        new[] { "AML", "AMCL", "AMC", "AMCR", "AMR" },
        new[] { "STL", "ST", "STR" },
    };

    public static readonly IReadOnlyList<string> RowLabels = new[]
    {
        "Goalkeeper", "Defence", "Defensive Midfield", "Midfield", "Attacking Midfield", "Attack",
    };

    /// <summary>Plain-words name of each of the five lanes, indexed by column.</summary>
    public static readonly IReadOnlyList<string> FlankLabels = new[]
    {
        "left", "centre left", "centre", "centre right", "right",
    };

    /// <summary>
    /// A slot code in plain words — row label plus flank ("Defence centre right"), or the row label
    /// alone for the goalkeeper's single-slot row. Null for an unrecognised slot, so the caller can
    /// fall back to the raw code rather than guess.
    /// </summary>
    public static string SlotPhrase(string slot)
    {
        // The game names the wide slots of the defensive-midfield row wing backs (the WB (L) and
        // WB (R) positions), not "Defensive Midfield left/right" — use its terminology so the
        // grids match what the game calls the position everywhere else.
        if (slot == "WBL") return "Left Wing Back";
        if (slot == "WBR") return "Right Wing Back";
        if (!TryGetCoord(slot, out int row, out int col)) return null;
        return Rows[row].Length == 1 ? RowLabels[row] : RowLabels[row] + " " + FlankLabels[col];
    }

    private static readonly Dictionary<string, (int Row, int Col)> _coords = BuildCoords();

    private static Dictionary<string, (int, int)> BuildCoords()
    {
        // Five-wide lanes. The full back lines occupy 0..4; GK and ST are centred; the wide
        // strikers (STL/STR) sit just inside the wingers so an up/down move lands intuitively.
        var cols = new Dictionary<string, int>
        {
            ["GK"] = 2,
            ["DL"] = 0, ["DCL"] = 1, ["DC"] = 2, ["DCR"] = 3, ["DR"] = 4,
            ["WBL"] = 0, ["DML"] = 1, ["DMC"] = 2, ["DMR"] = 3, ["WBR"] = 4,
            ["ML"] = 0, ["MCL"] = 1, ["MC"] = 2, ["MCR"] = 3, ["MR"] = 4,
            ["AML"] = 0, ["AMCL"] = 1, ["AMC"] = 2, ["AMCR"] = 3, ["AMR"] = 4,
            ["STL"] = 1, ["ST"] = 2, ["STR"] = 3,
        };
        var result = new Dictionary<string, (int, int)>();
        for (int r = 0; r < Rows.Length; r++)
            foreach (var slot in Rows[r])
                result[slot] = (r, cols[slot]);
        return result;
    }

    public static bool TryGetCoord(string slot, out int row, out int col)
    {
        if (slot != null && _coords.TryGetValue(slot, out var c)) { row = c.Row; col = c.Col; return true; }
        row = col = -1;
        return false;
    }

    /// <summary>
    /// The slot reached by pressing <paramref name="dir"/> from <paramref name="current"/>, limited
    /// to <paramref name="present"/>. Left/right stay within the current row and do not wrap; up/down
    /// jump to the nearest present slot in the closest non-empty row that direction (skipping rows
    /// with no present slot). Returns null when there is nowhere to go (the move is a no-op).
    /// </summary>
    public static string Neighbor(IReadOnlyCollection<string> present, string current, PitchNavDir dir)
    {
        if (present == null || current == null || !_coords.TryGetValue(current, out var cur)) return null;
        var presentSet = present as ISet<string> ?? new HashSet<string>(present);

        // Goalkeeper is row 0 (bottom of the screen), attack is the last row (top), so pressing Up
        // moves toward higher row indices and Down toward lower ones.
        return dir switch
        {
            PitchNavDir.Left => Horizontal(presentSet, cur, step: -1),
            PitchNavDir.Right => Horizontal(presentSet, cur, step: +1),
            PitchNavDir.Up => Vertical(presentSet, cur, step: +1),
            PitchNavDir.Down => Vertical(presentSet, cur, step: -1),
            _ => null,
        };
    }

    private static string Horizontal(ISet<string> present, (int Row, int Col) cur, int step)
    {
        string best = null;
        int bestCol = step > 0 ? int.MaxValue : int.MinValue;
        foreach (var slot in Rows[cur.Row])
        {
            if (!present.Contains(slot)) continue;
            int col = _coords[slot].Col;
            bool onTheRightSide = step > 0 ? col > cur.Col : col < cur.Col;
            if (!onTheRightSide) continue;
            // Nearest in the step direction: smallest column to the right, largest to the left.
            bool closer = step > 0 ? col < bestCol : col > bestCol;
            if (closer) { best = slot; bestCol = col; }
        }
        return best;
    }

    private static string Vertical(ISet<string> present, (int Row, int Col) cur, int step)
    {
        for (int r = cur.Row + step; r >= 0 && r < Rows.Length; r += step)
        {
            string best = null;
            int bestDist = int.MaxValue, bestCol = int.MaxValue;
            foreach (var slot in Rows[r])
            {
                if (!present.Contains(slot)) continue;
                int col = _coords[slot].Col;
                int dist = col >= cur.Col ? col - cur.Col : cur.Col - col;
                // Nearest column; ties resolve to the left (smaller column) for stable behaviour.
                if (dist < bestDist || (dist == bestDist && col < bestCol))
                {
                    best = slot; bestDist = dist; bestCol = col;
                }
            }
            if (best != null) return best; // first non-empty row this direction wins
        }
        return null;
    }
}

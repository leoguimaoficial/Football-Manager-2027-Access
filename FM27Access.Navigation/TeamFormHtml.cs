using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Formats a standings table's team form cell for the HTML surface. The game binds each pip to a
/// <c>teamform</c> value and stores them oldest-first; the player wants most-recent-first with
/// consecutive identical results collapsed into a count: W W L D D reads "2W, L, 2D".
///
/// Value mapping, verified against the USS class the game applies to every pip on a live table:
/// 3 = win (team-form-win), 1 and 2 = draw (both team-form-draw — the game shows no difference),
/// 0 = defeat (team-form-defeat). A null slot (would not read) or any other value renders as the
/// "unavailable" gap marker in its chronological position and never joins a run, so a gap can
/// never be counted as a result.
/// </summary>
public static class TeamFormHtml
{
    public readonly struct Letters
    {
        public readonly string Win;
        public readonly string Draw;
        public readonly string Loss;

        public Letters(string win, string draw, string loss)
        {
            Win = win;
            Draw = draw;
            Loss = loss;
        }
    }

    public static string Format(IReadOnlyList<int?> resultsOldestFirst, Letters letters)
    {
        var sb = new StringBuilder();
        string run = null;
        int count = 0;
        for (int i = resultsOldestFirst.Count - 1; i >= 0; i--)
        {
            string token = Token(resultsOldestFirst[i], letters);
            if (token != null && token == run)
            {
                count++;
                continue;
            }
            Flush(sb, run, count);
            if (token == null)
            {
                Append(sb, Degrade.Unavailable);
                run = null;
                count = 0;
            }
            else
            {
                run = token;
                count = 1;
            }
        }
        Flush(sb, run, count);
        return sb.ToString();
    }

    private static string Token(int? value, Letters letters) => value switch
    {
        3 => letters.Win,
        1 or 2 => letters.Draw,
        0 => letters.Loss,
        _ => null,
    };

    private static void Flush(StringBuilder sb, string run, int count)
    {
        if (run == null) return;
        Append(sb, count > 1 ? count.ToString(CultureInfo.InvariantCulture) + run : run);
    }

    private static void Append(StringBuilder sb, string part)
    {
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(part);
    }
}

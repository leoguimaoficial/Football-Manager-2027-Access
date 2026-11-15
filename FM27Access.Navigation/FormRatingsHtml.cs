using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Formats a roster-table form cell's match ratings for the HTML surface. The game stores the
/// pips oldest-first (verified against dated season results — see docs/form-column-html-plan.md);
/// the player wants them most-recent-first, so this reverses. Ratings arrive on the game's
/// MatchRating100 scale (rating × 10: 87 = 8.7) and render to one decimal. A null slot is a pip
/// whose binding existed but whose rating would not read — it renders as the standard
/// "unavailable" gap marker in its chronological position rather than silently vanishing, which
/// would shift every other rating's place in the sequence.
/// </summary>
public static class FormRatingsHtml
{
    public static string Format(IReadOnlyList<int?> ratings100OldestFirst)
    {
        var sb = new StringBuilder();
        for (int i = ratings100OldestFirst.Count - 1; i >= 0; i--)
        {
            if (sb.Length > 0) sb.Append(", ");
            var rating100 = ratings100OldestFirst[i];
            sb.Append(rating100.HasValue
                ? (rating100.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture)
                : Degrade.Unavailable);
        }
        return sb.ToString();
    }
}

#nullable enable

namespace FM27Access.Plugin.Adapters;

// Describe the actual glyph, without inferring an amount or a rate of change.
// AttributeChange's numerical Up 1 contract belongs only to attribute rows.
internal static class ImageStyleLabel
{
    // The standings template draws these beside the numerical position. Name
    // the actual glyph, without inventing a number of places gained or lost.
    internal static string? RankingArrow(string? sprite)
    {
        if (sprite?.StartsWith("chevron top_", StringComparison.OrdinalIgnoreCase) == true)
            return "Upward arrow";
        if (sprite?.StartsWith("chevron down_", StringComparison.OrdinalIgnoreCase) == true)
            return "Downward arrow";
        if (sprite?.StartsWith("line_", StringComparison.OrdinalIgnoreCase) == true)
            return "Horizontal line";
        return null;
    }

    internal static string? For(string? name) => name == "ui-concepts-double_chevron_top_green"
        ? "Double upward arrow (green)"
        : null;
}

using System.Text.RegularExpressions;

namespace FM27Access.Plugin.Adapters;

// A complete native percentage value, never a monetary amount or field label.
internal static class ProgressCaptionValue
{
    private static readonly Regex Percentage = new(@"^\s*[+\-\u2212]?\p{Nd}+(?:[.,\u066b]\p{Nd}+)?\s*[%\u066a]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool IsPercentage(string value) =>
        !string.IsNullOrWhiteSpace(value) && Percentage.IsMatch(value);
}

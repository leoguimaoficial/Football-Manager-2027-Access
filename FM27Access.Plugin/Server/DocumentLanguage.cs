#nullable enable
using System.Globalization;

namespace FM27Access.Plugin.Server;

// TranslationManager supplies a resource name (for example "portuguese"),
// not an HTML language tag. Resolve standard culture names/English names;
// do not guess from the Windows culture or declare unknown resources English.
internal static class DocumentLanguage
{
    private static readonly CultureInfo[] Cultures = CultureInfo.GetCultures(
        CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
        .Where(c => c.Name.Length != 0).ToArray();

    internal static string FromNative(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) return "und";
        string name = resource.Trim();
        // Actual native preference PropertyValue aliases. All other installed
        // resources use standard English language names. These identify game
        // languages, not widget captions or translated companion strings.
        if (name.Equals("english (us)", StringComparison.OrdinalIgnoreCase)) return "en-US";
        if (name.Equals("spanish (latam)", StringComparison.OrdinalIgnoreCase)) return "es-419";
        var exact = Cultures.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Name;
        var names = Cultures.Where(c => string.Equals(c.EnglishName, name,
                StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length == 1 ? names[0] : "und";
    }
}

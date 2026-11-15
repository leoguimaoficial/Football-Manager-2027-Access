using System.Text.RegularExpressions;

namespace FM27Access.Speech;

public static class TextFilters
{
    private static readonly Dictionary<string, string> _spriteNames = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex LinkTagRegex = new(
        @"<link=""[^""]*"">(.*?)</link>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex KnownRichTextTagRegex = new(
        @"</?(?:color|b|i|size|alpha|strikethrough|underline)(?:=[^>]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemainingTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private static readonly Regex WhitespaceRunRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    private static readonly Regex SpriteTagRegex = new(
        @"<sprite\s+[^>]*?name=""([^""]+)""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void RegisterSpriteName(string name, string spoken)
    {
        _spriteNames[name] = spoken;
    }

    internal static void ClearSpriteNames()
    {
        _spriteNames.Clear();
    }

    /// <summary>
    /// The canonical markup-cleaning chain, identical to the order the speech pipeline
    /// registers in FM27AccessPlugin. The HTML surface calls this directly so a string
    /// shown in the browser reads exactly as the speech surface would speak it: sprites
    /// become their spoken names, links collapse to their text, and all rich-text and
    /// stray tags are removed. Single source of truth so the two surfaces never drift.
    /// </summary>
    public static string CleanForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = StripControlCharacters(text);
        text = ConvertSpriteTags(text);
        text = ExtractLinkText(text);
        text = StripKnownRichTextTags(text);
        text = StripRemainingTags(text);
        text = NormalizeWhitespace(text);
        return text;
    }

    public static string ConvertSpriteTags(string text)
    {
        if (text.Length == 0) return text;
        if (!text.Contains("<sprite", StringComparison.OrdinalIgnoreCase)) return text;

        return SpriteTagRegex.Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (_spriteNames.TryGetValue(name, out var spoken))
                return spoken;

            Log.Warn($"Unknown sprite name: \"{name}\". Announcing as \"Unknown sprite\".");
            return "Unknown sprite";
        });
    }

    public static string ExtractLinkText(string text)
    {
        if (text.Length == 0) return text;
        if (!text.Contains("<link", StringComparison.OrdinalIgnoreCase)) return text;

        return LinkTagRegex.Replace(text, "$1");
    }

    public static string StripKnownRichTextTags(string text)
    {
        if (text.Length == 0) return text;
        if (!text.Contains('<')) return text;

        return KnownRichTextTagRegex.Replace(text, "");
    }

    public static string StripRemainingTags(string text)
    {
        if (text.Length == 0) return text;
        if (!text.Contains('<')) return text;

        return RemainingTagRegex.Replace(text, "");
    }

    public static string NormalizeWhitespace(string text)
    {
        if (text.Length == 0) return text;

        // Fast path, mirroring this file's other stages: the regex and Trim allocate even when
        // the text is already in normal form. Normal form = no leading/trailing whitespace, and
        // every interior whitespace char is a single plain ' ' (a run, or any non-space
        // whitespace like a tab or a no-break space, takes the regex path).
        bool needs = char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]);
        if (!needs)
        {
            for (int i = 1; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (c == ' ' ? text[i + 1] == ' ' : char.IsWhiteSpace(c)) { needs = true; break; }
            }
        }
        if (!needs) return text;

        return WhitespaceRunRegex.Replace(text, " ").Trim();
    }

    public static string StripControlCharacters(string text)
    {
        if (text.Length == 0) return text;

        // Fast path: check if any control characters exist before allocating
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
            {
                return StripControlCharactersSlow(text, i);
            }
        }

        return text;
    }

    private static string StripControlCharactersSlow(string text, int firstControlIndex)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        sb.Append(text, 0, firstControlIndex);

        for (int i = firstControlIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsControl(c) || c == '\t' || c == '\n' || c == '\r')
                sb.Append(c);
        }

        return sb.ToString();
    }
}

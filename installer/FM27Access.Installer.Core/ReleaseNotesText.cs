using System.Net;
using System.Text.RegularExpressions;

namespace FM27Access.Installer;

/// <summary>Readable native text from player-authored release Markdown; no HTML execution.</summary>
public static class ReleaseNotesText
{
    private static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement, RegexOptions.None, TimeSpan.FromSeconds(1));

    public static string Format(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        string value = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        value = Replace(value, @"(?s)<!--.*?-->", "");
        value = Replace(value, @"!?\[([^\]\n]*)\]\([^\n)]*\)", "$1");
        value = Replace(value, @"<((?:https?://|mailto:)[^>\n]+)>", "$1");
        value = Replace(value, @"</?[A-Za-z][^>\n]*>", "");
        value = WebUtility.HtmlDecode(value);
        value = Replace(value, @"(?m)^\s{0,3}#{1,6}\s+(.+?)(?:\s+#+)?$", "$1");
        value = Replace(value, @"(?m)^\s{0,3}>\s?", "");
        value = Replace(value, @"(?m)^\s*([-*_])(?:\s*\1){2,}\s*$|^\s*=+\s*$|^\s*(```|~~~).*$", "");
        value = Replace(value, @"(?m)^(\s*)[-*+]\s+", "$1• ");
        value = Replace(value, @"\*\*(.+?)\*\*|__(.+?)__", "$1$2");
        value = Replace(value, @"(?<!\w)[*_]([^*_\n]+)[*_](?!\w)", "$1");
        value = Replace(value, @"`+([^`\n]+)`+", "$1");
        value = Replace(value, @"\\([\\`*_{}\[\]()#+.!>\-])", "$1");
        value = string.Join("\n", value.Split('\n').Select(line => line.TrimEnd()));
        value = Replace(value, @"\n{3,}", "\n\n");
        return value.Trim().Replace("\n", Environment.NewLine);
    }
}

#nullable enable
using System;
using System.Globalization;

namespace FM27Access.Plugin.Server;

// MessageViewHistory's existing opaque comparison string survives Plugin reload.
// Preserve that FM26 API, carrying the FM27 native identifier with its title.
internal static class MessageVerificationKey
{
    private const string Prefix = "\u001eFM27-news:";
    internal static string? Create(int? id, string? subject)
        => id > 0 ? Prefix + id.Value.ToString(CultureInfo.InvariantCulture) + "\u001e" + (subject ?? "") : subject;

    internal static int? Id(string? key)
    {
        if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        int end = key.IndexOf('\u001e', Prefix.Length);
        return end > Prefix.Length && int.TryParse(key.Substring(Prefix.Length, end - Prefix.Length),
            NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : null;
    }

    internal static string? Subject(string? key)
        => Id(key).HasValue ? key!.Substring(key.IndexOf('\u001e', Prefix.Length) + 1) : key;

    internal static bool Matches(string? key, int? id, string? subject)
        => Id(key).HasValue
            ? Id(key) == id && (string.IsNullOrEmpty(Subject(key)) || string.Equals(Subject(key), subject, StringComparison.Ordinal))
            : !string.IsNullOrEmpty(subject) && string.Equals(key, subject, StringComparison.Ordinal);
}

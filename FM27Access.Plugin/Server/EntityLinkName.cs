using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads a person's name out of the game's inline entity link — the form the binding system hands
/// back for a person's <c>Name</c>: <c>\x01&lt;payload&gt;\x02&lt;display name&gt;\x01</c>, where the
/// payload is base64 of the link's machine half. That half carries the record the link points at
/// as a tagged field: the four bytes <c>"ferd"</c> ("dref" stored reversed) followed by the record's
/// combined index and type, little-endian. Verified 2026-10-04 on five players: each link's value
/// equalled its person reference's <c>CombinedIndexAndType</c> exactly.
///
/// So a name is returned only together with the record it names, and the caller shows it only for
/// that record: a name can never land on the wrong player. Anything that does not parse to exactly
/// one reference and a non-empty name returns false. Pure, so the tests compile it in.
/// </summary>
internal static class EntityLinkName
{
    private const char Open = '\x01';
    private const char Split = '\x02';
    private static readonly byte[] RefTag = { (byte)'f', (byte)'e', (byte)'r', (byte)'d' };

    internal static bool TryParse(string link, out int reference, out string name)
    {
        reference = 0;
        name = null;
        if (string.IsNullOrEmpty(link) || link[0] != Open) return false;
        int split = link.IndexOf(Split, 1);
        if (split < 0) return false;
        int end = link.IndexOf(Open, split + 1);
        string display = (end < 0 ? link.Substring(split + 1) : link.Substring(split + 1, end - split - 1)).Trim();
        if (display.Length == 0) return false;
        // One link only: a second token after this one would make "the name" ambiguous.
        if (end >= 0 && end != link.Length - 1) return false;

        // Unpadded base64 is still base64; restore the padding the decoder requires.
        string encoded = link.Substring(1, split - 1);
        if (encoded.Length % 4 != 0) encoded = encoded.PadRight(encoded.Length + 4 - encoded.Length % 4, '=');
        byte[] payload;
        try { payload = Convert.FromBase64String(encoded); }
        catch (FormatException) { return false; }

        int at = IndexOf(payload, RefTag, 0);
        if (at < 0 || at + RefTag.Length + 4 > payload.Length) return false;
        if (IndexOf(payload, RefTag, at + 1) >= 0) return false;
        reference = BitConverter.ToInt32(payload, at + RefTag.Length);
        name = display;
        return true;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (int i = from; i <= haystack.Length - needle.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < needle.Length && hit; j++) hit = haystack[i + j] == needle[j];
            if (hit) return i;
        }
        return -1;
    }
}

#nullable enable
namespace FM27Access.Plugin.Adapters;

public static class ColourValue
{
    public static string? NormalizeHex(string? raw)
    {
        string? hex=raw?.StartsWith('#')==true?raw[1..]:raw;
        if(hex?.Length is not(6 or 8)||!hex.All(Uri.IsHexDigit))return null;
        return "#"+hex.ToUpperInvariant()+(hex.Length==6?"FF":"");
    }
}

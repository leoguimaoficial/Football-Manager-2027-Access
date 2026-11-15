using System;
using System.Collections.Generic;
using System.Text;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only reverse lookup over the game's translation table: resolves runtime TranslationID
/// GameIDs in [from, to) and lists every id whose translated text equals one of the wanted strings
/// (case-sensitive). The recon step for reusing a game string the UI builds in code rather than
/// from a visible StaticText. GET /locscan?q=W,D,L[&amp;from=0][&amp;to=200000]. Read-only.
/// </summary>
internal static class LocScanProbe
{
    internal static string Run(string q, int from, int to)
    {
        if (string.IsNullOrEmpty(q)) return "usage: /locscan?q=a,b,c[&from=0][&to=200000]\n";
        var wanted = new HashSet<string>(q.Split(','));
        var sb = new StringBuilder();
        int failures = 0, resolved = 0;
        for (int id = from; id < to; id++)
        {
            string s;
            try { s = new SI.Translation.TranslationID(id, string.Empty).TranslatedString; }
            catch (Exception) { failures++; continue; }
            if (string.IsNullOrEmpty(s)) continue;
            resolved++;
            if (wanted.Contains(s)) sb.Append(id).Append(" = \"").Append(s).Append("\"\n");
        }
        sb.Append("scanned ").Append(from).Append("..").Append(to).Append(": ").Append(resolved)
          .Append(" resolved, ").Append(failures).Append(" threw\n");
        return sb.ToString();
    }
}

using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Pure HTML builder for a squad table's "Inf" cell — the player's status flags.
///
/// The game renders that cell as an SI.Bindable.ExpandingList: collapsed, it puts exactly ONE badge
/// in the row tree and builds the rest inside a floating popup only on hover, so a walk of the tree
/// sees one flag and the cell reads as complete when it is not. The Plugin's StatusFlagsReader gets
/// the full set off the widget's own data instead; this assembles it.
///
/// One flag renders flat (a cell that gains no structure from a one-item list). Two or more render
/// as a list, so a screen reader announces the count and steps flag by flag rather than delivering
/// one long unbroken sentence — which is what makes this scale to a player carrying five of them.
/// There is no cap and no "+N more": the whole point is that every flag reaches the player.
///
/// Game-free, so the shaping and escaping are unit-tested here. The first flag's control markup is
/// rendered live by the Plugin (it owns handle registration) and passed in already escaped.
/// </summary>
public static class StatusFlagsHtml
{
    /// <summary>
    /// One status flag as the player should hear it: the game's full title ("Injured", "Fitness
    /// Test") and the game's own reason for it ("Failed fitness test, not recommended to be
    /// selected"). Both are live game strings, already decoded and cleaned by the reader.
    /// </summary>
    public sealed class StatusFlag
    {
        public readonly string Full;
        public readonly string Reason;

        public StatusFlag(string full, string reason)
        {
            Full = full;
            Reason = reason;
        }
    }

    /// <summary>
    /// The cell's inner HTML. <paramref name="controlHtml"/> is the first flag's live control
    /// (the game's own badge button, which opens its explanation panel) already rendered and
    /// escaped by the caller, or null when the flag has no control to keep. A non-empty
    /// <paramref name="unreadableMarker"/> means the widget was present but its flags could not be
    /// read, and renders alone — never alongside a partial list, which would present an incomplete
    /// cell as a complete one.
    /// </summary>
    public static string Build(IReadOnlyList<StatusFlag> flags, string controlHtml, string unreadableMarker)
    {
        if (!string.IsNullOrEmpty(unreadableMarker)) return Escape(unreadableMarker);
        if (flags == null || flags.Count == 0) return "";

        var sb = new StringBuilder();
        if (flags.Count == 1)
        {
            AppendFlag(sb, flags[0], controlHtml);
            return sb.ToString();
        }

        sb.Append("<ul>");
        for (int i = 0; i < flags.Count; i++)
        {
            sb.Append("<li>");
            AppendFlag(sb, flags[i], i == 0 ? controlHtml : null);
            sb.Append("</li>");
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    // One flag: its name — as the game's own control where it has one, else plain text — then its
    // reason. A reason that only restates the name ("Youth Contract", whose reason is "Youth
    // Contract") or is empty carries nothing and is dropped rather than read twice.
    private static void AppendFlag(StringBuilder sb, StatusFlag flag, string controlHtml)
    {
        string name = flag.Full ?? "";
        bool named = true;
        if (!string.IsNullOrEmpty(controlHtml)) sb.Append(controlHtml);
        else if (name.Length > 0) sb.Append(Escape(name));
        else named = false;

        string reason = flag.Reason ?? "";
        if (reason.Length == 0 || reason == name) return;
        if (named) sb.Append(", ");
        sb.Append(Escape(reason));
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}

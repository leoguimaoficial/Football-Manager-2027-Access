using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#nullable disable

namespace FM27Access.Plugin.Server;

/// <summary>
/// Converts the asset name drawn by a match-report incident into an English event label. The
/// report exposes no text or tooltip for the icon, but its sprite name is live, stable data from
/// the game (for example <c>goal_small_3x</c> or <c>penalty missed_small_3x</c>).
///
/// Known event families are classified explicitly so presentation suffixes and competition-
/// specific variants cannot leak into the label. A readable, previously unseen asset is
/// humanised as a forward-compatible fallback; an opaque/missing name returns null so callers can
/// emit their normal honest unavailable marker without dropping the incident row.
/// </summary>
internal static class MatchIncidentLabel
{
    private static readonly Regex PresentationSuffix = new(
        @"_(?:small|medium|large)(?: \d+px)?(?:_\d+x)?$|_\d+x$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Separators = new(@"[_-]+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    internal static string FromSprite(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;

        string key = spriteName.Trim().ToLowerInvariant();

        // Most-specific first: every penalty/own-goal asset also contains the broad "goal" word.
        // The match reports draw an own goal as "goal down" (verified 2026-10-05 on two reports: each
        // row credits the goal to one side and names a player of the other).
        if (Has(key, "own_goal", "own goal", "goal down", "goal_down", "goal-down")) return "Own goal";
        if (Has(key, "goal-disallowed", "goal_disallowed", "goal disallowed")) return "Goal disallowed";
        if (Has(key, "goal-allowed", "goal_allowed", "goal allowed")) return "Goal allowed";
        // The current native summary's generic penalty asset is misspelled.
        // It does not by itself establish scored/missed or a penalty shootout.
        if (key.StartsWith("penelities_", StringComparison.Ordinal)) return "Penalty kick";
        if (key.Contains("penalt"))
        {
            if (key.Contains("awarded")) return "Penalty awarded";
            if (key.Contains("scored")) return "Penalty scored";
            if (key.Contains("missed")) return "Penalty missed";
            if (Has(key, "shootout", "penalties")) return "Penalty shootout";
            return "Penalty kick";
        }
        if (Has(key, "sub on and sent off", "sub_on_and_sent_off")) return "Substitute sent off";
        if (Has(key, "yellow card", "yellow_card", "yellow-card")) return "Yellow card";
        if (Has(key, "red card", "red_card", "red-card", "sent off", "sent_off")) return "Red card";
        if (Has(key, "sub on and injured", "sub_on_and_injured")) return "Substitute injured";
        if (Has(key, "sub on and off", "sub_on_and_off")) return "Substituted on and off";
        if (Has(key, "sub on", "sub_on")) return "Substitution on";
        if (Has(key, "sub off", "sub_off")) return "Substitution off";
        if (key.Contains("injur")) return "Injury";
        if (Has(key, "free kick", "free_kick", "free-kick", "freekick")) return "Free kick";
        if (Has(key, "corner kick", "corner_kick", "corner-kick") || key.Contains("corner")) return "Corner kick";
        if (Has(key, "throw in", "throw_in", "throw-in")) return "Throw-in";
        if (Has(key, "kick off", "kick_off", "kick-off", "kickoff")) return "Kick-off";
        if (Has(key, "half time", "half_time", "half-time", "halftime")) return "Half-time";
        if (Has(key, "end of match", "end_of_match", "end-of-match", "full time", "full_time", "full-time")) return "Full-time";
        if (Has(key, "extra time", "extra_time", "extra-time", "overtime")) return "Extra time";
        if (key.Contains("shootout")) return "Penalty shootout";
        if (Has(key, "start of match", "start_of_match", "start-of-match")) return "Start of match";
        if (Has(key, "play resumed", "play_resumed", "play-resumed")) return "Play resumed";
        if (key.Contains("foul")) return "Foul";
        if (key.Contains("goal")) return "Goal";

        string readable = PresentationSuffix.Replace(key, "");
        readable = Spaces.Replace(Separators.Replace(readable, " "), " ").Trim();
        // Binding/debug-shaped names are not player-facing event words. Degrade instead of
        // announcing an implementation identifier that merely happens to contain letters.
        if (readable.Length == 0 || readable.Contains('=') || !Regex.IsMatch(readable, "[a-z]"))
            return null;
        return char.ToUpperInvariant(readable[0]) + readable.Substring(1);
    }

    /// <summary>
    /// Gives the otherwise positional player names their verified meaning. In a regular goal row
    /// the game's Player1 is the scorer and optional Player2 is the assist provider. No Player2
    /// means an unassisted goal and must add no empty phrase. Other incident families retain the
    /// game's neutral name order because their secondary participant has not been verified to mean
    /// the same thing (and penalties/own goals do not carry a normal assist).
    /// </summary>
    internal static string FormatPlayers(string eventType, IReadOnlyList<string> players)
    {
        if (players == null || players.Count == 0) return "";
        if (eventType == "Goal" && players.Count > 1 && !string.IsNullOrWhiteSpace(players[1]))
            return players[0] + ", assisted by " + players[1];
        return string.Join(", ", players);
    }

    private static bool Has(string value, params string[] fragments)
    {
        foreach (string fragment in fragments)
            if (value.Contains(fragment)) return true;
        return false;
    }
}

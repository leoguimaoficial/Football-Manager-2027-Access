using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// The player report's attribute development marker: how the mod words the small arrow the game
/// paints beside an attribute whose value has moved over the current month.
///
/// The game draws six glyphs on one symmetric ramp — colour and angle scale together, from a green
/// arrow pointing straight up through yellow at a shallow angle to a red one pointing straight down
/// — and hides the icon entirely when nothing has shifted.
///
/// The ramp is NOT six sizes of change. Only the two extremes are a change: the attribute's
/// displayed number has actually moved a whole point, up at the top of the ramp and down at the
/// bottom. The four middle glyphs are sub-point drift — progress accumulating toward the next whole
/// point, in the direction the arrow leans — with the number still showing what it showed before.
/// So the wording says "Up 1" and "Down 1" only at the extremes and describes the rest as
/// progression or regression under way.
///
/// Worded rather than signed ("+1"/"-1") because a screen reader at default punctuation settings
/// drops the sign, leaving a gain and a loss both reading as "one" — the one distinction in this
/// column that must never blur.
///
/// Levels were verified live against the game's own AttributeImprovementLevel binding across ~53
/// players: Union = +3, Union-1 = +2, Union-2 = +1, Union-3 = -1. Union-4 and Union-5 never
/// appeared on any player in that sweep, so their levels (-2, -3) are read off the artwork instead:
/// they are the exact mirrors of Union-1 and Union, an orange arrow angled down and a red one
/// pointing straight down.
///
/// Lives here rather than beside the plugin's other sprite readers so it can be unit tested: it is
/// pure string logic and needs no game assemblies.
/// </summary>
public static class AttributeChange
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Union"] = "Up 1",
        ["Union-1"] = "Progressing well",
        ["Union-2"] = "Slight progression",
        ["Union-3"] = "Slight regression",
        ["Union-4"] = "Regressing",
        ["Union-5"] = "Down 1",
    };

    /// <summary>
    /// The development wording for an attribute row's arrow sprite, or null when the sprite is not
    /// one of the six the game uses for it. Callers degrade on null rather than blanking: a blank
    /// cell reads as "this attribute did not move", which would be a lie about an icon that is on
    /// screen.
    /// </summary>
    public static string LabelFor(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        return Labels.TryGetValue(StripDensitySuffix(spriteName), out var label) ? label : null;
    }

    // Atlas sprites carry the density they were authored for ("Union-2_2x"). Match on the base name
    // so the same mapping holds if the game ever serves this panel a 1x or 4x atlas.
    private static string StripDensitySuffix(string spriteName)
    {
        var underscore = spriteName.LastIndexOf('_');
        if (underscore <= 0) return spriteName;
        var suffix = spriteName.Substring(underscore + 1);
        return suffix.Length >= 2 && suffix.EndsWith("x")
               && int.TryParse(suffix.Substring(0, suffix.Length - 1), out _)
            ? spriteName.Substring(0, underscore)
            : spriteName;
    }
}

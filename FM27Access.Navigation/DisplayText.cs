using System;
using System.Collections.Generic;
using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// The single source of truth for an element's visible text on the HTML surface. Both the
/// initial render and the live re-read call this, so the text they compare can never drift
/// apart, which is what makes "did it change?" diffing meaningful.
///
/// Returns rich-text-stripped text (via <see cref="TextFilters.CleanForDisplay"/>) but NOT
/// HTML-escaped: the renderer escapes it for the initial markup, while the live update
/// channel sends it raw for the client to set as textContent (literal, no escaping needed).
///
/// Reads live every call (no caching) and does not swallow read failures: unreadable
/// display text affects what the player hears, so a throw surfaces rather than silently
/// blanking the element.
/// </summary>
public static class DisplayText
{
    // Roles whose visible text is the element's label. Progress bars, headings and buttons
    // are handled explicitly; every other role falls back to the full announcement (matching
    // the renderer's default branch). A star rating's label IS its whole value ("2 and a half
    // stars"), so the role suffix adds nothing next to it. A segmented toggle renders as a
    // button whose element carries the role, so its visible text is the bare label too.
    private static readonly HashSet<string> LabelRoles = new()
    {
        "combobox", "tab", "column header", "star rating", "toggle button",
    };

    // Roles rendered as a native HTML <input> (checkbox/radio): the input element carries the
    // role and live state via ARIA, which the screen reader announces itself. Their accessible
    // name must therefore be the bare label, never the full announcement: baking "role, state"
    // into the name would double up with (and go stale against) the live ARIA, which a screen
    // reader reads back as a contradictory description ("not checked ... toggle, on").
    private static readonly HashSet<string> NativeInputRoles = new()
    {
        "checkbox", "toggle", "radio button",
    };

    // A button's short descriptive tail (a brief tooltip) is folded into the primary label
    // rather than hidden behind the on-demand Alt+I read; only a genuinely long tail stays a
    // tooltip. The cutoff is the character length of a representative long subtitle:
    // "Increased, So far: 6 weeks (Current Injury), Expected to return in 4 weeks - 2 months".
    public const int InlineDescriptionMaxLength = 85;

    // Appended after the primary label of a button whose tooltip is too long to inline, so the
    // user knows the on-demand read is available. Worded exactly as the command chord.
    private const string MoreInfoHint = "Alt I for more info";

    private static bool ShortEnoughToInline(string cleanedDescription)
        => !string.IsNullOrEmpty(cleanedDescription)
           && cleanedDescription.Length <= InlineDescriptionMaxLength;

    // True when a button's secondary text was short enough to fold into the label, so the
    // on-demand read has nothing further to offer — distinct from a button that never had a
    // description at all (which re-announces its label instead).
    public static bool HasInlinedDescription(IAccessibleElement el)
        => (el.Role ?? "") == "button" && ShortEnoughToInline(Clean(el.GetDescription()));

    public static string Of(IAccessibleElement el)
    {
        // A panel profile's rename replaces the label portion only; role and live state
        // still come from the element. The speech surface applies this in
        // Announcer.GetDisplayAnnouncement; this is the HTML surface's equivalent.
        var rename = NavigationManager.GetRenameLabel(el);

        if (NavigationManager.IsHeading(el))
            return Clean(rename ?? Label(el));

        string role = el.Role ?? "";
        // A step bar's whole content is its filled-of-total count, and a stepper's own label is
        // usually a template id a profile rename replaces — so, like a progress bar, the label
        // alone would drop the only value the element carries.
        if (role == "progress bar" || role == "stepper")
            return Clean((rename ?? Label(el)) + ": " + el.GetState());
        // Buttons show their primary label. A short descriptive remainder is folded in after
        // it ("primary, secondary"); a long one is held back and read on demand via
        // DescriptionOf. The empty-primary fallback matches the other label roles.
        if (role == "button")
        {
            var primary = Clean(rename ?? PrimaryLabel(el));
            var description = Clean(el.GetDescription());
            string label;
            if (string.IsNullOrEmpty(description)) label = primary;
            else if (ShortEnoughToInline(description)) label = primary + ", " + description;
            // A long tail stays an on-demand tooltip; advertise the command that reads it,
            // unless the button is disabled (it cannot take focus, so Alt+I can never reach it).
            else label = IsDisabled(el) ? primary : primary + ", " + MoreInfoHint;
            // A button group's selected member appends "selected" last, and a warning-flagged
            // cell its "warning", mirroring the speech surface's state-last ordering
            // (ButtonAdapter.GetState).
            if (HasStateToken(el, "warning")) label += ", warning";
            return IsSelected(el) ? label + ", selected" : label;
        }
        // Native-input controls take the bare label with no announcement fallback (see
        // NativeInputRoles): an unresolved label yields an empty name rather than a stale,
        // contradictory "label, role, state" echo of the ARIA the input already carries.
        if (NativeInputRoles.Contains(role))
            return Clean(rename ?? el.GetLabel());
        if (LabelRoles.Contains(role))
            return Clean(rename ?? Label(el));
        return Clean(rename ?? el.GetAnnouncement());
    }

    // The secondary portion of a control's label, read on demand (the HTML surface's Alt+I
    // command). Cleaned the same way as the primary so rich-text markup never leaks. A
    // rename replaces only the primary label, so the description still comes from the element.
    public static string DescriptionOf(IAccessibleElement el)
    {
        var description = Clean(el.GetDescription());
        // A short tail is already shown inline in the button label (see Of), so there is
        // nothing extra to read on demand. Only buttons inline, so any other element's
        // description always reads in full.
        if ((el.Role ?? "") == "button" && ShortEnoughToInline(description)) return "";
        return description;
    }

    // Mirrors the renderer's label resolution: the explicit label if present, otherwise
    // the full announcement.
    private static string Label(IAccessibleElement el)
    {
        var l = el.GetLabel();
        return string.IsNullOrEmpty(l) ? el.GetAnnouncement() : l;
    }

    // The primary label with the same empty fallback as Label: a button with no readable
    // primary text reads as its full announcement rather than blanking.
    private static string PrimaryLabel(IAccessibleElement el)
    {
        var p = el.GetPrimaryLabel();
        return string.IsNullOrEmpty(p) ? el.GetAnnouncement() : p;
    }

    private static string Clean(string s) => TextFilters.CleanForDisplay(s);

    // Mirrors the adapters' disabled signal: the "disabled" token in the comma-joined state.
    private static bool IsDisabled(IAccessibleElement el) => HasStateToken(el, "disabled");

    // Mirrors the adapters' selected signal: the "selected" token in the comma-joined state.
    private static bool IsSelected(IAccessibleElement el) => HasStateToken(el, "selected");

    private static bool HasStateToken(IAccessibleElement el, string token)
    {
        var state = el.GetState();
        if (string.IsNullOrEmpty(state)) return false;
        foreach (var part in state.Split(new[] { ", " }, StringSplitOptions.None))
            if (part == token) return true;
        return false;
    }
}

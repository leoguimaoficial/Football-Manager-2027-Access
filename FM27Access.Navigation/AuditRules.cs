using System;
using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// The pure, game-free rules that classify a rendered navigation stop into one of the recurring
/// accessibility-defect flavours the autonomous run keeps fixing — so detection lives in one place
/// the renderer, the dev-driver audit, and the tests all share, instead of an agent re-deriving the
/// same buckets by eye on each screen.
///
/// The flavours mirror the worklog's recurring clusters: an interactive control with no accessible
/// name (the broken case), an honest gap MARKER the control degraded to (the worklist of still-
/// unnamed/unclassified controls, not a bug), a leaf image rendered as a stop (decorative noise),
/// a field label left stranded before the control it should name (the CLUSTER 1 disconnected-label
/// signature), and a title rendered twice in a row.
///
/// These operate on the already-resolved (role, name) the player perceives — what the HTML surface
/// renders and the speech surface announces — NOT raw element data, so a profile rename that fixes
/// a label is correctly seen as fixed. Reading those live values off the element is the caller's job
/// (Server/Dev/Audit.cs); everything decidable from strings lives here and is unit-tested.
/// </summary>
public static class AuditRules
{
    public enum Finding
    {
        /// <summary>No defect.</summary>
        Ok,
        /// <summary>An interactive control whose accessible name is empty — a genuine bug.</summary>
        EmptyName,
        /// <summary>The control degraded to an honest gap marker (still-unnamed/unclassified). Not a
        /// bug; the inventory of controls a name source or classifier mapping would still improve.</summary>
        DegradeMarker,
        /// <summary>A leaf image rendered as a stop ("…, image") — a decorative-noise candidate.</summary>
        ImageStop,
    }

    /// <summary>
    /// The honest gap phrases a control degrades to when it cannot be named or classified: the
    /// centralized <see cref="Speech.Degrade"/> markers plus the two adapter-authored "unlabeled …"
    /// markers (ButtonAdapter's icon-button fallback, DiggableAdapter's expandable fallback). Matching
    /// these lets the audit inventory the still-degraded controls without mistaking an honest marker
    /// for a missing-name bug. Kept as plain strings (not the adapter literals) because those adapters
    /// live in the game-bound Plugin assembly this game-free project cannot reference.
    /// </summary>
    public static readonly IReadOnlyList<string> DegradeMarkers = new[]
    {
        Speech.Degrade.UnknownControl,   // "unknown control"
        Speech.Degrade.Unavailable,      // "unavailable"
        Speech.Degrade.Unreadable,       // "couldn't read"
        "unlabeled icon button",
        "unlabeled expandable",
    };

    public static bool IsDegradeMarker(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < DegradeMarkers.Count; i++)
            if (string.Equals(name, DegradeMarkers[i], StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// A soft "this might be a developer id leaking as a label" signal: the string carries an
    /// underscore, or a camelHump (a lowercase letter immediately followed by an uppercase one), the
    /// PascalCase/camelCase signature of names like "BookmarkButton". All-caps acronyms ("OK") and
    /// single words ("Continue") have no camelHump and are not flagged. This is the SAME test
    /// ButtonAdapter uses to reject id-shaped fallbacks (PluginAdapterBase forwards here), reused by
    /// the audit as an INFO-level lead — NOT a confirmed finding, because real proper nouns
    /// ("McTominay", "DeBruyne") also camelHump. It exists so a NEW leak flavour the closed-set rules
    /// do not yet model still surfaces something for a human to look at.
    /// </summary>
    public static bool LooksLikeDeveloperId(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.IndexOf('_') >= 0) return true;
        for (int i = 1; i < s.Length; i++)
            if (char.IsUpper(s[i]) && char.IsLower(s[i - 1])) return true;
        return false;
    }

    /// <summary>
    /// True for roles the HTML surface renders as an operable control (see HtmlRenderer's role
    /// switch). "text", "heading", "image", "progress bar" are informational, not interactive.
    /// </summary>
    public static bool IsInteractiveRole(string role) => role switch
    {
        "button" or "checkbox" or "toggle" or "radio button" or "text field"
            or "slider" or "combobox" or "dropdown" or "searchable dropdown"
            or "number field" => true,
        _ => false,
    };

    /// <summary>
    /// Classify one stop by its rendered role, resolved accessible name, and whether the live
    /// element reports itself activatable. Order matters: a leaf image is decorative regardless of
    /// name; an honest degrade marker is a (non-empty) name, so it is recognised before the empty
    /// check; only a control with no name at all is the EmptyName bug.
    /// </summary>
    public static Finding ClassifyStop(string role, string name, bool canActivate)
    {
        string n = (name ?? "").Trim();
        if (role == "image") return Finding.ImageStop;
        if (IsDegradeMarker(n)) return Finding.DegradeMarker;
        if ((canActivate || IsInteractiveRole(role)) && n.Length == 0) return Finding.EmptyName;
        return Finding.Ok;
    }

    /// <summary>A single rendered stop reduced to the three fields the rules decide on.</summary>
    public readonly struct Stop
    {
        public readonly string Role;
        public readonly string Name;
        public readonly bool CanActivate;

        public Stop(string role, string name, bool canActivate)
        {
            Role = role ?? "";
            Name = name ?? "";
            CanActivate = canActivate;
        }

        public bool Interactive => CanActivate || IsInteractiveRole(Role);
    }

    /// <summary>
    /// True when a text stop sits immediately before an interactive stop that has NO usable name of
    /// its own (empty, or only a degrade marker) — the disconnected-label signature: a field label
    /// the following control failed to adopt, so the player hears a stray line then an unnamed
    /// control. Requiring the FOLLOWING control to be unnamed keeps this from flagging ordinary
    /// label/value prose, where the control already names itself.
    /// </summary>
    public static bool IsOrphanLabel(Stop label, Stop next)
    {
        if (label.Role != "text" || string.IsNullOrWhiteSpace(label.Name)) return false;
        if (!next.Interactive) return false;
        return string.IsNullOrWhiteSpace(next.Name) || IsDegradeMarker(next.Name);
    }

    /// <summary>
    /// True when two consecutive informational stops carry the same name — a title or label rendered
    /// twice (the B7a duplicate-title flavour). Restricted to text/heading roles on purpose: a row of
    /// identically-named CONTROLS is ordinary UI, not a bug — five honest "unlabeled icon button"
    /// ribbon bookmarks, or repeated "Edit"/"Remove" actions in a list, must not read as duplicates.
    /// Empty and degrade-marker names never match either (a run of blank or "couldn't read" stops is
    /// not a duplicated title).
    /// </summary>
    public static bool IsDuplicateAdjacent(Stop a, Stop b)
    {
        if (string.IsNullOrWhiteSpace(a.Name) || IsDegradeMarker(a.Name)) return false;
        if (a.Role != "text" && a.Role != "heading") return false;
        return a.Role == b.Role && string.Equals(a.Name, b.Name, StringComparison.Ordinal);
    }
}

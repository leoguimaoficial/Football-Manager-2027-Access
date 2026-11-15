using System.Text;
using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>Why a menu holder was accepted as a control's menu, or rejected.</summary>
public enum MenuScopeVerdict
{
    NoHolder,
    /// <summary>The control itself carries the menu. Never in doubt.</summary>
    Self,
    /// <summary>A holder whose own text is what the control is named after — the menu is about the label.</summary>
    LabelMatch,
    /// <summary>A textless holder close enough to be this control's own binding rather than a neighbour's.</summary>
    ShallowWrapper,
    /// <summary>A text holder naming a different entity than the label — the tile/row inheritance bug.</summary>
    RejectedLabelMismatch,
    /// <summary>Several rival holders and none matches the label: no single entity can answer for it.</summary>
    RejectedComposite,
    /// <summary>A textless holder too far away to be this control's own binding.</summary>
    RejectedDepth,
    /// <summary>A single-entity self-contained menu component (a calendar event): its whole subtree is
    /// one entity, so a control at any depth inside it opens that entity's menu.</summary>
    EntityComponent,
}

/// <summary>
/// The single rule for whether a menu-bearing holder may speak for the control a player is on.
///
/// The rule in one line: <b>a control advertises a menu only when the menu belongs to the entity the
/// control is named after.</b> Without it, the resolver's "any menu-bearing element in my subtree"
/// search lets a summary tile inherit a buried player's or the manager's own menu, and a message's
/// subject line inherit its sender's — the label and the menu describing different things, which a
/// player has no way to discover except by opening it.
///
/// Pure and game-free by design (no VisualElement, no IL2CPP): the shapes it decides are observable
/// as five plain values, so every case is unit-testable without a running game.
/// </summary>
public static class ContextMenuScope
{
    /// <summary>
    /// How far below (or above) a control a TEXTLESS holder may sit and still count as that control's
    /// own binding. A name cell's PluginContextMenuBindings sits a level or two inside its button; a
    /// summary tile's binding sits deep inside a sub-block belonging to another entity entirely.
    /// Calibrated from the /ctxcensus depth histogram, not guessed.
    /// </summary>
    public const int MaxWrapperDepth = 3;

    /// <summary>
    /// Judge a resolved holder. <paramref name="ownerLabel"/> is the control's rendered accessible
    /// name, <paramref name="holderText"/> the holder's own text (null/empty for a textless holder, or
    /// for a text holder whose cell has not streamed in yet), <paramref name="depth"/> the levels
    /// between them, and <paramref name="holderCount"/> how many rival holders the subtree carried.
    /// <paramref name="holderIsSelfContainedEntity"/> is set when the holder is a single-entity
    /// component that bakes its own menu (a calendar event): its whole subtree is that ONE entity, so
    /// depth cannot mislabel and the cap does not apply — unlike a binding buried in a multi-entity tile.
    /// </summary>
    public static MenuScopeVerdict Judge(
        string ownerLabel, string holderText, bool holderIsText, int depth, int holderCount, bool isSelf,
        bool holderIsSelfContainedEntity = false)
    {
        if (isSelf) return MenuScopeVerdict.Self;

        // A self-contained entity component (an FMCalendarEvent training session / fixture tile) owns
        // its whole subtree as one entity, so any control inside it opens that entity's menu no matter
        // how deep the control sits. This is the calendar training-session case: the session button is
        // a deep SIButton whose only menu is the ancestor event's — legitimate, and the depth cap below
        // would wrongly drop it.
        if (holderIsSelfContainedEntity) return MenuScopeVerdict.EntityComponent;

        string holder = Normalize(holderText);

        // A text holder with readable text is judged by NAME, not distance: that is the only evidence
        // that says whose menu it is. An unstreamed text holder has no name to judge, so it falls
        // through to the distance rule rather than being rejected for a value that has not arrived.
        if (holderIsText && holder.Length > 0)
        {
            if (Matches(Normalize(ownerLabel), holder)) return MenuScopeVerdict.LabelMatch;
            return holderCount > 1 ? MenuScopeVerdict.RejectedComposite : MenuScopeVerdict.RejectedLabelMismatch;
        }

        return depth <= MaxWrapperDepth ? MenuScopeVerdict.ShallowWrapper : MenuScopeVerdict.RejectedDepth;
    }

    /// <summary>True when the verdict permits the mark.</summary>
    public static bool Accepts(MenuScopeVerdict verdict) =>
        verdict is MenuScopeVerdict.Self or MenuScopeVerdict.LabelMatch
                or MenuScopeVerdict.ShallowWrapper or MenuScopeVerdict.EntityComponent;

    // Either name containing the other, so a cell that decorates the entity ("12, Dan Abraham, 7.4")
    // still matches its own name link, while a tile ("Medical Center") never matches a player inside
    // it. Both sides are already normalized. A one-character holder is not evidence of anything, so
    // it falls to the caller's distance rule via the length guard.
    private static bool Matches(string owner, string holder)
    {
        if (owner.Length == 0 || holder.Length < 2) return false;
        return owner.Contains(holder) || holder.Contains(owner);
    }

    // Rich text stripped (the game's link/colour markup wraps every entity name), then lowercased with
    // punctuation dropped and whitespace collapsed, so "Atlético Ottawa" and "Atlético Ottawa," are one
    // string. Diacritics are deliberately kept: both sides come from the same game text, so they match
    // as-is, and folding them would risk collapsing genuinely different names.
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var cleaned = TextFilters.CleanForDisplay(s);
        if (string.IsNullOrEmpty(cleaned)) return "";

        var sb = new StringBuilder(cleaned.Length);
        bool pendingSpace = false;
        foreach (var c in cleaned)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else pendingSpace = true;
        }
        return sb.ToString();
    }
}

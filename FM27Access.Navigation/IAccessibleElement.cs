namespace FM27Access.Navigation;

/// <summary>
/// Contract for every accessible widget in the game UI.
/// All read methods query live from the backing element — never cached.
/// If the backing element is destroyed or data is unreadable, methods throw.
/// </summary>
public interface IAccessibleElement
{
    /// <summary>e.g. "button", "checkbox", "slider", "text"</summary>
    string Role { get; }

    /// <summary>False for informational elements (text, progress bar, gauge, image).</summary>
    bool CanActivate { get; }

    /// <summary>
    /// True when this element carries a secondary right-click context menu (the game marks it with a
    /// PluginContextMenuBindings node in its ancestry). The surfaces expose this as a secondary action
    /// — aria-haspopup on the companion, Shift+F10 on the in-game cursor. Read live.
    /// </summary>
    bool HasContextMenu { get; }

    /// <summary>
    /// Readable text for this element, read live every call.
    /// Throws if the element is destroyed or text is unreadable.
    /// </summary>
    string GetLabel();

    /// <summary>
    /// Current state (e.g. "checked", "75%", "3 of 10").
    /// Returns empty string if the element has no meaningful state.
    /// Throws if state exists but cannot be read.
    /// </summary>
    string GetState();

    /// <summary>
    /// Composes role, label, and state into a speech-ready announcement.
    /// Reads live — calls GetLabel() and GetState() internally.
    /// </summary>
    string GetAnnouncement();

    /// <summary>
    /// The distinguishing first portion of the label — what the HTML surface shows on a
    /// control whose full label is too long to display cleanly. For most elements this is
    /// the whole label; for buttons it is the primary text, with the rest exposed via
    /// <see cref="GetDescription"/>. Read live; throws if unreadable.
    /// </summary>
    string GetPrimaryLabel();

    /// <summary>
    /// The secondary portion of the label not shown by <see cref="GetPrimaryLabel"/> — the
    /// descriptive remainder a player retrieves on demand. Empty string when the element has
    /// no secondary text. Read live; throws if text exists but cannot be read.
    /// </summary>
    string GetDescription();

    /// <summary>
    /// Performs the default action (click, toggle, etc.).
    /// Throws NotSupportedException if CanActivate is false.
    /// Throws if the element is destroyed.
    /// </summary>
    void Activate();

    /// <summary>
    /// Checks resolved visibility live from the backing element.
    /// </summary>
    bool IsVisible();
}

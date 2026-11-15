namespace FM27Access.Navigation.Input;

/// <summary>
/// A handler on the input stack. Each handler represents a distinct interaction
/// context (normal navigation, dropdown, text editing, date picker, etc.).
///
/// Handlers declare which keys they claim. The InputBlockingCoordinator uses
/// this to selectively disable conflicting game InputActions.
/// </summary>
public interface IInputHandler
{
    /// <summary>
    /// Human-readable name for logging and debugging.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// When true, ALL keyboard-bound game InputActions are disabled while this
    /// handler is active (not just ClaimedKeys). Used for text editing, modal
    /// dialogs where the game should receive no keyboard input.
    /// </summary>
    bool CapturesAllInput { get; }

    /// <summary>
    /// The set of keys this handler uses. The InputBlockingCoordinator disables
    /// game InputActions bound to these keys when this handler is on the stack.
    /// </summary>
    IReadOnlyList<KeyboardKey> ClaimedKeys { get; }

    /// <summary>
    /// Called when a key is pressed. Return true to consume the key (stops the
    /// stack walk). Return false to let it fall through to lower handlers.
    /// </summary>
    bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers);

    /// <summary>
    /// Called each frame. Return true to stop the stack walk (key was consumed
    /// via polling). Return false to let lower handlers tick.
    /// </summary>
    bool Tick();

    /// <summary>
    /// Called when this handler becomes (or returns to being) the active handler.
    /// Should only announce state to the user, not perform UI mutations. Handlers
    /// that open popups (e.g. DropdownHandler) should guard against re-activation
    /// while already waiting for a popup.
    /// </summary>
    void OnActivate();

    /// <summary>
    /// Called when this handler is removed from the stack.
    /// </summary>
    void OnDeactivate();
}

namespace FM27Access.Navigation.Input;

/// <summary>
/// Abstraction over game InputActions. The Navigation project uses this interface
/// so it can be tested without game assemblies. The Plugin project provides the
/// real implementation that wraps Unity InputSystem InputActions.
/// </summary>
public interface IInputActionBridge
{
    /// <summary>
    /// Disable all game InputActions bound to the specified keys.
    /// Implementation must snapshot each action's enabled state before disabling,
    /// so it can be restored correctly later.
    /// </summary>
    void DisableActionsForKeys(IReadOnlyList<KeyboardKey> keys);

    /// <summary>
    /// Re-enable game InputActions bound to the specified keys, but only those
    /// that were enabled before the mod disabled them.
    /// </summary>
    void EnableActionsForKeys(IReadOnlyList<KeyboardKey> keys);

    /// <summary>
    /// Disable ALL keyboard-bound game InputActions across all maps.
    /// Used when a CapturesAllInput handler is active.
    /// </summary>
    void DisableAllKeyboardActions();

    /// <summary>
    /// Re-enable all keyboard-bound game InputActions that were enabled before
    /// the mod's DisableAllKeyboardActions() call.
    /// </summary>
    void EnableAllKeyboardActions();

    /// <summary>
    /// Re-assert disabled state each frame for actions the coordinator has blocked.
    /// The game may re-enable InputActions mid-frame; this forces them back off.
    /// </summary>
    void ReAssertBlocking(IReadOnlyList<KeyboardKey> activeKeys, bool capturesAll);
}

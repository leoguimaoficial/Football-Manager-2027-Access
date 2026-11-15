using UnityEngine.InputSystem;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input;

/// <summary>
/// Implements IInputActionBridge using the real Unity InputSystem.
/// Snapshots each action's enabled state the first time the mod touches it,
/// so re-enable only restores actions that were actually enabled before.
/// </summary>
public class InputActionBridgeImpl : IInputActionBridge
{
    private readonly BindingRegistry _registry;
    private readonly Dictionary<InputAction, bool> _snapshots = new();

    public InputActionBridgeImpl(BindingRegistry registry)
    {
        _registry = registry;
    }

    public void DisableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
    {
        foreach (var key in keys)
        {
            foreach (var action in _registry.GetActionsForKey(key))
                DisableWithSnapshot(action);
        }
    }

    public void EnableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
    {
        foreach (var key in keys)
        {
            foreach (var action in _registry.GetActionsForKey(key))
                RestoreAction(action);
        }
    }

    public void DisableAllKeyboardActions()
    {
        foreach (var action in _registry.AllKeyboardActions)
            DisableWithSnapshot(action);
    }

    public void EnableAllKeyboardActions()
    {
        foreach (var action in _registry.AllKeyboardActions)
            RestoreAction(action);
    }

    /// <summary>
    /// Re-assert disabled state for actions the coordinator has marked as blocked.
    /// Called each frame by KeyboardPoller to handle the game re-enabling actions.
    /// </summary>
    public void ReAssertBlocking(IReadOnlyList<KeyboardKey> activeKeys, bool capturesAll)
    {
        // Index loops (not foreach) over the IReadOnlyList arguments: foreach over an interface-typed
        // list boxes a struct enumerator on the heap, and this runs every frame. Indexing does not.
        if (capturesAll)
        {
            var all = _registry.AllKeyboardActions;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].enabled)
                    all[i].Disable();
            }
            return;
        }

        for (int k = 0; k < activeKeys.Count; k++)
        {
            var actions = _registry.GetActionsForKey(activeKeys[k]);
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].enabled)
                    actions[i].Disable();
            }
        }
    }

    private void DisableWithSnapshot(InputAction action)
    {
        if (!_snapshots.ContainsKey(action))
            _snapshots[action] = action.enabled;

        if (action.enabled)
            action.Disable();
    }

    private void RestoreAction(InputAction action)
    {
        if (_snapshots.TryGetValue(action, out bool wasEnabled))
        {
            _snapshots.Remove(action);
            if (wasEnabled)
                action.Enable();
        }
    }
}

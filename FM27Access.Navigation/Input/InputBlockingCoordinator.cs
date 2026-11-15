namespace FM27Access.Navigation.Input;

/// <summary>
/// Ref-counts how many handlers claim each key. Calls bridge to disable on
/// first claim, enable when last claim removed. Handles CapturesAllInput flag.
///
/// This class doesn't know about game InputActions — it works with abstract
/// KeyboardKeys and delegates to IInputActionBridge for the real work.
/// </summary>
public class InputBlockingCoordinator
{
    private readonly IInputActionBridge _bridge;
    private readonly Func<IReadOnlyList<IInputHandler>> _getStackHandlers;
    private readonly Dictionary<KeyboardKey, int> _refCount = new();
    // Cached active-key set (every key currently in _refCount), rebuilt only on push/pop so the
    // per-frame ReAssertBlocking reads it without allocating a fresh list. Handed to the bridge by
    // reference: it is valid only for the duration of that call and must not be retained by the callee.
    private readonly List<KeyboardKey> _activeKeys = new();
    private int _captureAllDepth;

    /// <param name="bridge">Bridge to game InputActions.</param>
    /// <param name="getStackHandlers">
    /// Returns all handlers currently on the stack. Used to recalculate
    /// claimed keys after a CapturesAllInput handler is popped.
    /// </param>
    public InputBlockingCoordinator(IInputActionBridge bridge, Func<IReadOnlyList<IInputHandler>> getStackHandlers)
    {
        _bridge = bridge;
        _getStackHandlers = getStackHandlers;
    }

    /// <summary>
    /// Called when a handler is pushed onto the stack.
    /// </summary>
    public void OnHandlerPushed(IInputHandler handler)
    {
        if (handler.CapturesAllInput)
        {
            _captureAllDepth++;
            if (_captureAllDepth == 1)
                _bridge.DisableAllKeyboardActions();
        }

        foreach (var key in handler.ClaimedKeys)
        {
            _refCount.TryGetValue(key, out int count);
            _refCount[key] = count + 1;

            if (count == 0 && _captureAllDepth == 0)
                _bridge.DisableActionsForKeys(new[] { key });
        }

        RebuildActiveKeys();
    }

    /// <summary>
    /// Called when a handler is popped from the stack.
    /// </summary>
    public void OnHandlerPopped(IInputHandler handler)
    {
        if (handler.CapturesAllInput)
        {
            _captureAllDepth--;
            if (_captureAllDepth == 0)
            {
                _bridge.EnableAllKeyboardActions();
                ReDisableClaimedKeys();
            }
        }

        foreach (var key in handler.ClaimedKeys)
        {
            if (!_refCount.TryGetValue(key, out int count))
                continue;

            int newCount = count - 1;
            if (newCount <= 0)
            {
                _refCount.Remove(key);
                if (_captureAllDepth == 0)
                    _bridge.EnableActionsForKeys(new[] { key });
            }
            else
            {
                _refCount[key] = newCount;
            }
        }

        RebuildActiveKeys();
    }

    /// <summary>
    /// Called each frame to re-assert disabled state. The game may re-enable
    /// InputActions between frames; this forces them back off.
    /// </summary>
    public void ReAssertBlocking()
    {
        _bridge.ReAssertBlocking(_activeKeys, _captureAllDepth > 0);
    }

    /// <summary>
    /// After EnableAllKeyboardActions(), re-disable keys that remaining
    /// handlers still claim.
    /// </summary>
    private void ReDisableClaimedKeys()
    {
        if (_activeKeys.Count > 0)
            _bridge.DisableActionsForKeys(_activeKeys);
    }

    // Rebuild the cached active-key set from _refCount. Called only when _refCount changes (handler
    // push/pop), so the per-frame ReAssertBlocking reads the cached list instead of rebuilding one.
    // _refCount only ever holds keys with count >= 1 (a key is removed the moment its count hits 0).
    private void RebuildActiveKeys()
    {
        _activeKeys.Clear();
        foreach (var kvp in _refCount)
        {
            if (kvp.Value > 0)
                _activeKeys.Add(kvp.Key);
        }
    }
}

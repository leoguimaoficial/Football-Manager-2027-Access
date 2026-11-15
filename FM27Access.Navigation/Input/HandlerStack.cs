namespace FM27Access.Navigation.Input;

/// <summary>
/// Manages a stack of IInputHandler instances. Push/Pop/Replace manage lifecycle
/// with OnActivate/OnDeactivate callbacks. Notifies the InputBlockingCoordinator
/// directly when the stack changes.
///
/// KeyboardPoller walks the stack top-to-bottom using GetAt() + Count.
/// Each handler receives Tick() / HandleKeyDown() until one consumes the event
/// or a CapturesAllInput barrier is reached (inclusive).
/// </summary>
public class HandlerStack
{
    private readonly List<IInputHandler> _stack = new();
    private InputBlockingCoordinator _coordinator;

    public IInputHandler ActiveHandler =>
        _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

    public int Count => _stack.Count;

    public IInputHandler GetAt(int index)
    {
        if (index < 0 || index >= _stack.Count) return null;
        return _stack[index];
    }

    /// <summary>
    /// Wire up the coordinator after both objects are constructed.
    /// Must be called before Push/Pop/Replace/Clear.
    /// </summary>
    public void SetCoordinator(InputBlockingCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    /// <summary>
    /// Push a handler onto the stack. Calls OnActivate first so the handler
    /// can trigger game UI, then notifies the coordinator to disable InputActions.
    /// </summary>
    public void Push(IInputHandler handler)
    {
        _stack.Add(handler);
        handler.OnActivate();
        _coordinator.OnHandlerPushed(handler);
        Speech.Log.Debug($"HandlerStack.Push: {handler.DisplayName} (depth={_stack.Count})");
    }

    /// <summary>
    /// Remove the top handler. Calls OnDeactivate on the removed handler, notifies
    /// coordinator, then calls OnActivate on the newly exposed handler (if any).
    /// Crashes on empty stack — popping nothing is a bug.
    /// </summary>
    public void Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException("HandlerStack.Pop called on empty stack");

        var removed = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
        removed.OnDeactivate();
        _coordinator.OnHandlerPopped(removed);
        Speech.Log.Debug($"HandlerStack.Pop: {removed.DisplayName} (depth={_stack.Count})");

        var newActive = ActiveHandler;
        if (newActive != null)
            newActive.OnActivate();
    }

    /// <summary>
    /// Replace the top handler with a new one. Pops then pushes.
    /// If stack is empty, equivalent to Push.
    /// </summary>
    public void Replace(IInputHandler handler)
    {
        if (_stack.Count > 0)
        {
            var removed = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            removed.OnDeactivate();
            _coordinator.OnHandlerPopped(removed);
            Speech.Log.Debug($"HandlerStack.Replace: removed {removed.DisplayName}");
        }

        _stack.Add(handler);
        handler.OnActivate();
        _coordinator.OnHandlerPushed(handler);
        Speech.Log.Debug($"HandlerStack.Replace: activated {handler.DisplayName} (depth={_stack.Count})");
    }

    /// <summary>
    /// Deactivate all handlers top-to-bottom and clear the stack.
    /// </summary>
    public void Clear()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            _stack[i].OnDeactivate();
            _coordinator.OnHandlerPopped(_stack[i]);
        }
        _stack.Clear();
        Speech.Log.Debug("HandlerStack.Clear: stack cleared");
    }

    /// <summary>
    /// Collect all handlers currently on the stack, for the coordinator to
    /// recalculate blocking state after a CapturesAllInput handler is popped.
    /// </summary>
    public IReadOnlyList<IInputHandler> GetAllHandlers() => _stack.AsReadOnly();
}

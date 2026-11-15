using FM27Access.Navigation.Input;
using Xunit;

namespace FM27Access.Tests.Input;

public class HandlerStackTests
{
    private readonly FakeBridge _bridge = new();
    private readonly HandlerStack _stack;
    private readonly InputBlockingCoordinator _coordinator;

    public HandlerStackTests()
    {
        _stack = new HandlerStack();
        _coordinator = new InputBlockingCoordinator(_bridge, _stack.GetAllHandlers);
        _stack.SetCoordinator(_coordinator);
    }

    [Fact]
    public void Push_CallsOnActivateAndNotifiesCoordinator()
    {
        var handler = new FakeHandler("A", false, KeyboardKey.Enter);
        _stack.Push(handler);

        Assert.Equal(1, handler.ActivateCount);
        Assert.Same(handler, _stack.ActiveHandler);
        Assert.Equal(1, _stack.Count);
        Assert.Contains(KeyboardKey.Enter, _bridge.DisabledKeys);
    }

    [Fact]
    public void Pop_CallsOnDeactivateAndReactivatesPrevious()
    {
        var bottom = new FakeHandler("Bottom");
        var top = new FakeHandler("Top");

        _stack.Push(bottom);
        _stack.Push(top);
        bottom.ActivateCount = 0; // reset after push

        _stack.Pop();

        Assert.Equal(1, top.DeactivateCount);
        Assert.Equal(1, bottom.ActivateCount); // reactivated
        Assert.Same(bottom, _stack.ActiveHandler);
    }

    [Fact]
    public void Pop_OnEmptyStack_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _stack.Pop());
    }

    [Fact]
    public void Pop_LastHandler_LeavesStackEmpty()
    {
        var handler = new FakeHandler("Only");
        _stack.Push(handler);

        _stack.Pop();

        Assert.Null(_stack.ActiveHandler);
        Assert.Equal(0, _stack.Count);
        Assert.Equal(1, handler.DeactivateCount);
    }

    [Fact]
    public void Replace_PopsOldAndPushesNew()
    {
        var old = new FakeHandler("Old");
        var replacement = new FakeHandler("New");

        _stack.Push(old);
        _stack.Replace(replacement);

        Assert.Equal(1, old.DeactivateCount);
        Assert.Equal(1, replacement.ActivateCount);
        Assert.Same(replacement, _stack.ActiveHandler);
        Assert.Equal(1, _stack.Count);
    }

    [Fact]
    public void Replace_OnEmptyStack_ActsAsPush()
    {
        var handler = new FakeHandler("Fresh");

        _stack.Replace(handler);

        Assert.Equal(1, handler.ActivateCount);
        Assert.Same(handler, _stack.ActiveHandler);
        Assert.Equal(1, _stack.Count);
    }

    [Fact]
    public void Clear_DeactivatesAllTopToBottom()
    {
        var a = new FakeHandler("A");
        var b = new FakeHandler("B");
        var c = new FakeHandler("C");

        _stack.Push(a);
        _stack.Push(b);
        _stack.Push(c);

        _stack.Clear();

        Assert.Equal(0, _stack.Count);
        Assert.Null(_stack.ActiveHandler);
        Assert.Equal(1, a.DeactivateCount);
        Assert.Equal(1, b.DeactivateCount);
        Assert.Equal(1, c.DeactivateCount);
    }

    [Fact]
    public void GetAt_ReturnsCorrectHandler()
    {
        var bottom = new FakeHandler("Bottom");
        var top = new FakeHandler("Top");

        _stack.Push(bottom);
        _stack.Push(top);

        Assert.Same(bottom, _stack.GetAt(0));
        Assert.Same(top, _stack.GetAt(1));
        Assert.Null(_stack.GetAt(-1));
        Assert.Null(_stack.GetAt(2));
    }

    [Fact]
    public void Push_DoesNotDeactivatePreviousHandler()
    {
        var bottom = new FakeHandler("Bottom");
        var top = new FakeHandler("Top");

        _stack.Push(bottom);
        _stack.Push(top);

        Assert.Equal(0, bottom.DeactivateCount);
    }

    [Fact]
    public void GetAllHandlers_ReturnsAllInOrder()
    {
        var a = new FakeHandler("A");
        var b = new FakeHandler("B");

        _stack.Push(a);
        _stack.Push(b);

        var all = _stack.GetAllHandlers();
        Assert.Equal(2, all.Count);
        Assert.Same(a, all[0]);
        Assert.Same(b, all[1]);
    }

    [Fact]
    public void Push_OnActivateRunsBeforeCoordinatorNotification()
    {
        var handler = new OrderTrackingHandler("Track", _bridge);
        _stack.Push(handler);

        // OnActivate sets a flag; coordinator then disables keys.
        // If OnActivate ran first, the flag was set before DisableActionsForKeys.
        Assert.True(handler.WasActiveWhenDisableCalled);
    }

    private class FakeHandler : IInputHandler
    {
        public FakeHandler(string name, bool capturesAll = false, params KeyboardKey[] keys)
        {
            DisplayName = name;
            CapturesAllInput = capturesAll;
            ClaimedKeys = keys;
        }

        public string DisplayName { get; }
        public bool CapturesAllInput { get; }
        public IReadOnlyList<KeyboardKey> ClaimedKeys { get; }
        public int ActivateCount { get; set; }
        public int DeactivateCount { get; set; }

        public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers) => false;
        public bool Tick() => false;
        public void OnActivate() => ActivateCount++;
        public void OnDeactivate() => DeactivateCount++;
    }

    private class FakeBridge : IInputActionBridge
    {
        public List<KeyboardKey> DisabledKeys { get; } = new();
        public List<KeyboardKey> EnabledKeys { get; } = new();
        public bool IsActive { get; set; }

        public void DisableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
        {
            foreach (var key in keys) DisabledKeys.Add(key);
        }

        public void EnableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
        {
            foreach (var key in keys) EnabledKeys.Add(key);
        }

        public void DisableAllKeyboardActions() { }
        public void EnableAllKeyboardActions() { }

        public void ReAssertBlocking(IReadOnlyList<KeyboardKey> activeKeys, bool capturesAll) { }
    }

    /// <summary>
    /// Handler that records whether OnActivate ran before the bridge saw a disable call.
    /// </summary>
    private class OrderTrackingHandler : IInputHandler
    {
        private readonly FakeBridge _bridge;
        private bool _activated;

        public OrderTrackingHandler(string name, FakeBridge bridge)
        {
            DisplayName = name;
            _bridge = bridge;
            ClaimedKeys = new[] { KeyboardKey.Enter };
        }

        public string DisplayName { get; }
        public bool CapturesAllInput => false;
        public IReadOnlyList<KeyboardKey> ClaimedKeys { get; }
        public bool WasActiveWhenDisableCalled { get; private set; }

        public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers) => false;
        public bool Tick() => false;

        public void OnActivate()
        {
            _activated = true;
            // Snapshot: bridge shouldn't have our key disabled yet
            WasActiveWhenDisableCalled = !_bridge.DisabledKeys.Contains(KeyboardKey.Enter);
        }

        public void OnDeactivate() { }
    }
}

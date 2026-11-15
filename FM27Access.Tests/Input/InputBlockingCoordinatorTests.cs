using FM27Access.Navigation.Input;
using Xunit;

namespace FM27Access.Tests.Input;

public class InputBlockingCoordinatorTests
{
    private readonly FakeBridge _bridge = new();
    private readonly List<IInputHandler> _stackHandlers = new();
    private readonly InputBlockingCoordinator _coordinator;

    public InputBlockingCoordinatorTests()
    {
        _coordinator = new InputBlockingCoordinator(_bridge, () => _stackHandlers.AsReadOnly());
    }

    [Fact]
    public void SingleHandler_DisablesClaimedKeys()
    {
        var handler = new FakeHandler("Nav", false, KeyboardKey.Enter, KeyboardKey.Escape);
        SimulatePush(handler);

        Assert.Contains(KeyboardKey.Enter, _bridge.DisabledKeys);
        Assert.Contains(KeyboardKey.Escape, _bridge.DisabledKeys);
    }

    [Fact]
    public void SingleHandler_PopReenablesKeys()
    {
        var handler = new FakeHandler("Nav", false, KeyboardKey.Enter, KeyboardKey.Escape);
        SimulatePush(handler);
        SimulatePop(handler);

        Assert.Contains(KeyboardKey.Enter, _bridge.EnabledKeys);
        Assert.Contains(KeyboardKey.Escape, _bridge.EnabledKeys);
    }

    [Fact]
    public void OverlappingKeys_RefCounted_StaysDisabledUntilAllPopped()
    {
        var a = new FakeHandler("A", false, KeyboardKey.Enter, KeyboardKey.Escape);
        var b = new FakeHandler("B", false, KeyboardKey.Enter, KeyboardKey.UpArrow);

        SimulatePush(a);
        _bridge.DisabledKeys.Clear();
        SimulatePush(b);

        // Enter already disabled by A, only UpArrow newly disabled
        Assert.DoesNotContain(KeyboardKey.Enter, _bridge.DisabledKeys);
        Assert.Contains(KeyboardKey.UpArrow, _bridge.DisabledKeys);

        // Pop B: Enter stays disabled (A still claims it), UpArrow re-enabled
        _bridge.EnabledKeys.Clear();
        SimulatePop(b);

        Assert.DoesNotContain(KeyboardKey.Enter, _bridge.EnabledKeys);
        Assert.Contains(KeyboardKey.UpArrow, _bridge.EnabledKeys);

        // Pop A: Enter finally re-enabled
        _bridge.EnabledKeys.Clear();
        SimulatePop(a);
        Assert.Contains(KeyboardKey.Enter, _bridge.EnabledKeys);
    }

    [Fact]
    public void CapturesAllInput_DisablesAllKeyboardActions()
    {
        var handler = new FakeHandler("Edit", true, KeyboardKey.Enter);
        SimulatePush(handler);

        Assert.True(_bridge.AllDisabled);
    }

    [Fact]
    public void CapturesAllInput_PopRestoresAndReDisablesClaimed()
    {
        var nav = new FakeHandler("Nav", false, KeyboardKey.Enter, KeyboardKey.Escape);
        var edit = new FakeHandler("Edit", true, KeyboardKey.Enter);

        SimulatePush(nav);
        SimulatePush(edit);

        _bridge.Reset();
        SimulatePop(edit);

        // All re-enabled first
        Assert.True(_bridge.AllEnabled);
        // Then Nav's claimed keys re-disabled
        Assert.Contains(KeyboardKey.Enter, _bridge.DisabledKeys);
        Assert.Contains(KeyboardKey.Escape, _bridge.DisabledKeys);
    }

    [Fact]
    public void NoCaptureAll_IndividualKeysDisabledNotAll()
    {
        var handler = new FakeHandler("Nav", false, KeyboardKey.Enter);
        SimulatePush(handler);

        Assert.False(_bridge.AllDisabled);
        Assert.Contains(KeyboardKey.Enter, _bridge.DisabledKeys);
    }

    [Fact]
    public void CapturesAllInput_SkipsPerKeyDisable()
    {
        var handler = new FakeHandler("Edit", true, KeyboardKey.Enter);
        SimulatePush(handler);

        // DisableAllKeyboardActions was called, per-key disable skipped
        // because captureAll is active
        Assert.True(_bridge.AllDisabled);
        Assert.DoesNotContain(KeyboardKey.Enter, _bridge.DisabledKeys);
    }

    [Fact]
    public void NestedCapturesAllInput_OnlyEnablesOnLastPop()
    {
        var editA = new FakeHandler("EditA", true);
        var editB = new FakeHandler("EditB", true);

        SimulatePush(editA);
        _bridge.Reset();
        SimulatePush(editB);

        // Already capturing all, no new DisableAll call
        Assert.False(_bridge.AllDisabled);

        SimulatePop(editB);
        // Still one CapturesAllInput handler on stack
        Assert.False(_bridge.AllEnabled);

        SimulatePop(editA);
        // Now all are re-enabled
        Assert.True(_bridge.AllEnabled);
    }

    [Fact]
    public void ReAssertBlocking_ReportsCurrentActiveKeys_AndUpdatesOnPop()
    {
        var a = new FakeHandler("A", false, KeyboardKey.Enter, KeyboardKey.Escape);
        var b = new FakeHandler("B", false, KeyboardKey.UpArrow);
        SimulatePush(a);
        SimulatePush(b);

        _coordinator.ReAssertBlocking();
        Assert.Contains(KeyboardKey.Enter, _bridge.LastActiveKeys);
        Assert.Contains(KeyboardKey.Escape, _bridge.LastActiveKeys);
        Assert.Contains(KeyboardKey.UpArrow, _bridge.LastActiveKeys);

        // Popping B drops UpArrow from the active set; A's keys remain. Guards the cached-list
        // rebuild: a stale cache would keep reporting UpArrow after the pop.
        SimulatePop(b);
        _coordinator.ReAssertBlocking();
        Assert.DoesNotContain(KeyboardKey.UpArrow, _bridge.LastActiveKeys);
        Assert.Contains(KeyboardKey.Enter, _bridge.LastActiveKeys);
        Assert.Contains(KeyboardKey.Escape, _bridge.LastActiveKeys);
    }

    [Fact]
    public void PopHandlerWithNoClaimedKeys_DoesNotCrash()
    {
        var handler = new FakeHandler("Empty", false);
        SimulatePush(handler);
        SimulatePop(handler);
        // No exceptions thrown
    }

    private void SimulatePush(IInputHandler handler)
    {
        _stackHandlers.Add(handler);
        _coordinator.OnHandlerPushed(handler);
    }

    private void SimulatePop(IInputHandler handler)
    {
        _stackHandlers.Remove(handler);
        _coordinator.OnHandlerPopped(handler);
    }

    private class FakeHandler : IInputHandler
    {
        public FakeHandler(string name, bool capturesAll, params KeyboardKey[] keys)
        {
            DisplayName = name;
            CapturesAllInput = capturesAll;
            ClaimedKeys = keys;
        }

        public string DisplayName { get; }
        public bool CapturesAllInput { get; }
        public IReadOnlyList<KeyboardKey> ClaimedKeys { get; }

        public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers) => false;
        public bool Tick() => false;
        public void OnActivate() { }
        public void OnDeactivate() { }
    }

    private class FakeBridge : IInputActionBridge
    {
        public List<KeyboardKey> DisabledKeys { get; } = new();
        public List<KeyboardKey> EnabledKeys { get; } = new();
        public bool AllDisabled { get; private set; }
        public bool AllEnabled { get; private set; }

        public void DisableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
        {
            foreach (var key in keys) DisabledKeys.Add(key);
        }

        public void EnableActionsForKeys(IReadOnlyList<KeyboardKey> keys)
        {
            foreach (var key in keys) EnabledKeys.Add(key);
        }

        public void DisableAllKeyboardActions() => AllDisabled = true;
        public void EnableAllKeyboardActions() => AllEnabled = true;

        public List<KeyboardKey> LastActiveKeys { get; private set; } = new();
        public bool LastCapturesAll { get; private set; }

        public void ReAssertBlocking(IReadOnlyList<KeyboardKey> activeKeys, bool capturesAll)
        {
            // Snapshot: the coordinator hands us its reused cached list, so copy to capture call-time state.
            LastActiveKeys = new List<KeyboardKey>(activeKeys);
            LastCapturesAll = capturesAll;
        }

        public void Reset()
        {
            DisabledKeys.Clear();
            EnabledKeys.Clear();
            AllDisabled = false;
            AllEnabled = false;
            LastActiveKeys = new();
            LastCapturesAll = false;
        }
    }
}

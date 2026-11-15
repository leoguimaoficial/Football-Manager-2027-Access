using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Contexts;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// An explicitly requested destination can already be open while a different
// local tab is selected. Native OpenSubtab then does nothing. Resolve the entry's
// actual opening parameter and activate its existing FM26 tab adapter instead.
// No captions, guessed indices, native binding writes or forced panel reopening.
internal sealed class NativeDestinationTab
{
    // Native bindings retain a function pointer: keep the callback rooted until
    // every subscription is unbound, including during Plugin shutdown/reload.
    private static readonly Bindings.ValueChangedCallback Callback =
        new Action<Bindings.Key, SI.Core.TypedValue>(OnValue);
    private static readonly Dictionary<ulong, NativeDestinationTab> Pending = new();
    private static NativeDestinationTab Issuing;
    // Diagnostic only: a dispatch count, never a claim of observed success.
    internal static int HistorySelectionCommands { get; private set; }
    // PanelHistoryNode is a native VALUE TYPE. List reads produce boxed copies
    // with different Pointers; its official panel/instance identifiers, scoped
    // to the real stack slot, identify the logical entry instead.
    private readonly record struct HistoryKey(int Index, uint Panel, uint Identifier);
    private sealed record RememberedTabs(HistoryKey Key, Dictionary<string, int> Values,
        FixtureHistorySelection.Snapshot Fixture);
    private static readonly Dictionary<HistoryKey, RememberedTabs> HistoryTabs = new();

    private static HistoryKey Key(int index, FM.UI.PanelHistoryNode node)
        => new(index, node.PanelID, node.Identifier);

    private readonly BindingSubsystem _bindings;
    private readonly PanelID _destination;
    private readonly int _objectId;
    private readonly IntPtr _panel;
    private readonly ButtonGroup _group;
    private readonly int _selected;
    private readonly string _selectionPath;
    private readonly TaskCompletionSource<int> _value = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Bindings.Key _key;
    private bool _bound;

    private NativeDestinationTab(BindingSubsystem bindings, PanelID destination,
        int objectId, SI.Bindable.Panel panel, ButtonGroup group)
    {
        _bindings = bindings;
        _destination = destination;
        _objectId = objectId;
        _panel = panel.Pointer;
        _group = group;
        _selected = group.GetSelectedIndex();
        _selectionPath = group.SelectionBinding.m_path;
    }

    // A navigation entry can be a proxy PanelID whose OpenEvent opens a shared
    // panel with a local-tab argument. Compare against that declared native
    // target before deciding that the destination needs to be opened again.
    // Bound panel references still use ordinary native navigation; no target
    // or tab value is guessed from the proxy's name/UID.
    internal static PanelID OpeningDestination(PanelID destination,
        SI.Bindable.Events.OpenPanelEventContext opening)
    {
        if (opening?.m_argumentBindings == null) return destination;
        var reference = opening?.m_panelReference;
        if (reference == null
            || reference.kind != SI.Bindable.Events.OpenPanelEventContext.PanelReference.Kind.Asset
            || reference.asset == null) return destination;
        var asset = reference.asset.Asset;
        return asset != null ? asset.TryCast<PanelID>() ?? destination
            : reference.asset.LoadAssetAsync().WaitForCompletion() ?? destination;
    }

    // Main thread only. A null result means this destination has no matching
    // native local-tab parameter, so ordinary native navigation remains in use.
    internal static NativeDestinationTab Prepare(GameWorldTabsAndBookmarksModule module,
        PanelID destination, int objectId, SI.Bindable.Events.OpenPanelEventContext opening)
    {
        if (opening?.m_argumentBindings == null) return null;
        NativeDestinationTab request = null;
        string sourcePath = null;
        foreach (var view in InteractivePanels.Current())
        {
            if (view.Panel.PanelID?.ID != destination.ID) continue;
            var stack = new Stack<VisualElement>();
            stack.Push(view.Panel);
            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (!ElementBridge.IsVisibleInHierarchy(element)) continue;
                var grid = element.TryCast<TabbedGridLayoutElement>();
                var group = grid?.m_buttons;
                if (group != null && ElementBridge.IsVisibleInHierarchy(group))
                    foreach (var argument in opening.m_argumentBindings)
                    {
                        if (argument == null || string.IsNullOrEmpty(argument.name)
                            || argument.name != grid.SelectionBinding.m_path
                            || argument.name != group.SelectionBinding.m_path) continue;
                        if (request != null)
                            throw new InvalidOperationException("The destination's local tab is ambiguous. Refresh the screen.");
                        sourcePath = argument.path.m_path;
                        if (string.IsNullOrEmpty(sourcePath))
                            throw new InvalidOperationException("The destination's local-tab parameter is not bound. Refresh the screen.");
                        request = new NativeDestinationTab(SI.Core.GameSubsystems.Get<BindingSubsystem>(),
                            destination, objectId, view.Panel, group);
                    }
                for (int i = element.hierarchy.childCount - 1; i >= 0; i--)
                    stack.Push(element.hierarchy[i]);
            }
        }
        if (request == null) return null;
        // Navigation has one native foreground. A newer request cancels an
        // earlier value read before either can issue a stale UI command.
        foreach (var previousRequest in new List<NativeDestinationTab>(Pending.Values))
        {
            previousRequest._value.TrySetException(new InvalidOperationException("A newer navigation request replaced this destination."));
            previousRequest.Close();
        }
        var previous = Issuing;
        try
        {
            Issuing = request;
            var scope = module.m_openEventScope;
            var path = Il2CppSystem.MemoryExtensions.AsSpan(sourcePath);
            request._key = BindingUtils.BindWithinScope(request._bindings, ref scope,
                ref path, request._group, Callback);
            request._bound = true;
            Pending.Add(request._key.m_key, request);
            return request;
        }
        catch { request.Close(); throw; }
        finally { Issuing = previous; }
    }

    private static void OnValue(Bindings.Key key, SI.Core.TypedValue value)
    {
        var request = Pending.TryGetValue(key.m_key, out var pending) ? pending : Issuing;
        if (request == null) return;
        try
        {
            var number = BindingRecords.NumberOf(value);
            if (!number.HasValue) return; // The initial unbound callback may precede its value.
            if (number.Value < int.MinValue || number.Value > int.MaxValue
                || number.Value != Math.Truncate(number.Value))
                throw new InvalidOperationException("The destination's native tab value is invalid.");
            request._value.TrySetResult((int)number.Value);
        }
        catch (Exception error) { request._value.TrySetException(error); }
    }

    // Only the HTTP caller waits. Unity continues frames and resolves its own
    // binding. A timeout never leaves a deferred UI command behind.
    internal int WaitForValue()
    {
        if (!_value.Task.Wait(1500))
            throw new TimeoutException("The destination's local-tab value is not ready. Refresh the screen.");
        return _value.Task.GetAwaiter().GetResult();
    }

    // Main thread only, immediately before the sole native command.
    internal bool Activate(int value)
    {
        var module = BookmarkBar.Module();
        bool visibleOwner = false;
        foreach (var view in InteractivePanels.Current())
            if (view.Panel.Pointer == _panel) visibleOwner = true;
        if (!visibleOwner || !module.IsSameAsCurrentSubtab(_destination, _objectId)
            || !ElementBridge.IsVisibleInHierarchy(_group)
            || _group.SelectionBinding.m_path != _selectionPath
            || _group.GetSelectedIndex() != _selected)
            throw new InvalidOperationException("The destination changed while its local tab was loading. Choose it again.");
        return ActivateGroup(_group, value);
    }

    private static bool ActivateGroup(ButtonGroup group, int value)
    {
        int index = group.GetIndexForOptionValue(value);
        if (index < 0 || index >= group.Count || !group.IsSelectable(index))
            throw new InvalidOperationException("The requested native tab is not available right now.");
        if (index == group.GetSelectedIndex()) return false;
        var slot = group.m_slots[index];
        var button = slot == null ? null : new Il2CppSystem.Object(slot.Pointer).TryCast<SIButton>();
        if (button == null)
            throw new InvalidOperationException("The destination's native tab button could not be resolved.");
        foreach (var tab in new TabContext(group, new ClassificationCache()).GetAllTabs())
            if (tab.BackingElement.Pointer == button.Pointer)
            {
                tab.Activate();
                return true; // Native activation may replace the content tree.
            }
        throw new InvalidOperationException("The requested native tab is no longer on screen.");
    }

    // Opening arguments can remain unchanged after a real local tab selection.
    // Remember the ACTUAL native option immediately before user actions, scoped
    // to its actual history node and owner. Never treat an opening default as
    // the user's last selection; never rewrite the game's history/model.
    internal static void RememberHistorySelection()
    {
        var live = GameHistory.TryRead();
        if (live == null) return;
        var nodes = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager?.m_history;
        if (nodes == null) return;
        foreach (var saved in new List<RememberedTabs>(HistoryTabs.Values))
            if (saved.Key.Index >= nodes.Count || Key(saved.Key.Index, nodes[saved.Key.Index]) != saved.Key)
                HistoryTabs.Remove(saved.Key);
        var node = nodes[live.Value.Index];
        var key = Key(live.Value.Index, node);
        var panel = FM27PanelCatalog.GetForegroundPanel();
        if (panel?.PanelID?.ID != node.PanelID || panel.Identifier != node.Identifier) return;
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var fixtureLists = new List<VisualElement>();
        foreach (var entry in HistorySelection.CollectGroups(panel, HistorySelection.OpeningPaths(node), fixtureLists))
        {
            int selected = entry.Value.GetSelectedIndex();
            if (selected < 0 || selected >= entry.Value.Count || !entry.Value.IsSelectable(selected)) continue;
            int option = entry.Value.GetOptionValue(selected);
            if (entry.Value.GetIndexForOptionValue(option) != selected)
                throw new InvalidOperationException("The current native tab option is ambiguous.");
            values.Add(entry.Key, option);
        }
        var fixture = FixtureHistorySelection.Capture(fixtureLists, out bool fixturePending);
        if (fixturePending || values.Count == 0 && fixture == null) return;
        if (HistoryTabs.TryGetValue(key, out var previous)
            && previous.Fixture == fixture
            && previous.Values.Count == values.Count
            && values.All(pair => previous.Values.TryGetValue(pair.Key, out int value) && value == pair.Value)) return;
        HistoryTabs[key] = new RememberedTabs(key, values, fixture);
    }

    // Restore only selections actually observed before leaving this exact node,
    // through the SAME native option mapping and tab adapter as navigation.
    internal sealed class HistorySelection
    {
        private readonly HistoryKey _key;
        private readonly Dictionary<string, int> _values;
        private readonly RememberedTabs _snapshot;
        private readonly HashSet<string> _issued = new(StringComparer.Ordinal);
        private readonly FixtureHistorySelection _fixture;
        internal bool Issued => _issued.Count != 0 || _fixture?.Issued == true;
        internal bool HasFixture => _fixture != null;

        private HistorySelection(RememberedTabs snapshot)
        {
            _snapshot = snapshot; _key = snapshot.Key; _values = snapshot.Values;
            if (snapshot.Fixture != null) _fixture = new FixtureHistorySelection(snapshot.Fixture);
        }

        internal static HistorySelection Begin(int index)
        {
            if (GameHistory.TryRead()?.Index != index) return null;
            var nodes = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager?.m_history;
            if (nodes == null || index < 0 || index >= nodes.Count) return null;
            var node = nodes[index];
            return HistoryTabs.TryGetValue(Key(index, node), out var snapshot)
                ? new HistorySelection(snapshot) : null;
        }

        internal static HashSet<string> OpeningPaths(FM.UI.PanelHistoryNode node)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (node.Arguments == null) return result;
            foreach (var argument in node.Arguments)
            {
                if (argument.Parent != FM.UI.PanelHistoryNode.NoParentIndex
                    || string.IsNullOrEmpty(argument.Name)) continue;
                var number = BindingRecords.NumberOf(argument.Value);
                if (!number.HasValue || number.Value < int.MinValue || number.Value > int.MaxValue
                    || number.Value != Math.Truncate(number.Value)) continue;
                // Only the declared path is relevant. Its opening value can
                // remain stale after the user changes the actual native tab.
                result.Add(argument.Name);
            }
            return result;
        }

        // Main thread only. False means await actual panel/selection readiness.
        // Every native tab command is issued at most once for this user press.
        internal bool Step()
        {
            if (GameHistory.TryRead()?.Index != _key.Index) return true;
            var nodes = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager?.m_history;
            if (nodes == null || _key.Index >= nodes.Count || Key(_key.Index, nodes[_key.Index]) != _key) return true;
            if (!HistoryTabs.TryGetValue(_key, out var current) || !ReferenceEquals(current, _snapshot)) return true;

            var panel = FM27PanelCatalog.GetForegroundPanel();
            if (panel?.PanelID?.ID != _key.Panel || panel.Identifier != _key.Identifier) return false;
            var fixtureLists = _fixture == null ? null : new List<VisualElement>();
            var groups = CollectGroups(panel, _values.Keys, fixtureLists);
            // A landed panel can precede its tab controls. An empty/partial
            // native tree is not evidence that every retained option arrived.
            if (groups.Count != _values.Count) return false;
            foreach (var entry in groups)
            {
                int target = entry.Value.GetIndexForOptionValue(_values[entry.Key]);
                if (target < 0 || target >= entry.Value.Count || !entry.Value.IsSelectable(target)) return false;
                if (entry.Value.GetSelectedIndex() == target) continue;
                if (!_issued.Add(entry.Key)) return false;
                if (ActivateGroup(entry.Value, _values[entry.Key])) HistorySelectionCommands++;
                return false;
            }
            return _fixture?.Step(fixtureLists) ?? true;
        }

        internal static Dictionary<string, ButtonGroup> Groups(SI.Bindable.Panel panel, ICollection<string> paths)
            => CollectGroups(panel, paths, null);

        internal static Dictionary<string, ButtonGroup> CollectGroups(SI.Bindable.Panel panel,
            ICollection<string> paths, List<VisualElement> fixtureLists)
        {
            var groups = new Dictionary<string, ButtonGroup>(StringComparer.Ordinal);
            if (fixtureLists != null)
                foreach (var list in NativeTreeQuery.All<StreamedObjectList>(panel, "FixturesList"))
                    if (ElementBridge.IsVisibleInHierarchy(list) && list.GetIl2CppType().Name == "StreamedObjectList")
                        fixtureLists.Add(list);
            foreach (var grid in NativeTreeQuery.All<TabbedGridLayoutElement>(panel))
            {
                if (!ElementBridge.IsVisibleInHierarchy(grid)) continue;
                var group = grid?.m_buttons;
                if (group != null && ElementBridge.IsVisibleInHierarchy(group))
                {
                    var path = grid.SelectionBinding.m_path;
                    if (!string.IsNullOrEmpty(path) && path == group.SelectionBinding.m_path && paths.Contains(path))
                    {
                        if (!groups.TryAdd(path, group))
                            throw new InvalidOperationException("The history destination's local tab is ambiguous.");
                    }
                }
            }
            return groups;
        }
    }

    // Background caller, using the existing single history-press budget. Native
    // reads/commands remain on Unity; a replaced destination cancels its restore.
    internal static void RestoreHistorySelection(int index, long deadline, Func<bool> superseded)
    {
        var request = MainThreadDispatcher.Invoke(() => HistorySelection.Begin(index));
        if (request == null) return;
        string baseline = SseStreamer.CaptureShapeBaseline();
        bool fixtureLease = false;
        try
        {
            if (request.HasFixture)
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0 || !(fixtureLease = System.Threading.Monitor.TryEnter(
                    FixtureScheduleLoader.ReadLock, (int)Math.Min(left, int.MaxValue))))
                    throw new TimeoutException("The history fixture list is still updating.");
            }
            while (!superseded() && Environment.TickCount64 < deadline)
            {
                bool settled = MainThreadDispatcher.Invoke(() => superseded()
                    || Environment.TickCount64 < deadline && request.Step());
                if (settled) return;
                System.Threading.Thread.Sleep(16);
            }
            if (!superseded()) throw new TimeoutException("The history destination's native tab did not become ready.");
        }
        finally
        {
            if (fixtureLease) System.Threading.Monitor.Exit(FixtureScheduleLoader.ReadLock);
            // Local tabs can replace their content without changing panel ID.
            // Reuse the original bounded follow-up, never reissue a command.
            if (request.Issued) SseStreamer.ArmSettleAfterAction(baseline);
        }
    }

    internal void Close()
    {
        if (!_bound) return;
        _bound = false;
        Pending.Remove(_key.m_key);
        _bindings.Unbind(ref _key, Callback);
    }

    internal static void Shutdown()
    {
        HistoryTabs.Clear();
        foreach (var request in new List<NativeDestinationTab>(Pending.Values))
        {
            request._value.TrySetException(new InvalidOperationException("Navigation stopped during Plugin reload."));
            request.Close();
        }
    }
}

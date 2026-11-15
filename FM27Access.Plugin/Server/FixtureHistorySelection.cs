using FM27Access.Plugin.Contexts;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// Calendar's native selected-fixture list does not persist selection on reopen.
// Retain only its observed scalar/reference identity in the existing history
// snapshot; never rewrite history arguments, persistence flags or model values.
internal sealed class FixtureHistorySelection
{
    internal sealed record Snapshot(string Path, int Index, string Reference);
    private readonly Snapshot _snapshot;
    internal bool Issued { get; private set; }

    internal FixtureHistorySelection(Snapshot snapshot) => _snapshot = snapshot;

    internal static Snapshot Capture(IReadOnlyList<VisualElement> lists, out bool pending)
    {
        pending = false;
        Snapshot result = null;
        foreach (var list in lists)
        {
            // The name alone is not a selection contract: unrelated readonly
            // fixture lists must not suspend ordinary local-tab snapshots.
            if (!IsFixtureSelectionList(list)) continue;
            var value = Read(list);
            if (value == null) { pending = true; continue; }
            if (result != null) throw new InvalidOperationException("The selected fixture's native list is ambiguous.");
            result = value;
        }
        return result;
    }

    private static bool IsFixtureSelectionList(VisualElement element)
    {
        var list = element?.TryCast<StreamedObjectList>();
        if (list == null || string.IsNullOrEmpty(list.SelectedItemsBinding.m_path)
            || NativeList(list)?.SelectionMode != SI.UI.VirtualisedList.SelectionModes.ExactlyOne
            || OwnerReference(list) == null) return false;
        var key = list.m_selectedItemsKey;
        var record = BindingRecords.RecordOf(SI.Core.GameSubsystems.Get<BindingSubsystem>().Get(ref key));
        if (record == null) return true;
        var fields = BindingRecords.Fields(record);
        return fields.ContainsKey("Fixture") && fields.ContainsKey("Index") || fields.Count == 0;
    }

    // Read the actual bound selectedfixturelistelement record, not a temporary
    // checkbox/controller flag. The independent selectedfixture owner must agree.
    internal static Snapshot Read(VisualElement element)
    {
        var list = element?.TryCast<StreamedObjectList>();
        if (list == null || list.m_selectionRestorePending || !ElementBridge.IsVisibleInHierarchy(list)) return null;
        string path = list.SelectedItemsBinding.m_path;
        if (string.IsNullOrEmpty(path)) return null;
        var key = list.m_selectedItemsKey;
        var record = BindingRecords.RecordOf(SI.Core.GameSubsystems.Get<BindingSubsystem>().Get(ref key));
        if (record == null) return null;
        var fields = BindingRecords.Fields(record);
        var reference = BindingRecords.Field(fields, "Fixture")?.Get()?.TryCast<FM.UI.FixtureReference>();
        var identity = BindingRecords.ReferenceIdentity(reference);
        var number = BindingRecords.NumberOf(BindingRecords.Field(fields, "Index"));
        if (identity == null || !number.HasValue || number < 0 || number > int.MaxValue
            || number != Math.Truncate(number.Value)) return null;
        var native = NativeList(list);
        if (native?.SelectionMode != SI.UI.VirtualisedList.SelectionModes.ExactlyOne
            || native.SelectionInternal?.Count != 1 || native.SelectionInternal[0] != (int)number.Value) return null;
        if (OwnerReference(list) != identity) return null;
        return new(path, (int)number.Value, identity);
    }

    private static SI.UI.VirtualisedList NativeList(VisualElement list)
    {
        for (int i = 0; i < list.hierarchy.childCount; i++)
        {
            var native = list.hierarchy[i].TryCast<SI.UI.VirtualisedList>();
            if (native != null) return native;
        }
        return null;
    }

    private static string OwnerReference(VisualElement list)
    {
        for (var parent = list.parent; parent != null; parent = parent.parent)
        {
            var node = BindingRecords.NodeOf(parent);
            if (node == null) continue;
            var selected = BindingRecords.FindNode(node, "selectedfixture", 3);
            if (selected == null) continue;
            return BindingRecords.ReferenceIdentity(BindingRecords.ValueOf(selected)?.Get()?.TryCast<FM.UI.FixtureReference>());
        }
        return null;
    }

    // Main thread, under the background caller's existing fixture read lease.
    // The original data index is only a realization hint: reference identity is
    // revalidated before the sole native selection and throughout observation.
    internal bool Step(IReadOnlyList<VisualElement> lists)
    {
        VisualElement match = null;
        foreach (var element in lists)
        {
            var list = element.TryCast<StreamedObjectList>();
            if (list?.SelectedItemsBinding.m_path != _snapshot.Path) continue;
            if (match != null) throw new InvalidOperationException("The history fixture list is ambiguous.");
            match = list;
        }
        if (match == null) return false;
        var current = Read(match);
        if (current == null) return false;
        if (current.Reference == _snapshot.Reference && current.Index == _snapshot.Index) return true;
        if (Issued) return false;
        var context = new FixtureScheduleContext(match, Array.Empty<string>());
        if (context.ScrollToRowPending(_snapshot.Index) != 0) return false;
        if (!context.MatchesRowIdentity(_snapshot.Index, _snapshot.Reference))
            throw new InvalidOperationException("The previously selected fixture has changed. Choose it again.");
        Issued = true;
        if (!context.SelectRow(_snapshot.Index, out _))
            throw new InvalidOperationException("The previously selected fixture is not available now.");
        return false;
    }
}

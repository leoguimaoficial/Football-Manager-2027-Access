using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Target element → its <c>ContextMenuDescriptionManipulator</c>, for the menus whose manipulator the
/// element does NOT expose as a field.
///
/// <see cref="ContextMenuOpener"/>'s universal detector reflects the element's IL2CPP type for a field
/// of that type. Exactly four types in the game have one — <c>SIText</c>,
/// <c>PluginContextMenuBindings</c>, <c>FMCalendarEvent</c>, <c>FMPitchIndicator</c> — and they cover
/// every menu the mod surfaced up to now. A third attachment shape exists:
/// <c>SI.Bindable.StreamedTable.AddContextMenu</c> builds a fresh manipulator per column header from a
/// local closure and hands it straight to <c>AddManipulator</c>. UI Toolkit keeps no back-reference on
/// the element, so there is nothing to reflect and no instance to drive — the header's menu was
/// structurally invisible to the mod. This registry is the missing back-reference, recorded by
/// <see cref="Patches.ContextMenuManipulatorRegisterPatch"/> at the moment the manipulator binds.
///
/// Only NON-field-backed manipulators are kept: the field types are already reachable, and registering
/// them too would pin every hyperlink text in the game. Main thread only (the patches run on the UI
/// thread and every reader is a main-thread path), so no locking.
/// </summary>
internal static class ManipulatorRegistry
{
    // Keyed by the target's IL2CPP pointer. Safe as an identity: the entry holds the manipulator,
    // which holds its target, so the native object cannot be freed and its address cannot be recycled
    // under a live entry. (Recycled addresses are exactly how a stale IL2CPP key returns the wrong
    // object — see the context-menu zombie-panel finding — so the pinning is deliberate, not a leak
    // we tolerate.)
    private static readonly Dictionary<IntPtr, SI.UI.ContextMenuDescriptionManipulator> _byTarget = new();

    // Above this, a prune runs on the next registration. A column-header manipulator dies with its
    // table, and nothing tells us — the game drops the element without unbinding the manipulator — so
    // detached entries are swept rather than accumulating for the whole session.
    private const int PruneThreshold = 1024;

    internal static int Count => _byTarget.Count;

    /// <summary>
    /// Record a manipulator against its target. A no-op when the target already exposes the
    /// manipulator as a field (the existing reflection path serves it) or when either side is null.
    /// </summary>
    internal static void Register(VisualElement target, SI.UI.ContextMenuDescriptionManipulator manipulator)
    {
        if (target == null || manipulator == null) return;
        if (ContextMenuOpener.HasMenuManipulatorField(target)) return;
        if (_byTarget.Count >= PruneThreshold) Prune();
        _byTarget[target.Pointer] = manipulator;
    }

    /// <summary>Drop a manipulator that is unbinding from its target.</summary>
    internal static void Unregister(VisualElement target)
    {
        if (target == null) return;
        _byTarget.Remove(target.Pointer);
    }

    /// <summary>The manipulator recorded for this element, or null.</summary>
    internal static SI.UI.ContextMenuDescriptionManipulator Get(VisualElement element)
    {
        if (element == null) return null;
        return _byTarget.TryGetValue(element.Pointer, out var m) ? m : null;
    }

    internal static void Clear() => _byTarget.Clear();

    // Drop entries whose target has left the panel tree. A detached element in this game is being torn
    // down (PanelManager destroys panels rather than parking them), so a null panel means the menu is
    // gone with it. The cost of being wrong is bounded and honest: the element renders without its
    // aria-haspopup mark and a rebuild re-registers it — a missing affordance, never a wrong one.
    private static void Prune()
    {
        var dead = new List<IntPtr>();
        foreach (var pair in _byTarget)
        {
            VisualElement target;
            try { target = pair.Value.target; }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"manipulator registry: target read failed ({ex.GetType().Name}); entry dropped");
                dead.Add(pair.Key);
                continue;
            }
            if (target == null || target.panel == null) dead.Add(pair.Key);
        }
        foreach (var key in dead) _byTarget.Remove(key);
        Speech.Log.Info($"[ManipulatorRegistry] pruned {dead.Count} detached of {dead.Count + _byTarget.Count}.");
    }
}

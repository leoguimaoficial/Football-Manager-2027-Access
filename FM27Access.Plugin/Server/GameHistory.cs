using System;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Read-only view of the GAME's own screen history — the stack the Back/Forward buttons walk, and
/// the anchor the mod's virtual history entries (an open full-screen Message view) are pinned to.
/// See docs/message-view-history-plan.md.
///
/// The mod does not own this stack and never writes to it: back and forward still happen by clicking
/// the game's own buttons (the NavigateBack/NavigateForward delegates in CoreEntry), so nothing here
/// can desynchronise the game's navigation. All this class does is answer "where in the stack are we,
/// and what shape is it".
///
/// Deliberately reflection-free. FM27 exposes the position as PropertyValue in the Current
/// DynamicReference, the same data used by its history selector. Never read the manager's
/// ref-returning CurrentIndex or box its ValueWithChangeNotify&lt;int&gt; backing field.
/// The node count comes from the history string
/// rather than the <c>List&lt;PanelHistoryNode&gt;</c> field for the same reason (verified against the
/// field live: every observed pair agreed, including the empty stack a save load leaves).
///
/// Main thread only (it touches live IL2CPP objects). Nothing is cached: every call re-reads.
/// </summary>
internal static class GameHistory
{
    internal static bool IsAtForeground(int index)
    {
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        // Native history sets this guard for its own asynchronous replay and
        // releases it at completion. Do not start a virtual Inbox reopen while
        // the game's preceding Forward is still finishing its native callback.
        if (manager == null || manager.m_ignoreEvents) return false;
        var nodes = manager.m_history;
        if (nodes == null || index < 0 || index >= nodes.Count) return false;
        var node = nodes[index];
        // Native Back can reuse another already mounted instance of the same
        // destination (observed Portal node Identifier79, actual panel80).
        // The node's Identifier guards retained slots, not transition readiness.
        // FMNavigationManager's highest sort entry can be OverviewTitleBar on
        // Portal. Use the existing actual interactive set, which includes that
        // persistent chrome and the current content and suppresses backgrounds.
        foreach (var view in InteractivePanels.Current())
            if (view.Panel.PanelID?.ID == node.PanelID) return true;
        return false;
    }

    /// <summary>How GetPanelHistoryString joins node names. Shared with MessageViewHistory's prefix
    /// comparison, which splits the same string.</summary>
    private const string NodeSeparator = ", ";

    // The manager only goes missing wholesale (before init, or a game build that moved it), so one
    // note is the whole story; without this the warning would repeat on every navigation.
    private static bool _noted;

    /// <summary>
    /// The live stack: the current position, how many nodes there are, the game's own history string
    /// (node names in order — the shape an entry is validated against), and whether the game considers
    /// back/forward available. Null when the history cannot be read, which every caller treats as
    /// "behave exactly as before this feature existed".
    /// </summary>
    internal static (int Index, int Count, string Shape, bool CanBack, bool CanForward)? TryRead()
    {
        try
        {
            var module = FM.UI.PanelHistoryManagerModule.Instance;
            var manager = module?.PanelHistoryManager;
            if (manager == null)
                return NoteOnce("PanelHistoryManager not available yet");

            var shape = manager.GetPanelHistoryString() ?? "";
            int count = NodeCount(shape);
            var current = manager.Current?.Get()?.TryCast<SI.Bindable.DynamicReference>();
            if (current == null) return null;
            var names = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            foreach (var key in current.Keys)
            {
                if (names.GetInfo(key).Name != "PropertyValue") continue;
                var value = BindingRecords.NumberOf(current[key]);
                if (value == null || value < 0 || value >= count || value != Math.Truncate(value.Value))
                    return null;
                return ((int)value.Value, count, shape, manager.CanPrevious, manager.CanNext);
            }
            return null;
        }
        catch (Exception ex)
        {
            return NoteOnce($"history read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int NodeCount(string shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return 0;
        int count = 1;
        int at = 0;
        while ((at = shape.IndexOf(NodeSeparator, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += NodeSeparator.Length;
        }
        return count;
    }

    private static (int, int, string, bool, bool)? NoteOnce(string reason)
    {
        if (!_noted)
        {
            _noted = true;
            Degrade.Note($"[GameHistory] {reason}");
        }
        return null;
    }
}

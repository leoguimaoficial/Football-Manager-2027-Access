using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

/// <summary>
/// The Player Interest dialog (PlayerSearchTransferInterestDialog) commits a stale interest level
/// when Ok is pressed without a sub-option click: the sub-radios display the current filter value,
/// but the dialog's commit model only syncs when a radio is actually clicked — verified live,
/// open-then-Ok flipped "Persuadable" and "Interested" to "Doubtful" while the radios showed the
/// true value throughout. A mouse player virtually always clicks before Ok, so the game never
/// shows this; a screen-reader player reviewing the dialog and leaving with Ok gets their filter
/// silently changed. When the dialog opens, re-click the already-selected sub-radio once so the
/// model matches the display and Ok always commits exactly what is shown.
/// </summary>
internal static class InterestDialogSync
{
    private const string DialogPanelName = "PlayerSearchTransferInterestDialog";
    private const float TimeoutSeconds = 4f;

    /// <summary>Called from PanelLoadedPatch for every loaded panel, after OnPanelReady.</summary>
    public static void OnPanelLoaded(string panelName, VisualElement panel)
    {
        try
        {
            if (panelName != DialogPanelName) return;
            var state = new SyncState(panel);
            panel.schedule.Execute((Action)state.Tick);
        }
        catch (Exception ex)
        {
            // A failed sync leaves the dialog exactly as the game built it; the gap is the
            // game's own quirk, not new breakage.
            Speech.Log.Error($"[InterestDialogSync] panel-loaded handling failed, continuing: {ex}");
        }
    }

    private sealed class SyncState
    {
        private readonly VisualElement _panel;
        private readonly float _deadline;

        public SyncState(VisualElement panel)
        {
            _panel = panel;
            _deadline = UnityEngine.Time.realtimeSinceStartup + TimeoutSeconds;
        }

        public void Tick()
        {
            try
            {
                if (!AccessibilityMod.IsAlive || _panel.panel == null)
                    return; // mod halted or dialog already closed

                if (UnityEngine.Time.realtimeSinceStartup > _deadline)
                {
                    // No selected sub-option appeared: either the dialog opened on a parent with
                    // no sub-list or nothing is selected yet. There is nothing to sync — the
                    // player's own first click will set the model.
                    Speech.Log.Info("[InterestDialogSync] no selected sub-option to sync before timeout.");
                    return;
                }

                var selected = FindSelectedSubRadio(_panel);
                if (selected == null)
                {
                    _panel.schedule.Execute((Action)Tick); // still binding; try next frame
                    return;
                }

                // Re-clicking the selected radio is a no-op at the model level too (the game's
                // click handler early-outs on an unchanged value — verified live), so the sync
                // needs two REAL changes: click a sibling, then click the original back next
                // frame. The dialog is modal and applies nothing until Ok, so the intermediate
                // state never reaches the filter.
                var sibling = FindUnselectedSibling(selected);
                if (sibling == null)
                {
                    Speech.Log.Info("[InterestDialogSync] no sibling to toggle through; leaving the dialog as built.");
                    return;
                }
                var original = new RadioButtonAdapter(selected);
                string label = original.GetLabel();
                new RadioButtonAdapter(sibling).Activate();
                _panel.schedule.Execute((Action)(() =>
                {
                    try
                    {
                        original.Activate();
                        Speech.Log.Info($"[InterestDialogSync] toggled through a sibling back to \"{label}\" so Ok commits the displayed value.");
                    }
                    catch (Exception ex)
                    {
                        // The toggle-back failing would leave the SIBLING selected — visible and
                        // honest (the radios always show the game's real state), but log loudly.
                        Speech.Log.Error($"[InterestDialogSync] toggle-back failed, selection now shows the sibling: {ex}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"interest dialog sync failed mid-run: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // The selected radio of a sub-option group: a visible, value=true SIRadioButton inside a
    // nested SIRadioGroup that itself lives inside a (parent) SIRadioButton's subtree. The
    // nesting requirement keeps the outer group's parents out of the search — re-clicking a
    // parent would be a different action (it re-fires the sub-list switch), not a no-op sync.
    // An unselected, visible radio in the same nested group as the selected one — the toggle
    // partner. Null when the group has no other member.
    private static VisualElement FindUnselectedSibling(VisualElement selected)
    {
        var group = selected.hierarchy.parent;
        while (group != null && group.GetIl2CppType().Name != "SIRadioGroup")
            group = group.hierarchy.parent;
        if (group == null) return null;
        return FindVisibleUnselectedRadio(group, selected);
    }

    private static VisualElement FindVisibleUnselectedRadio(VisualElement root, VisualElement not)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "SIRadioButton")
            {
                if (child != not && !child.Cast<SI.Bindable.SIRadioButton>().value)
                    return child;
                continue; // the selected one, or a radio nested deeper than this group's level
            }
            var deeper = FindVisibleUnselectedRadio(child, not);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static VisualElement FindSelectedSubRadio(VisualElement root)
    {
        // Depth-first hits the selected PARENT radio before its nested subs (the sub-group
        // lives inside the parent's subtree), so this resolves outer-then-inner naturally.
        var parentRadio = FindVisibleOfType(root, "SIRadioButton", selectedOnly: true);
        if (parentRadio == null) return null;
        var nestedGroup = FindVisibleOfType(parentRadio, "SIRadioGroup", selectedOnly: false);
        if (nestedGroup == null) return null;
        return FindVisibleOfType(nestedGroup, "SIRadioButton", selectedOnly: true);
    }

    private static VisualElement FindVisibleOfType(VisualElement root, string typeName, bool selectedOnly)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == typeName
                && (!selectedOnly || child.Cast<SI.Bindable.SIRadioButton>().value))
                return child;
            var deeper = FindVisibleOfType(child, typeName, selectedOnly);
            if (deeper != null) return deeper;
        }
        return null;
    }
}

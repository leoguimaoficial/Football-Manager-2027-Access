using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

/// <summary>
/// The Appearance step of Head Coach Creation is a grid of preset portrait thumbnails —
/// purely visual, with no text the mod could ever speak. When the step opens with no preset
/// chosen yet, pick one at random and confirm, landing the player on the Biography step.
/// When a preset is already chosen (the player came back via Back or Edit Head Coach), leave
/// the page alone: it stays browsable, Confirm moves forward, and a second Back leaves the
/// creation flow.
/// </summary>
internal static class AppearanceAutoSkip
{
    private const string CreationPanelName = "ManagerCreationScreen";
    private const float TimeoutSeconds = 8f;
    private const float NoticeMaxAgeSeconds = 10f;

    private static readonly System.Random Rng = new();

    // Spoken after the next persona step announces itself, so the player first hears where
    // they landed, then why. Age-guarded so a skip that goes somewhere unexpected can never
    // replay a stale notice later.
    private static string _pendingNotice;
    private static float _noticeSetAt;

    /// <summary>Called from PanelLoadedPatch for every loaded panel, after OnPanelReady.</summary>
    public static void OnPanelLoaded(string panelName, VisualElement panel)
    {
        try
        {
            if (panelName.StartsWith("ManagerPersona"))
            {
                var notice = _pendingNotice;
                _pendingNotice = null;
                if (notice != null && UnityEngine.Time.realtimeSinceStartup - _noticeSetAt < NoticeMaxAgeSeconds)
                    Speech.SpeechPipeline.SpeakQueued(notice);
                return;
            }

            if (panelName == CreationPanelName)
                Begin(panel);
        }
        catch (Exception ex)
        {
            // One failed auto-skip must not disturb panel handling; the page stays usable as-is.
            Speech.Log.Error($"[AppearanceAutoSkip] panel-loaded handling failed, continuing: {ex}");
        }
    }

    private static void Begin(VisualElement panel)
    {
        Speech.Log.Info("[AppearanceAutoSkip] Appearance step loaded, watching for preset grid.");
        var state = new SkipState(panel);
        panel.schedule.Execute((Action)state.Tick);
    }

    private sealed class SkipState
    {
        private readonly VisualElement _panel;
        private readonly float _deadline;

        // 0 = deciding (find grid, gate on selection), 1 = preset clicked, waiting for
        // Confirm to enable, 2 = Confirm clicked, waiting for the panel to close.
        private int _phase;
        private bool _done;

        public SkipState(VisualElement panel)
        {
            _panel = panel;
            _deadline = UnityEngine.Time.realtimeSinceStartup + TimeoutSeconds;
        }

        public void Tick()
        {
            try
            {
                if (!AccessibilityMod.IsAlive || _panel.panel == null)
                    return; // mod halted or panel torn down (after Confirm this is success)

                if (UnityEngine.Time.realtimeSinceStartup > _deadline)
                {
                    OnTimeout();
                    return;
                }

                switch (_phase)
                {
                    case 0: Decide(); break;
                    case 1: ConfirmWhenEnabled(); break;
                    case 2: break; // panel still alive; keep watching until it closes or times out
                }

                if (!_done)
                    _panel.schedule.Execute((Action)Tick);
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"appearance auto-skip failed mid-run: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Decide()
        {
            var confirm = FindConfirmButton(_panel);
            var presets = FindPresetButtons(_panel);
            if (confirm == null || presets.Count == 0)
                return; // still populating; re-armed by Tick

            if (confirm.enabledInHierarchy)
            {
                // Confirm is live, so the game already holds a selection. If a tile shows the
                // selected marker this is a return visit — leave the page alone. Without the
                // marker the state is ambiguous (bindings may still be applying); keep waiting
                // and let the timeout resolve to "don't touch".
                if (AnyPresetSelected(presets))
                {
                    Speech.Log.Info("[AppearanceAutoSkip] Preset already selected; leaving the page alone.");
                    _done = true;
                }
                return;
            }

            var pick = presets[Rng.Next(presets.Count)];
            var adapter = new PresetButtonAdapter(pick);
            var label = adapter.GetLabel();
            adapter.Activate();
            _pendingNotice = "Appearance menu inaccessible, automatically skipped";
            _noticeSetAt = UnityEngine.Time.realtimeSinceStartup;
            Speech.Log.Info($"[AppearanceAutoSkip] Clicked {label} of {presets.Count} presets.");
            _phase = 1;
        }

        private void ConfirmWhenEnabled()
        {
            var confirm = FindConfirmButton(_panel);
            if (confirm == null || !confirm.enabledInHierarchy)
                return;

            _noticeSetAt = UnityEngine.Time.realtimeSinceStartup;
            new ButtonAdapter(confirm).Activate();
            Speech.Log.Info("[AppearanceAutoSkip] Clicked Confirm.");
            _phase = 2;
        }

        private void OnTimeout()
        {
            if (_phase == 0)
            {
                // Never touched anything; the page is exactly what it would be without this
                // feature. Log the gap for the dev driver and stay quiet.
                Speech.Degrade.Note("appearance auto-skip: no actionable state before timeout (grid or Confirm missing, or selection state ambiguous)");
            }
            else
            {
                // A preset was clicked but the step never advanced. The player must finish by hand.
                _pendingNotice = null;
                Speech.Degrade.Note($"appearance auto-skip: stalled in phase {_phase} before timeout");
                Speech.SpeechPipeline.SpeakQueued("Appearance auto-select failed, choose a preset, then Confirm");
            }
        }
    }

    private static VisualElement FindConfirmButton(VisualElement panel)
    {
        var rightButtons = panel.Q<VisualElement>("right-side-buttons");
        return rightButtons?.Q<SI.Bindable.SIButton>("buttons-button-primary-default");
    }

    private static List<VisualElement> FindPresetButtons(VisualElement panel)
    {
        var found = new List<VisualElement>();
        var section = panel.Q<VisualElement>("PresetsSection");
        if (section != null)
            CollectPresetButtons(section, found);
        return found;
    }

    private static void CollectPresetButtons(VisualElement root, List<VisualElement> into)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIButton"
                && PresetButtonAdapter.IsPresetButton(child)
                && ElementBridge.IsVisible(child))
            {
                into.Add(child);
            }
            else
            {
                CollectPresetButtons(child, into);
            }
        }
    }

    private static bool AnyPresetSelected(List<VisualElement> presets)
    {
        foreach (var preset in presets)
        {
            var marker = preset.Q<VisualElement>("active-toggle");
            if (marker != null && marker.visible)
                return true;
        }
        return false;
    }
}

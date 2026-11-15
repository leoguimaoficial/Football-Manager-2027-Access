using System;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The game's own match playback strip (TemplateContainer "MatchPlaybackControls" on the match
/// overlay): the play/pause toggle and the settings button that opens the match preferences
/// card. Shared by the Alt+P / Alt+M companion chords and the sound-cues page's pause/resume.
/// Everything is read live from the buttons' current sprites at call time; nothing is cached.
///
/// Threading: <see cref="Toggle"/> and <see cref="OpenSettings"/> are the chord entry points
/// and marshal themselves onto the main thread (the /matchchord route runs on a socket
/// thread, and touching VisualElements off the main thread hard-crashes IL2CPP natively).
/// <see cref="PauseIfPlaying"/> and <see cref="ResumeIfPaused"/> are main-thread only —
/// their caller (the sound-cues page's activation path) is already dispatched, and a nested
/// Invoke from the main thread can never complete (the drain is the blocked thread itself).
///
/// The playpause SIButton's visible child sprite IS the live playback state (the ButtonAdapter
/// label precedent: a "pause" icon means the match is playing, a "play" icon means it is
/// paused). The ensure-pause/ensure-resume calls act only on an observed state — a blind click
/// could silently flip the match the wrong way — while the player's explicit toggle always
/// clicks (the flip is what they asked for) and only the spoken direction needs the sprite.
/// </summary>
internal static class MatchPlayback
{
    private const string PlayPauseButtonName = "playpause";
    private const string ControlsStripName = "MatchPlaybackControls";
    private const string SettingsName = "settings";

    /// <summary>Presses play/pause only when the match is observably playing. Main thread only.</summary>
    internal static void PauseIfPlaying()
    {
        var (button, playing) = FindPlayPause();
        if (button != null && playing == true) ClickSIButton(button, PlayPauseButtonName);
    }

    /// <summary>Presses play/pause only when the match is observably paused. Main thread only.</summary>
    internal static void ResumeIfPaused()
    {
        var (button, playing) = FindPlayPause();
        if (button != null && playing == false) ClickSIButton(button, PlayPauseButtonName);
    }

    /// <summary>
    /// Alt+P: presses the game's play/pause button and replies with the direction observed at
    /// press time ("pausing" / "resuming"). An unreadable sprite still toggles — the flip is
    /// what the player asked for — but the reply then claims no direction.
    /// </summary>
    internal static string Toggle()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ToggleCore);
        }
        catch (Exception ex)
        {
            Degrade.Note("[MatchPlayback] play pause toggle failed: " + ex.Message);
            return $"play pause {Degrade.Unavailable}";
        }
    }

    private static string ToggleCore()
    {
        var (button, playing) = FindPlayPause();
        if (button == null || !ClickSIButton(button, PlayPauseButtonName))
            return $"play pause button {Degrade.Unavailable}";
        if (playing == true) return "pausing";
        if (playing == false) return "resuming";
        return "play pause pressed";
    }

    /// <summary>
    /// Alt+M: presses the settings button on the playback strip, opening the game's match
    /// preferences card. Empty reply on success — the card's arrival announcement is the
    /// feedback. The button is anchored inside the strip because "settings" alone is too
    /// generic a name to search for across every open panel.
    /// </summary>
    internal static string OpenSettings()
    {
        try
        {
            return MainThreadDispatcher.Invoke(OpenSettingsCore);
        }
        catch (Exception ex)
        {
            Degrade.Note("[MatchPlayback] settings open failed: " + ex.Message);
            return $"match settings {Degrade.Unavailable}";
        }
    }

    private static string OpenSettingsCore()
    {
        VisualElement button = null;
        try
        {
            var strip = FindInOpenPanels(ControlsStripName);
            var settings = strip == null ? null : FindDescendantNamed(strip, SettingsName);
            button = settings == null ? null : FindDescendantSIButton(settings);
        }
        catch (Exception ex)
        {
            Degrade.Note("[MatchPlayback] settings button search failed: " + ex.Message);
            return $"match settings {Degrade.Unavailable}";
        }
        if (button == null)
        {
            Degrade.Note("[MatchPlayback] settings button not found on the playback strip.");
            return $"match settings {Degrade.Unavailable}";
        }
        if (!ClickSIButton(button, SettingsName)) return $"match settings {Degrade.Unavailable}";
        return "";
    }

    internal static (VisualElement Button, bool? Playing) FindPlayPause()
    {
        VisualElement button;
        try
        {
            button = FindInOpenPanels(PlayPauseButtonName);
        }
        catch (Exception ex)
        {
            Degrade.Note("[MatchPlayback] playpause search failed: " + ex.Message);
            return (null, null);
        }
        if (button == null)
        {
            // Not on a match screen (or the template changed). Noted so an agent sees why
            // nothing was clicked.
            Degrade.Note("[MatchPlayback] playpause button not found.");
            return (null, null);
        }

        // The visible child sprite names the available ACTION: pause bars while playing, a play
        // triangle while paused.
        for (int i = 0; i < button.hierarchy.childCount; i++)
        {
            var child = button.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            var sprite = Adapters.SpriteHelper.ReadSpriteName(child);
            if (string.IsNullOrEmpty(sprite)) continue;
            var s = sprite.ToLowerInvariant();
            bool pause = s.Contains("pause");
            bool play = s.Contains("play");
            if (pause && !play) return (button, true);
            if (play && !pause) return (button, false);
        }
        Degrade.Note("[MatchPlayback] playpause state unreadable from its sprite.");
        return (button, null);
    }

    private static bool ClickSIButton(VisualElement button, string what)
    {
        var si = button.TryCast<SI.Bindable.SIButton>();
        if (si == null)
        {
            Degrade.Note($"[MatchPlayback] {what} element is not an SIButton; not clicking.");
            return false;
        }
        si.SimulateButtonClick();
        return true;
    }

    private static VisualElement FindInOpenPanels(string name)
    {
        if (NavigationManager.GetAllOpenPanels() is not { } openPanels) return null;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            var el = FindDescendantNamed(panel, name);
            if (el != null && ElementBridge.IsVisible(el)) return el;
        }
        return null;
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindDescendantNamed(root.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    // The settings button itself is unnamed in the template ("settings" is its SIStyleSetter
    // wrapper), so the click target is the first SIButton in the wrapper's subtree.
    private static VisualElement FindDescendantSIButton(VisualElement root)
    {
        if (root.TryCast<SI.Bindable.SIButton>() != null) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindDescendantSIButton(root.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }
}

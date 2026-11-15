using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The continue button on the HTML surface, mirroring the speech surface's Tab/Shift+Tab:
/// "\" announces the button's current label plus the game date and time shown beside it in
/// the title bar, Alt+Enter presses it. The button lives outside the rendered panel (the
/// title bar, or the match HUD's flow actions), so the page has no element for it and both
/// commands locate it fresh via <see cref="ContinueButtonLocator"/>. Main thread only;
/// reads live, never caches.
/// </summary>
internal static class ContinueSurface
{
    // Same mod-authored phrase the speech surface uses for a missing button.
    private const string NotVisible = "continue button not visible on this screen";

    internal static string Announce()
    {
        try
        {
            // Read-only, like Describe: never closes a dropdown session in flight.
            return MainThreadDispatcher.Invoke(() =>
            {
                var hit = ContinueButtonLocator.Find();
                if (hit == null) return NotVisible;

                var label = DisplayText.Of(hit.Value.Element);
                var dayText = ReadDayWidget((SI.Bindable.Panel)hit.Value.Panel);
                return string.IsNullOrEmpty(dayText) ? label : label + ". " + dayText;
            });
        }
        catch (Exception ex)
        {
            Degrade.Note("[ContinueSurface] announce failed: " + ex.Message);
            return "continue button " + Degrade.Unavailable;
        }
    }

    internal static string Press()
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                DropdownSessionManager.CloseCurrentOnMainThread();
                var hit = ContinueButtonLocator.Find();
                if (hit == null) return NotVisible;
                if (IsDisabled(hit.Value.Element)) return "continue not available";
                hit.Value.Element.Activate();
                return "";
            });
        }
        catch (Exception ex)
        {
            return ServerText.Failure("Continue failed.", ex);
        }
    }

    // The day-widget read for callers outside this class (the arrival report's clock capture
    // and arrival read). Same live read as Announce's.
    internal static string ReadDayWidgetFor(SI.Bindable.Panel panel) => ReadDayWidget(panel);

    // The CurrentDayWidget sits beside the continue button in the title bar: WeekDay ("Fri"),
    // Month (the full date, "4/11/2025", despite the name), Time ("19:30"). The widget being
    // absent or hidden is a normal state (the match HUD has no date), so it contributes
    // nothing; present but unreadable degrades to an honest marker, never silence.
    private static string ReadDayWidget(SI.Bindable.Panel panel)
    {
        var widget = panel.Q<VisualElement>("CurrentDayWidget");
        if (widget == null || !ElementBridge.IsVisible(widget)) return "";
        try
        {
            var parts = new List<string>(3);
            foreach (var name in new[] { "WeekDay", "Month", "Time" })
            {
                var text = widget.Q<SI.Bindable.SIText>(name);
                if (text == null || !ElementBridge.IsVisible(text)) continue;
                var value = TextFilters.CleanForDisplay(text.text);
                if (!string.IsNullOrEmpty(value)) parts.Add(value);
            }
            return string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            Degrade.Note("[ContinueSurface] day widget read failed: " + ex.Message);
            return "date " + Degrade.Unavailable;
        }
    }

    // Mirrors the adapters' disabled signal: the "disabled" token in the comma-joined state.
    private static bool IsDisabled(IAccessibleElement el)
    {
        var state = el.GetState();
        if (string.IsNullOrEmpty(state)) return false;
        foreach (var part in state.Split(new[] { ", " }, StringSplitOptions.None))
            if (part == "disabled") return true;
        return false;
    }
}

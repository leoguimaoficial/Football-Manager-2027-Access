using System;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Go on vacation from the HTML surface: Alt+H in the companion calls the same handler the game's
/// own Shift+Space shortcut runs (<c>GameWorldShortcutsModule.OnGoOnHolidayShortcut</c>), which
/// fires the module's serialized OpenPanelEventContext and opens the Go on vacation card. The
/// reply is empty on success — the card rendering is the feedback — and a speakable phrase when
/// the panel could not open. Main thread only; the module reference is a live Unity object,
/// re-found when lost.
/// </summary>
internal static class HolidaySurface
{
    private static FM.UI.GameWorldShortcutsModule _module;

    private static FM.UI.GameWorldShortcutsModule Module()
    {
        // Unity-overloaded null check: a destroyed module reads as null and is re-found.
        if (_module != null) return _module;

        var gameModules = UnityEngine.Object.FindObjectOfType<GameModules>();
        if (gameModules == null) return null;

        var modules = gameModules.m_modules;
        if (modules == null) return null;
        for (int i = 0; i < modules.Count; i++)
        {
            var typed = modules[i]?.TryCast<FM.UI.GameWorldShortcutsModule>();
            if (typed != null)
            {
                _module = typed;
                return typed;
            }
        }
        return null;
    }

    internal static string Trigger()
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                // The native shortcut is blocked by the game's action map while a sim runs;
                // this direct call would bypass that block, so mirror the gate ourselves.
                if (Patches.ContinueStatePatch.IsProcessing) return "vacation not available while simulating";

                var module = Module();
                if (module == null)
                {
                    Degrade.Note("[HolidaySurface] GameWorldShortcutsModule not found.");
                    return "vacation " + Degrade.Unavailable;
                }

                // The handler ignores its CallbackContext, but the interop layer rejects a
                // null wrapper, so pass a freshly constructed empty one.
                module.OnGoOnHolidayShortcut(new UnityEngine.InputSystem.InputAction.CallbackContext());
                return "";
            });
        }
        catch (Exception ex)
        {
            Degrade.Note("[HolidaySurface] vacation trigger failed: " + ex.Message);
            return "vacation " + Degrade.Unavailable;
        }
    }
}

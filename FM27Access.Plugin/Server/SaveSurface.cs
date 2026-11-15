using System;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Quick save on the HTML surface: Ctrl+S in the companion calls the same handler the game's
/// own Ctrl+S shortcut runs (<c>SaveGameHandlerModule.OnSaveGameInput</c>), so the flow is
/// identical — overwrite the current career file, or the game's own no-file handling when
/// there is none. The trigger phrase is the only mod speech; everything after is the game's
/// own saving flow (progress panel, overwrite backup). Main thread only; the module reference
/// is a live Unity object, re-found when lost.
/// </summary>
internal static class SaveSurface
{
    private static FM.UI.SaveGameHandlerModule _module;

    private static FM.UI.SaveGameHandlerModule Module()
    {
        // Unity-overloaded null check: a destroyed module reads as null and is re-found.
        if (_module != null) return _module;

        var gameModules = UnityEngine.Object.FindObjectOfType<GameModules>();
        if (gameModules == null) return null;

        var modules = gameModules.m_modules;
        if (modules == null) return null;
        for (int i = 0; i < modules.Count; i++)
        {
            var typed = modules[i]?.TryCast<FM.UI.SaveGameHandlerModule>();
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
                if (Patches.ContinueStatePatch.IsProcessing) return "saving not available while simulating";

                var module = Module();
                if (module == null)
                {
                    Degrade.Note("[SaveSurface] SaveGameHandlerModule not found.");
                    return "save " + Degrade.Unavailable;
                }
                // The game's own gate for this handler (bound from the CanSave binding).
                if (!module.m_canSave) return "saving not available right now";

                // The handler ignores its CallbackContext, but the interop layer rejects a
                // null wrapper, so pass a freshly constructed empty one.
                module.OnSaveGameInput(new UnityEngine.InputSystem.InputAction.CallbackContext());
                return "Saving";
            });
        }
        catch (Exception ex)
        {
            Degrade.Note("[SaveSurface] save trigger failed: " + ex.Message);
            return "save " + Degrade.Unavailable;
        }
    }
}

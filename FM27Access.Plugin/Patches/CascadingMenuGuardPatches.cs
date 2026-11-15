using System;
using HarmonyLib;

namespace FM27Access.Plugin.Patches;

/// <summary>
/// Crash guards for the game's own context-menu race. When a menu's items arrive asynchronously
/// AFTER its context has been dropped (a table scroll recycled the anchor row, or a close raced
/// the fill), the game's entry-added callback still fires ShowInternal → SetCurrentFolder on the
/// dead menu and throws NullReferenceException — which propagates uncaught through the game
/// plugin's data tick and HARD-CRASHES the whole game (two live Backtrace dumps on 2026-07-02,
/// plus one reproduced under stress cycling; CascadingMenu.cs:876 in every stack).
///
/// The finalizers convert that crash into a non-event: the show silently fails, the menu simply
/// does not appear, and the mod's open confirm already reports the honest "Context menu did not
/// open." A suppressed show is degrade-in-place (one menu lost); the unguarded alternative is the
/// entire game down.
/// </summary>
[HarmonyPatch(typeof(SI.UI.CascadingMenu), "SetCurrentFolder")]
public static class CascadingMenuSetCurrentFolderGuard
{
    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            Speech.Degrade.Note(
                $"game CascadingMenu.SetCurrentFolder threw {__exception.GetType().Name} (menu torn down mid-fill); show suppressed");
        }
        catch { /* the guard must never introduce its own failure */ }
        return null;   // swallow: a lost menu show, not a lost game
    }
}

/// <summary>Same guard one frame up the crash path — see <see cref="CascadingMenuSetCurrentFolderGuard"/>.</summary>
[HarmonyPatch(typeof(SI.UI.CascadingMenuPopup), "ShowInternal")]
public static class CascadingMenuPopupShowInternalGuard
{
    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            Speech.Degrade.Note(
                $"game CascadingMenuPopup.ShowInternal threw {__exception.GetType().Name} (menu torn down mid-fill); show suppressed");
        }
        catch { /* the guard must never introduce its own failure */ }
        return null;
    }
}

/// <summary>
/// The decisive guard: the TOP of the managed late-fill path. OnItemsReceived registers each
/// arriving menu item (RegisterItem → AddItem/AddFolder → the entry-added show callback); on a
/// dead description ANY of those branches can null-ref, and the two guards above only cover the
/// show branch — a live session (2026-07-02 17:57) crashed through RegisterItem directly, with
/// the exception escaping into the game plugin's native data transaction and wedging the main
/// loop. Swallowing here drops a dead menu's late items entirely — the menu simply never shows,
/// and the mod's open confirm reports the honest "Context menu did not open."
/// </summary>
[HarmonyPatch(typeof(FM.Game.PluginContextMenuContributor), "OnItemsReceived")]
public static class ContextMenuItemsReceivedGuard
{
    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            Speech.Degrade.Note(
                $"game PluginContextMenuContributor.OnItemsReceived threw {__exception.GetType().Name} (menu torn down mid-fill); items dropped");
        }
        catch { /* the guard must never introduce its own failure */ }
        return null;
    }
}

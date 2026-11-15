using FM.UI;
using FM27Access.Navigation;
using FM27Access.Plugin.Patches;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Drives FM26's OWN navigation (not the mod cursor) through the game's <see cref="FMNavigationManager"/>,
/// the same component the game's keyboard/gamepad input maps drive. Verified live via /eval: the menu's
/// focus is moved with <c>GetNextNavigatableElement</c> + <c>UpdateFocus</c>, not by the UI Toolkit
/// <c>NavigationMoveEvent</c> (which the mod swallows at every panel root, and which the game does not use
/// for focus movement anyway). When nothing is focused yet (e.g. a freshly loaded screen), the first
/// directional verb establishes focus on the panel's first focusable element rather than moving.
///
/// This is the ergonomic shortcut for poking the raw game; /eval remains the universal fallback for
/// anything a verb does not cover. All verbs run on the main thread via DevEndpoints' Invoke.
/// </summary>
internal static class GameDriver
{
    private static FMNavigationManager _nav;

    private static FMNavigationManager Nav()
    {
        if (_nav == null) _nav = UnityEngine.Object.FindObjectOfType<FMNavigationManager>();
        return _nav;
    }

    public static string Drive(string verb)
    {
        switch ((verb ?? "").Trim().ToLowerInvariant())
        {
            case "up": return Move(MoveDirection.Up, "up");
            case "down": return Move(MoveDirection.Down, "down");
            case "left": return Move(MoveDirection.Left, "left");
            case "right": return Move(MoveDirection.Right, "right");
            case "confirm":
            case "enter":
            case "submit":
                return Submit();
            case "activate":
            case "click":
                return Activate();
            case "cancel":
            case "back":
            case "escape":
                return Cancel();
            default:
                return $"[unknown verb] '{verb}' - up|down|left|right|confirm|cancel|activate\n";
        }
    }

    private static string Move(MoveDirection dir, string name)
    {
        var nm = Nav();
        if (nm == null) return "no FMNavigationManager\n";

        var focus=SI.Core.GameSubsystems.Get<SI.UI.FocusSubsystem>();
        var cur = focus.CurrentFocus;
        if (cur == null)
        {
            // Nothing focused yet: the first directional verb establishes focus, like a game's first
            // dpad press, rather than moving from nowhere.
            var first = FirstFocusable();
            if (first == null) return $"{name}: nothing focusable on this screen\n";
            if(!focus.TryFocusElement(first))return $"{name}: native focus rejected {NameOf(first)}\n";
            return $"{name} -> focus established on {NameOf(first)}\n";
        }

        var vector=dir switch{MoveDirection.Up=>Vector2.up,MoveDirection.Down=>Vector2.down,MoveDirection.Left=>Vector2.left,_=>Vector2.right};
        focus.ResolveDirection(vector,cur.worldBound.center,true,cur,out var next);
        if (next == null) return $"{name}: no element in that direction (focus: {NameOf(cur)})\n";
        if(!focus.TryFocusElement(next))return $"{name}: native focus rejected {NameOf(next)}\n";
        return $"{name} -> {NameOf(next)}\n";
    }

    private static string Submit()
    {
        var target = CurrentOrFirst();
        if (target == null) return "confirm: nothing focusable on this screen\n";
        target.SendEvent(new NavigationSubmitEvent());
        return $"confirm -> NavigationSubmitEvent on {NameOf(target)}\n";
    }

    private static string Activate()
    {
        var target = CurrentOrFirst();
        if (target == null) return "activate: nothing focusable on this screen\n";
        if (target.GetIl2CppType().Name == "SIButton")
        {
            target.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
            return $"activate -> SimulateButtonClick on {NameOf(target)}\n";
        }
        target.SendEvent(new NavigationSubmitEvent());
        return $"activate -> NavigationSubmitEvent on {NameOf(target)} (not an SIButton)\n";
    }

    private static string Cancel()
    {
        var target = CurrentOrFirst();
        if (target == null) return "cancel: nothing focusable on this screen\n";
        // NavigationCancelEvent is swallowed by the mod's panel-root blocker; let this one through.
        NavigationEventBlocker.AllowNext = true;
        try { target.SendEvent(new NavigationCancelEvent()); }
        finally { NavigationEventBlocker.AllowNext = false; }
        return $"cancel -> NavigationCancelEvent on {NameOf(target)}\n";
    }

    // The game's current focus, or the panel's first focusable element if nothing is focused yet
    // (focusing it so confirm/activate/cancel act on a real element).
    private static VisualElement CurrentOrFirst()
    {
        var nm = Nav();
        var focus=SI.Core.GameSubsystems.Get<SI.UI.FocusSubsystem>();
        var cur = focus?.CurrentFocus;
        if (cur != null) return cur;
        var first = FirstFocusable();
        if (first != null && focus != null&&!focus.TryFocusElement(first))return null;
        return first;
    }

    // Pre-order walk of the active panel for the first focusable, visible element — what the game
    // treats as a focus target (plain layout wrappers are focusable=false).
    private static VisualElement FirstFocusable()
    {
        if (NavigationManager.GetActivePanel() is not VisualElement panel) return null;
        var stack = new System.Collections.Generic.Stack<VisualElement>();
        stack.Push(panel);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e.focusable && e.visible) return e;
            for (int i = e.hierarchy.childCount - 1; i >= 0; i--)
                stack.Push(e.hierarchy[i]);
        }
        return null;
    }

    private static string NameOf(VisualElement e)
        => e == null ? "?" : (string.IsNullOrEmpty(e.name) ? e.GetIl2CppType().Name : e.name);
}

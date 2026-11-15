using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input;

/// <summary>
/// Polls Keyboard.current each frame and dispatches key events to the
/// handler stack. Re-asserts InputAction blocking via the coordinator
/// at the start of each frame to counter the game re-enabling actions.
/// </summary>
public class KeyboardPoller
{
    private readonly HandlerStack _handlerStack;
    private readonly InputBlockingCoordinator _coordinator;

    // All keys the mod might ever poll, in the order we check them.
    private static readonly KeyboardKey[] PollKeys = new[]
    {
        // Navigation
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.LeftArrow, KeyboardKey.RightArrow,
        KeyboardKey.Enter, KeyboardKey.Escape, KeyboardKey.Space,
        KeyboardKey.Tab, KeyboardKey.Home, KeyboardKey.End,
        KeyboardKey.PageUp, KeyboardKey.PageDown,
        KeyboardKey.Backspace, KeyboardKey.Delete,

        // Function keys (debug)
        KeyboardKey.F8, KeyboardKey.F9,
        KeyboardKey.F10, KeyboardKey.F11, KeyboardKey.F12,

        // Letters
        KeyboardKey.A, KeyboardKey.B, KeyboardKey.C, KeyboardKey.D,
        KeyboardKey.E, KeyboardKey.F, KeyboardKey.G, KeyboardKey.H,
        KeyboardKey.I, KeyboardKey.J, KeyboardKey.K, KeyboardKey.L,
        KeyboardKey.M, KeyboardKey.N, KeyboardKey.O, KeyboardKey.P,
        KeyboardKey.Q, KeyboardKey.R, KeyboardKey.S, KeyboardKey.T,
        KeyboardKey.U, KeyboardKey.V, KeyboardKey.W, KeyboardKey.X,
        KeyboardKey.Y, KeyboardKey.Z,

        // Digits
        KeyboardKey.Digit0, KeyboardKey.Digit1, KeyboardKey.Digit2,
        KeyboardKey.Digit3, KeyboardKey.Digit4, KeyboardKey.Digit5,
        KeyboardKey.Digit6, KeyboardKey.Digit7, KeyboardKey.Digit8,
        KeyboardKey.Digit9,
    };

    public KeyboardPoller(HandlerStack handlerStack, InputBlockingCoordinator coordinator)
    {
        _handlerStack = handlerStack;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Called each frame from AccessibilityBehaviour.Update().
    /// Re-asserts input blocking, then polls keyboard state and dispatches
    /// to the handler stack.
    /// </summary>
    public void Poll()
    {
        _coordinator.ReAssertBlocking();

        var kb = Keyboard.current;
        if (kb == null) return;

        // Tick walk: top-to-bottom, stop at barrier or consumption
        int count = _handlerStack.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            var handler = _handlerStack.GetAt(i);
            if (handler == null) break;
            if (handler.Tick()) break;
            if (_handlerStack.Count != count || _handlerStack.GetAt(i) != handler) break;
            if (handler.CapturesAllInput) break;
        }

        // Key dispatch: find pressed key, walk stack with HandleKeyDown
        var modifiers = ReadModifiers(kb);
        foreach (var key in PollKeys)
        {
            if (!WasPressedThisFrame(kb, key))
                continue;

            count = _handlerStack.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                var handler = _handlerStack.GetAt(i);
                if (handler == null) break;
                if (handler.HandleKeyDown(key, modifiers)) break;
                if (_handlerStack.Count != count || _handlerStack.GetAt(i) != handler) break;
                if (handler.CapturesAllInput) break;
            }
            break; // Only process the first pressed key per frame
        }
    }

    private static KeyModifiers ReadModifiers(Keyboard kb)
    {
        var mods = KeyModifiers.None;
        if (kb.shiftKey.isPressed) mods |= KeyModifiers.Shift;
        if (kb.ctrlKey.isPressed) mods |= KeyModifiers.Ctrl;
        if (kb.altKey.isPressed) mods |= KeyModifiers.Alt;
        return mods;
    }

    private static bool WasPressedThisFrame(Keyboard kb, KeyboardKey key)
    {
        var control = MapToControl(kb, key);
        return control != null && control.wasPressedThisFrame;
    }

    private static KeyControl MapToControl(Keyboard kb, KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.UpArrow => kb.upArrowKey,
            KeyboardKey.DownArrow => kb.downArrowKey,
            KeyboardKey.LeftArrow => kb.leftArrowKey,
            KeyboardKey.RightArrow => kb.rightArrowKey,
            KeyboardKey.Enter => kb.enterKey,
            KeyboardKey.Escape => kb.escapeKey,
            KeyboardKey.Space => kb.spaceKey,
            KeyboardKey.Tab => kb.tabKey,
            KeyboardKey.Home => kb.homeKey,
            KeyboardKey.End => kb.endKey,
            KeyboardKey.PageUp => kb.pageUpKey,
            KeyboardKey.PageDown => kb.pageDownKey,
            KeyboardKey.Backspace => kb.backspaceKey,
            KeyboardKey.Delete => kb.deleteKey,
            KeyboardKey.F8 => kb.f8Key,
            KeyboardKey.F9 => kb.f9Key,
            KeyboardKey.F10 => kb.f10Key,
            KeyboardKey.F11 => kb.f11Key,
            KeyboardKey.F12 => kb.f12Key,
            KeyboardKey.A => kb.aKey,
            KeyboardKey.B => kb.bKey,
            KeyboardKey.C => kb.cKey,
            KeyboardKey.D => kb.dKey,
            KeyboardKey.E => kb.eKey,
            KeyboardKey.F => kb.fKey,
            KeyboardKey.G => kb.gKey,
            KeyboardKey.H => kb.hKey,
            KeyboardKey.I => kb.iKey,
            KeyboardKey.J => kb.jKey,
            KeyboardKey.K => kb.kKey,
            KeyboardKey.L => kb.lKey,
            KeyboardKey.M => kb.mKey,
            KeyboardKey.N => kb.nKey,
            KeyboardKey.O => kb.oKey,
            KeyboardKey.P => kb.pKey,
            KeyboardKey.Q => kb.qKey,
            KeyboardKey.R => kb.rKey,
            KeyboardKey.S => kb.sKey,
            KeyboardKey.T => kb.tKey,
            KeyboardKey.U => kb.uKey,
            KeyboardKey.V => kb.vKey,
            KeyboardKey.W => kb.wKey,
            KeyboardKey.X => kb.xKey,
            KeyboardKey.Y => kb.yKey,
            KeyboardKey.Z => kb.zKey,
            KeyboardKey.Digit0 => kb.digit0Key,
            KeyboardKey.Digit1 => kb.digit1Key,
            KeyboardKey.Digit2 => kb.digit2Key,
            KeyboardKey.Digit3 => kb.digit3Key,
            KeyboardKey.Digit4 => kb.digit4Key,
            KeyboardKey.Digit5 => kb.digit5Key,
            KeyboardKey.Digit6 => kb.digit6Key,
            KeyboardKey.Digit7 => kb.digit7Key,
            KeyboardKey.Digit8 => kb.digit8Key,
            KeyboardKey.Digit9 => kb.digit9Key,
            _ => null,
        };
    }
}

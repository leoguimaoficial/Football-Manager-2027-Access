using UnityEngine.InputSystem;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input;

/// <summary>
/// At startup, scans all InputActionMaps in the game's InputActionAsset
/// and builds a reverse lookup: KeyboardKey -> List of InputActions bound to it.
///
/// Handlers declare physical keys (KeyboardKey enum). This registry bridges
/// those to the real InputActions that need to be disabled.
/// </summary>
public class BindingRegistry
{
    private readonly Dictionary<KeyboardKey, List<InputAction>> _keyToActions = new();
    private readonly List<InputAction> _allKeyboardActions = new();

    /// <summary>
    /// Scan all maps in the asset and build the reverse lookup.
    /// </summary>
    public void Build(InputActionAsset asset)
    {
        foreach (var map in asset.actionMaps)
        {
            foreach (var action in map.actions)
            {
                bool hasKeyboardBinding = false;
                var bindings = action.bindings;

                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    if (binding.isComposite)
                        continue;

                    var path = binding.effectivePath;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    // Only care about keyboard bindings
                    if (!path.StartsWith("<Keyboard>/"))
                        continue;

                    hasKeyboardBinding = true;
                    var keyPath = path.Substring("<Keyboard>/".Length);
                    var key = ParseKeyPath(keyPath);

                    if (key != KeyboardKey.None)
                    {
                        if (!_keyToActions.TryGetValue(key, out var list))
                        {
                            list = new List<InputAction>();
                            _keyToActions[key] = list;
                        }

                        if (!list.Contains(action))
                            list.Add(action);
                    }
                }

                if (hasKeyboardBinding && !_allKeyboardActions.Contains(action))
                    _allKeyboardActions.Add(action);
            }
        }

        Speech.Log.Info($"[BindingRegistry] Built: {_keyToActions.Count} keys mapped, {_allKeyboardActions.Count} keyboard-bound actions total.");
    }

    public IReadOnlyList<InputAction> GetActionsForKey(KeyboardKey key)
    {
        return _keyToActions.TryGetValue(key, out var list)
            ? list
            : Array.Empty<InputAction>();
    }

    public IReadOnlyList<InputAction> AllKeyboardActions => _allKeyboardActions;

    private static KeyboardKey ParseKeyPath(string keyPath)
    {
        // Handle modifier-prefixed paths like "shift+q" — extract the base key
        int plusIndex = keyPath.LastIndexOf('+');
        if (plusIndex >= 0)
            keyPath = keyPath.Substring(plusIndex + 1);

        return keyPath.ToLowerInvariant() switch
        {
            "uparrow" => KeyboardKey.UpArrow,
            "downarrow" => KeyboardKey.DownArrow,
            "leftarrow" => KeyboardKey.LeftArrow,
            "rightarrow" => KeyboardKey.RightArrow,
            "enter" => KeyboardKey.Enter,
            "numpadenter" => KeyboardKey.Enter,
            "escape" => KeyboardKey.Escape,
            "space" => KeyboardKey.Space,
            "tab" => KeyboardKey.Tab,
            "home" => KeyboardKey.Home,
            "end" => KeyboardKey.End,
            "pageup" => KeyboardKey.PageUp,
            "pagedown" => KeyboardKey.PageDown,
            "backspace" => KeyboardKey.Backspace,
            "delete" => KeyboardKey.Delete,
            "f8" => KeyboardKey.F8,
            "f9" => KeyboardKey.F9,
            "f10" => KeyboardKey.F10,
            "f11" => KeyboardKey.F11,
            "f12" => KeyboardKey.F12,
            "a" => KeyboardKey.A,
            "b" => KeyboardKey.B,
            "c" => KeyboardKey.C,
            "d" => KeyboardKey.D,
            "e" => KeyboardKey.E,
            "f" => KeyboardKey.F,
            "g" => KeyboardKey.G,
            "h" => KeyboardKey.H,
            "i" => KeyboardKey.I,
            "j" => KeyboardKey.J,
            "k" => KeyboardKey.K,
            "l" => KeyboardKey.L,
            "m" => KeyboardKey.M,
            "n" => KeyboardKey.N,
            "o" => KeyboardKey.O,
            "p" => KeyboardKey.P,
            "q" => KeyboardKey.Q,
            "r" => KeyboardKey.R,
            "s" => KeyboardKey.S,
            "t" => KeyboardKey.T,
            "u" => KeyboardKey.U,
            "v" => KeyboardKey.V,
            "w" => KeyboardKey.W,
            "x" => KeyboardKey.X,
            "y" => KeyboardKey.Y,
            "z" => KeyboardKey.Z,
            "1" => KeyboardKey.Digit1,
            "2" => KeyboardKey.Digit2,
            "3" => KeyboardKey.Digit3,
            "4" => KeyboardKey.Digit4,
            "5" => KeyboardKey.Digit5,
            "6" => KeyboardKey.Digit6,
            "7" => KeyboardKey.Digit7,
            "8" => KeyboardKey.Digit8,
            "9" => KeyboardKey.Digit9,
            "0" => KeyboardKey.Digit0,
            "numpad0" => KeyboardKey.Numpad0,
            "numpad1" => KeyboardKey.Numpad1,
            "numpad2" => KeyboardKey.Numpad2,
            "numpad3" => KeyboardKey.Numpad3,
            "numpad4" => KeyboardKey.Numpad4,
            "numpad5" => KeyboardKey.Numpad5,
            "numpad6" => KeyboardKey.Numpad6,
            "numpad7" => KeyboardKey.Numpad7,
            "numpad8" => KeyboardKey.Numpad8,
            "numpad9" => KeyboardKey.Numpad9,
            "leftshift" or "leftShift" => KeyboardKey.LeftShift,
            "rightshift" or "rightShift" => KeyboardKey.RightShift,
            "leftctrl" or "leftCtrl" => KeyboardKey.LeftCtrl,
            "rightctrl" or "rightCtrl" => KeyboardKey.RightCtrl,
            "leftalt" or "leftAlt" => KeyboardKey.LeftAlt,
            "rightalt" or "rightAlt" => KeyboardKey.RightAlt,
            "comma" => KeyboardKey.Comma,
            "period" => KeyboardKey.Period,
            "slash" => KeyboardKey.Slash,
            "semicolon" => KeyboardKey.Semicolon,
            // Platform-specific usage paths (e.g., */{Submit}) — not keyboard keys
            _ => KeyboardKey.None,
        };
    }
}

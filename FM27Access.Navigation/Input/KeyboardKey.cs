namespace FM27Access.Navigation.Input;

/// <summary>
/// Physical keyboard keys the mod may claim or poll.
/// Maps 1:1 to Unity InputSystem Key enum values used by Keyboard.current.
/// </summary>
public enum KeyboardKey
{
    None = 0,

    // Arrow keys
    UpArrow,
    DownArrow,
    LeftArrow,
    RightArrow,

    // Navigation
    Enter,
    Escape,
    Space,
    Tab,
    Home,
    End,
    PageUp,
    PageDown,
    Backspace,
    Delete,

    // Function keys (debug tools)
    F8,
    F9,
    F10,
    F11,
    F12,

    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Digits
    Digit0, Digit1, Digit2, Digit3, Digit4,
    Digit5, Digit6, Digit7, Digit8, Digit9,

    // Numpad
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
    Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,

    // Modifiers (for binding lookup, not typically claimed alone)
    LeftShift, RightShift,
    LeftCtrl, RightCtrl,
    LeftAlt, RightAlt,

    // Punctuation used by game bindings
    Comma, Period, Slash, Semicolon,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

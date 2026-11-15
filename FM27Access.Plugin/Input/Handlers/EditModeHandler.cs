using FM27Access.Navigation;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Active while a text/number field is being edited.
/// CapturesAllInput=true so all keyboard input goes to the game's text field
/// except Enter, which exits edit mode.
/// </summary>
internal class EditModeHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly IAccessibleElement _element;

    private static readonly KeyboardKey[] _claimedKeys = new[] { KeyboardKey.Enter };

    public EditModeHandler(HandlerStack stack, IAccessibleElement element)
    {
        _stack = stack;
        _element = element;
    }

    public string DisplayName => "Edit";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        if (key == KeyboardKey.Enter)
        {
            Speech.Log.Info("[Input] Exiting edit mode.");
            if (_element is Adapters.TextFieldAdapter tfa)
                tfa.Deactivate();
            var announcement = _element.GetAnnouncement();
            _stack.Pop();
            Speech.SpeechPipeline.SpeakInterrupt(announcement);
            return true;
        }
        return false;
    }

    public bool Tick() => false;

    public void OnActivate()
    {
        Speech.Log.Info($"[Input] Entering edit mode: {_element.Role}");
        _element.Activate();
        Speech.SpeechPipeline.SpeakInterrupt("editing. enter to confirm.");
    }

    public void OnDeactivate() { }
}

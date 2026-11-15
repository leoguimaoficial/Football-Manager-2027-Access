using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Active while a SearchBox is being used. CapturesAllInput=true so typing
/// goes to the game's search field. Enter and Escape are intercepted:
/// Escape exits the search, Enter submits.
/// </summary>
internal class SearchBoxHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.SearchBoxAdapter _adapter;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.Enter, KeyboardKey.Escape,
    };

    public SearchBoxHandler(HandlerStack stack, Adapters.SearchBoxAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
    }

    public string DisplayName => "Search";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case KeyboardKey.Escape:
                Speech.Log.Info("[SearchBox] Exiting search.");
                _adapter.Deactivate();
                var announcement = _adapter.GetAnnouncement();
                _stack.Pop();
                Speech.SpeechPipeline.SpeakInterrupt(announcement);
                return true;

            case KeyboardKey.Enter:
                Speech.Log.Info("[SearchBox] Submitting search.");
                var text = _adapter.ReadCurrentText();
                _adapter.Deactivate();
                _stack.Pop();
                Speech.SpeechPipeline.SpeakInterrupt(
                    string.IsNullOrEmpty(text) ? "search closed" : $"searching {text}");
                return true;

            default:
                return false;
        }
    }

    public bool Tick() => false;

    public void OnActivate()
    {
        Speech.Log.Info("[Input] Entering search box.");
        _adapter.Activate();
        Speech.SpeechPipeline.SpeakInterrupt("editing search. type to search, enter to submit, escape to cancel.");
    }

    public void OnDeactivate() { }
}

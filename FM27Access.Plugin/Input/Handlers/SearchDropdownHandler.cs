using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Manages searchable dropdown interaction: type to filter, down arrow
/// to browse results, up/down to navigate, Enter to confirm, Escape to
/// go back to search or cancel.
/// </summary>
internal class SearchDropdownHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.SearchableDropdownAdapter _adapter;
    private int _waitFrames;
    private bool _waitingForPopup = true;
    private bool _browsing;
    private List<string> _choices;
    private int _cursor;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.Enter, KeyboardKey.Escape,
    };

    public SearchDropdownHandler(HandlerStack stack, Adapters.SearchableDropdownAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
    }

    public string DisplayName => "Search Dropdown";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        if (_waitingForPopup) return true;

        if (!_browsing)
            return HandleTypingMode(key);

        return HandleBrowsingMode(key);
    }

    public bool Tick()
    {
        if (!_waitingForPopup) return false;

        _waitFrames++;
        if (_adapter.IsPopupVisible())
        {
            _adapter.FocusSearchField();
            _waitingForPopup = false;
            int totalChoices = _adapter.GetChoicesCount();
            Speech.SpeechPipeline.SpeakInterrupt($"search {totalChoices} options. type to filter, press down arrow to browse results, escape to cancel.");
            Speech.Log.Info($"[SearchDropdown] Popup open. {totalChoices} total choices.");
        }
        else if (_waitFrames > 30)
        {
            Speech.Log.Warn("[SearchDropdown] Popup did not appear after 30 frames.");
            Speech.SpeechPipeline.SpeakInterrupt("dropdown failed to open");
            _stack.Pop();
        }
        return true;
    }

    public void OnActivate()
    {
        Speech.Log.Info("[Input] Opening searchable dropdown popup.");
        _adapter.ShowPopup();
        _waitFrames = 0;
    }

    public void OnDeactivate() { }

    private bool HandleTypingMode(KeyboardKey key)
    {
        switch (key)
        {
            case KeyboardKey.Escape:
                Speech.Log.Info("[SearchDropdown] Cancelled during typing.");
                _adapter.BlurSearchField();
                _adapter.HideDropdown();
                Speech.SpeechPipeline.SpeakInterrupt("cancelled");
                _stack.Pop();
                return true;

            case KeyboardKey.DownArrow:
                _adapter.BlurSearchField();
                var searchText = _adapter.GetSearchText();
                _choices = _adapter.ReadFilteredChoices();
                if (_choices.Count == 0)
                {
                    Speech.SpeechPipeline.SpeakInterrupt($"no results for \"{searchText}\". continue typing or escape to cancel.");
                    _adapter.FocusSearchField();
                    Speech.Log.Info($"[SearchDropdown] No results for \"{searchText}\".");
                    return true;
                }
                _cursor = 0;
                _browsing = true;
                Speech.SpeechPipeline.SpeakInterrupt($"{_choices.Count} results. {_choices[0]}. up and down to browse, enter to confirm, escape to search again.");
                Speech.Log.Info($"[SearchDropdown] Browsing {_choices.Count} results for \"{searchText}\".");
                return true;

            default:
                return false;
        }
    }

    private bool HandleBrowsingMode(KeyboardKey key)
    {
        switch (key)
        {
            case KeyboardKey.Escape:
                _adapter.FocusSearchField();
                _browsing = false;
                Speech.SpeechPipeline.SpeakInterrupt("back to search. type to filter.");
                Speech.Log.Info("[SearchDropdown] Returned to typing mode.");
                return true;

            case KeyboardKey.DownArrow:
                if (_cursor < _choices.Count - 1)
                    _cursor++;
                Speech.SpeechPipeline.SpeakInterrupt(_choices[_cursor]);
                return true;

            case KeyboardKey.UpArrow:
                if (_cursor > 0)
                    _cursor--;
                Speech.SpeechPipeline.SpeakInterrupt(_choices[_cursor]);
                return true;

            case KeyboardKey.Enter:
                string selected = _choices[_cursor];
                Speech.Log.Info($"[SearchDropdown] Confirmed: index {_cursor}, \"{selected}\"");
                _adapter.ConfirmSelection(_cursor);
                Speech.SpeechPipeline.SpeakInterrupt(selected);
                _stack.Pop();
                return true;

            default:
                return true;
        }
    }
}

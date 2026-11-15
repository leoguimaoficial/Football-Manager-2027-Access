using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

internal class ComboboxHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.ComboboxAdapter _adapter;
    private bool _waitingForValueChange;
    private int _waitFrames;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.Enter, KeyboardKey.Escape,
        KeyboardKey.RightArrow,
    };

    public ComboboxHandler(HandlerStack stack, Adapters.ComboboxAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
    }

    public string DisplayName => "Combobox";
    public bool CapturesAllInput => false;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case KeyboardKey.UpArrow:
                if (!_adapter.IsIncrementEnabled())
                {
                    Speech.SpeechPipeline.SpeakInterrupt($"{_adapter.GetCurrentValue()}, maximum");
                    return true;
                }
                _adapter.Increment();
                _waitingForValueChange = true;
                _waitFrames = 0;
                return true;

            case KeyboardKey.DownArrow:
                if (!_adapter.IsDecrementEnabled())
                {
                    Speech.SpeechPipeline.SpeakInterrupt($"{_adapter.GetCurrentValue()}, minimum");
                    return true;
                }
                _adapter.Decrement();
                _waitingForValueChange = true;
                _waitFrames = 0;
                return true;

            case KeyboardKey.Enter:
                _stack.Pop();
                Speech.SpeechPipeline.SpeakInterrupt($"{_adapter.GetCurrentValue()}, submitted");
                return true;

            case KeyboardKey.Escape:
                _stack.Pop();
                Speech.SpeechPipeline.SpeakInterrupt(_adapter.GetCurrentValue());
                return true;

            case KeyboardKey.RightArrow:
                if (_adapter.HasDropdownChoices())
                {
                    var dropdownAdapter = _adapter.GetInternalDropdownAdapter();
                    _stack.Push(new DropdownHandler(_stack, dropdownAdapter));
                }
                return true;

            default:
                return true;
        }
    }

    public bool Tick()
    {
        if (_waitingForValueChange)
        {
            _waitFrames++;
            if (_waitFrames >= 3)
            {
                _waitingForValueChange = false;
                var newValue = _adapter.GetCurrentValue();
                Speech.SpeechPipeline.SpeakInterrupt(newValue);
            }
            return true;
        }
        return false;
    }

    public void OnActivate()
    {
        var value = _adapter.GetCurrentValue();
        var instructions = "up down to adjust, enter to confirm, escape to cancel";
        if (_adapter.HasDropdownChoices())
            instructions += ", right for options";
        Speech.SpeechPipeline.SpeakInterrupt($"{value}. {instructions}.");
    }

    public void OnDeactivate() { }
}

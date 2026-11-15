using FM27Access.Navigation.Input;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Input.Handlers;

internal class TacticsPlayerHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.TacticsPlayerAdapter _adapter;
    private int _cursorIndex;

    private static readonly string[] Options = { "Change role", "Player instructions", "Change pitch position" };

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.Enter, KeyboardKey.Escape,
    };

    public TacticsPlayerHandler(HandlerStack stack, Adapters.TacticsPlayerAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
    }

    public string DisplayName => "TacticsPlayer";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case KeyboardKey.DownArrow:
                _cursorIndex = (_cursorIndex + 1) % Options.Length;
                Speech.SpeechPipeline.SpeakInterrupt(Options[_cursorIndex]);
                return true;

            case KeyboardKey.UpArrow:
                _cursorIndex = (_cursorIndex - 1 + Options.Length) % Options.Length;
                Speech.SpeechPipeline.SpeakInterrupt(Options[_cursorIndex]);
                return true;

            case KeyboardKey.Enter:
                ExecuteOption();
                return true;

            case KeyboardKey.Escape:
                Speech.SpeechPipeline.SpeakInterrupt("cancelled");
                _stack.Pop();
                return true;

            default:
                return true;
        }
    }

    public bool Tick() => false;

    public void OnActivate()
    {
        var label = _adapter.GetLabel();
        var optionList = string.Join(", ", Options);
        Speech.SpeechPipeline.SpeakInterrupt($"{label}. {optionList}. Up and down to choose, enter to confirm.");
    }

    public void OnDeactivate() { }

    private static VisualElement FindTacticsPanelFromPanelManager()
    {
        if (!SI.Bindable.PanelManager.Exists()) return null;
        var pm = SI.Bindable.PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);
        for (int i = 0; i < layers.Count; i++)
        {
            var openPanels = layers[i].Open;
            if (openPanels == null) continue;
            for (int p = 0; p < openPanels.Count; p++)
            {
                var panel = openPanels[p];
                if (panel == null) continue;
                var found = FindDescendantByName(panel, "interactable-pitch-expanded");
                if (found != null) return found;
            }
        }
        return null;
    }

    private static VisualElement FindDescendantByName(VisualElement el, string name)
    {
        if (el.name == name)
            return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendantByName(el.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    private void ExecuteOption()
    {
        switch (_cursorIndex)
        {
            case 0: // Change role
                if (_adapter.FindRoleButton() == null)
                {
                    Speech.SpeechPipeline.SpeakInterrupt("role button not found");
                    _stack.Pop();
                    return;
                }
                Speech.Log.Info("[TacticsPlayer] Opening role dropdown.");
                _stack.Pop();
                _adapter.ActivateRoleButton();
                break;

            case 1: // Player instructions
                Speech.Log.Info("[TacticsPlayer] Opening player instructions.");
                _stack.Pop();
                _adapter.ActivateShirtButton();
                break;

            case 2: // Change pitch position
                var indicator = _adapter.FindAncestorIndicator();
                if (indicator == null)
                {
                    Speech.SpeechPipeline.SpeakInterrupt("pitch indicator not found");
                    _stack.Pop();
                    return;
                }
                var tacticsPanel = FindTacticsPanelFromPanelManager();
                if (tacticsPanel == null)
                {
                    Speech.SpeechPipeline.SpeakInterrupt("tactics panel not found");
                    _stack.Pop();
                    return;
                }
                var posName = indicator.name;
                var playerName = _adapter.ReadPlayerName(indicator) ?? "unknown";
                _stack.Replace(new PositionGridHandler(_stack, tacticsPanel, posName, playerName));
                break;
        }
    }
}

using FM27Access.Navigation.Input;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Input.Handlers;

internal class PositionGridHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly VisualElement _tacticsPanel;
    private string _sourceSlotName;
    private readonly string _sourcePlayerName;
    private int _rowIndex;
    private int _colIndex;

    private static readonly string[][] Rows =
    {
        new[] { "GK" },
        new[] { "DL", "DCL", "DC", "DCR", "DR" },
        new[] { "WBL", "DML", "DMC", "DMR", "WBR" },
        new[] { "ML", "MCL", "MC", "MCR", "MR" },
        new[] { "AML", "AMCL", "AMC", "AMCR", "AMR" },
        new[] { "STL", "ST", "STR" },
        new[] { "Exit" },
    };

    private static readonly KeyboardKey[] _claimedKeys =
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.LeftArrow, KeyboardKey.RightArrow,
        KeyboardKey.Enter, KeyboardKey.Escape,
    };

    public PositionGridHandler(HandlerStack stack, VisualElement tacticsPanel, string sourceSlotName, string sourcePlayerName)
    {
        _stack = stack;
        _tacticsPanel = tacticsPanel;
        _sourceSlotName = sourceSlotName;
        _sourcePlayerName = sourcePlayerName;
    }

    public string DisplayName => "PositionGrid";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case KeyboardKey.LeftArrow:
                _colIndex = Math.Max(0, _colIndex - 1);
                AnnounceCurrentCell();
                return true;

            case KeyboardKey.RightArrow:
                _colIndex = Math.Min(Rows[_rowIndex].Length - 1, _colIndex + 1);
                AnnounceCurrentCell();
                return true;

            case KeyboardKey.UpArrow:
                _rowIndex = Math.Max(0, _rowIndex - 1);
                _colIndex = Math.Min(_colIndex, Rows[_rowIndex].Length - 1);
                AnnounceCurrentCell();
                return true;

            case KeyboardKey.DownArrow:
                _rowIndex = Math.Min(Rows.Length - 1, _rowIndex + 1);
                _colIndex = Math.Min(_colIndex, Rows[_rowIndex].Length - 1);
                AnnounceCurrentCell();
                return true;

            case KeyboardKey.Enter:
                HandleEnter();
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
        if (!FindSlotInGrid(_sourceSlotName, out int row, out int col))
        {
            Speech.Log.Error($"[PositionGrid] Source slot '{_sourceSlotName}' not found in grid");
            return;
        }
        _rowIndex = row;
        _colIndex = col;
        Speech.SpeechPipeline.SpeakInterrupt(
            $"{_sourceSlotName} — selected for move ({_sourcePlayerName}). Navigate to an available position and press Enter.");
    }

    public void OnDeactivate() { }

    private void HandleEnter()
    {
        var slot = Rows[_rowIndex][_colIndex];

        if (slot == "Exit")
        {
            Speech.SpeechPipeline.SpeakInterrupt("cancelled");
            _stack.Pop();
            return;
        }

        if (slot == _sourceSlotName)
            return;

        // Swaps are allowed when the game permits them; the engine applies the game's own CanDrop
        // validity, so an occupied slot becomes a swap and an invalid target is reported, not forced.
        var result = Tactics.PitchMoveEngine.TryMove(_tacticsPanel, _sourceSlotName, slot);
        Speech.SpeechPipeline.SpeakInterrupt(result.Message);
        if (result.Ok)
            _sourceSlotName = slot; // the player now sits here; further moves start from the new slot
    }

    private void AnnounceCurrentCell()
    {
        var slot = Rows[_rowIndex][_colIndex];

        if (slot == "Exit")
        {
            Speech.SpeechPipeline.SpeakInterrupt("Exit");
            return;
        }

        if (slot == _sourceSlotName)
        {
            Speech.SpeechPipeline.SpeakInterrupt($"{slot} — selected for move ({_sourcePlayerName})");
            return;
        }

        var occupied = ReadPitchState();
        if (occupied.TryGetValue(slot, out var name))
            Speech.SpeechPipeline.SpeakInterrupt($"{slot} — occupied by {name}");
        else
            Speech.SpeechPipeline.SpeakInterrupt($"{slot} — available");
    }

    private static bool FindSlotInGrid(string slotName, out int row, out int col)
    {
        for (int r = 0; r < Rows.Length; r++)
        {
            for (int c = 0; c < Rows[r].Length; c++)
            {
                if (Rows[r][c] == slotName)
                {
                    row = r;
                    col = c;
                    return true;
                }
            }
        }
        row = 0;
        col = 0;
        return false;
    }

    private Dictionary<string, string> ReadPitchState()
    {
        var result = new Dictionary<string, string>();
        CollectOccupiedSlots(_tacticsPanel, result);
        return result;
    }

    private static void CollectOccupiedSlots(VisualElement parent, Dictionary<string, string> result)
    {
        if (parent.GetIl2CppType().Name == "FMPitchIndicator")
        {
            var playerName = ReadPlayerName(parent);
            if (playerName != null)
                result[parent.name] = playerName;
            return;
        }

        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectOccupiedSlots(parent.hierarchy[i], result);
    }

    private static string ReadPlayerName(VisualElement indicator)
    {
        var nameText = FindDescendantByName(indicator, "PlayerNameText");
        if (nameText == null) return null;

        try
        {
            var textEl = nameText.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[PositionGrid] Failed to read player name: {e.GetType().Name}");
        }
        return null;
    }

    private static VisualElement FindDescendantByName(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.name == name)
                return child;
            var deeper = FindDescendantByName(child, name);
            if (deeper != null)
                return deeper;
        }
        return null;
    }
}

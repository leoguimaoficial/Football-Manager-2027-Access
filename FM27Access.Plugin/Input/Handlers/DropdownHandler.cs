using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Manages dropdown interaction: navigate choices with up/down, confirm
/// with Enter, drill into submenus with Enter on items that have children,
/// go back with Left arrow, cancel with Escape.
/// </summary>
internal class DropdownHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.DropdownAdapter _rootAdapter;
    private readonly Stack<DropdownLevel> _levelStack = new();
    private int _waitFrames;
    private bool _waitingForPopup = true;
    private bool _waitingForChild;
    private bool _waitingForHide;
    private bool _waitingForScroll;
    private int _popupVisibleFrame;
    private int _childVisibleFrame;
    private string _pendingChoiceText;

    // Spoken when the game refused the commit, in place of the chosen label — the player has no
    // other way to tell that the value did not move.
    private const string NotApplied = "not applied";

    // Scroll-on-demand mode for large lists
    private bool _scrollMode;
    private bool _scrollModeFirstRead;
    private int _totalCount;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.LeftArrow, KeyboardKey.Enter, KeyboardKey.Escape,
    };

    private struct DropdownLevel
    {
        public Adapters.DropdownAdapter Adapter;
        public List<string> Choices;
        public bool[] HasChildren;
        public int CursorIndex;
        public string LevelName;
    }

    public DropdownHandler(HandlerStack stack, Adapters.DropdownAdapter adapter)
    {
        _stack = stack;
        _rootAdapter = adapter;
        _levelStack.Push(new DropdownLevel
        {
            Adapter = adapter,
            Choices = null,
            HasChildren = null,
            CursorIndex = 0,
            LevelName = null,
        });
    }

    public string DisplayName => "Dropdown";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        if (_waitingForPopup || _waitingForChild || _waitingForHide || _waitingForScroll) return true;

        var current = _levelStack.Peek();

        switch (key)
        {
            case KeyboardKey.DownArrow:
            {
                var level = _levelStack.Pop();
                int count = _scrollMode ? _totalCount : level.Choices.Count;
                level.CursorIndex = (level.CursorIndex + 1) % count;
                _levelStack.Push(level);
                if (_scrollMode)
                    ScrollAndRead(level);
                else
                    SpeakCurrentChoice(level);
                return true;
            }

            case KeyboardKey.UpArrow:
            {
                var level = _levelStack.Pop();
                int count = _scrollMode ? _totalCount : level.Choices.Count;
                level.CursorIndex = (level.CursorIndex - 1 + count) % count;
                _levelStack.Push(level);
                if (_scrollMode)
                    ScrollAndRead(level);
                else
                    SpeakCurrentChoice(level);
                return true;
            }

            case KeyboardKey.Enter:
                if (current.HasChildren.Length > current.CursorIndex && current.HasChildren[current.CursorIndex])
                {
                    Speech.Log.Info($"[Dropdown] Drilling into child at index {current.CursorIndex}");
                    current.Adapter.TriggerChildOpen(current.CursorIndex);
                    _waitFrames = 0;
                    _childVisibleFrame = 0;
                    _waitingForChild = true;
                }
                else if (_scrollMode && _levelStack.Count == 1)
                {
                    Speech.Log.Info($"[Dropdown] Confirmed scroll-mode selection: index {current.CursorIndex}");
                    var text = current.Adapter.ReadChoiceAtIndex(current.CursorIndex);
                    bool scrollCommitted = current.Adapter.ConfirmSelection(current.CursorIndex);
                    Speech.SpeechPipeline.SpeakInterrupt(
                        scrollCommitted ? text ?? "selected" : NotApplied);
                    _stack.Pop();
                }
                else
                {
                    Speech.Log.Info($"[Dropdown] Confirmed leaf selection: index {current.CursorIndex}");
                    string choiceText = current.Choices[current.CursorIndex];
                    if (_levelStack.Count == 1)
                    {
                        // A refused commit must not be announced as the choice: the player cannot
                        // see that the value never moved.
                        if (!current.Adapter.ConfirmSelection(current.CursorIndex))
                            choiceText = NotApplied;
                    }
                    else
                    {
                        current.Adapter.ConfirmLeafSelection(current.CursorIndex);
                        _pendingChoiceText = choiceText;
                        _waitFrames = 0;
                        _waitingForHide = true;
                        return true;
                    }
                    Speech.SpeechPipeline.SpeakInterrupt(choiceText);
                    _stack.Pop();
                }
                return true;

            case KeyboardKey.LeftArrow when _levelStack.Count > 1:
            {
                var childLevel = _levelStack.Pop();
                childLevel.Adapter.HideEntireHierarchy();
                var parentLevel = _levelStack.Peek();
                SpeakCurrentChoice(parentLevel, "back. ");
                Speech.Log.Info($"[Dropdown] Back to parent level. Cursor at {parentLevel.CursorIndex}.");
                return true;
            }

            case KeyboardKey.Escape:
                Speech.Log.Info("[Dropdown] Cancelled.");
                _rootAdapter.HideEntireHierarchy();
                Speech.SpeechPipeline.SpeakInterrupt("cancelled");
                _stack.Pop();
                return true;

            default:
                return true;
        }
    }

    public bool Tick()
    {
        if (_waitingForPopup)
        {
            _waitFrames++;
            var rootAdapter = _levelStack.Peek().Adapter;
            if (rootAdapter.IsPopupVisible())
            {
                if (_popupVisibleFrame == 0)
                    _popupVisibleFrame = _waitFrames;
                if (_waitFrames - _popupVisibleFrame < 6)
                    return true;
                var choices = rootAdapter.ReadChoices();
                int totalCount = rootAdapter.GetChoicesCount();
                if (choices.Count > 0 || totalCount > 0)
                {
                    var hasChildren = rootAdapter.BuildHasChildrenArray();
                    bool isHierarchical = false;
                    foreach (var h in hasChildren) { if (h) { isHierarchical = true; break; } }

                    int cursor = rootAdapter.GetSelectedIndex();

                    // Determine if we need scroll-on-demand mode
                    _scrollMode = totalCount > choices.Count;
                    _totalCount = totalCount;

                    if (_scrollMode)
                    {
                        if (cursor < 0 || cursor >= totalCount)
                            cursor = 0;

                        var level = _levelStack.Pop();
                        level.Choices = null;
                        level.HasChildren = hasChildren;
                        level.CursorIndex = cursor;
                        _levelStack.Push(level);

                        _waitingForPopup = false;

                        rootAdapter.ScrollToChoice(cursor);
                        _waitFrames = 0;
                        _waitingForScroll = true;
                        _scrollModeFirstRead = true;
                        Speech.Log.Info($"[Dropdown] Scroll mode. {totalCount} total choices (DOM has {choices.Count}), cursor={cursor}.");
                    }
                    else
                    {
                        if (cursor < 0 || cursor >= choices.Count)
                            cursor = 0;

                        var level = _levelStack.Pop();
                        level.Choices = choices;
                        level.HasChildren = hasChildren;
                        level.CursorIndex = cursor;
                        _levelStack.Push(level);

                        _waitingForPopup = false;
                        var currentChoice = choices[cursor];
                        string suffix = hasChildren.Length > cursor && hasChildren[cursor] ? ", has submenu" : "";
                        string instructions = isHierarchical ? "up and down to change, enter to open." : "up and down to change, enter to confirm.";
                        Speech.SpeechPipeline.SpeakInterrupt($"{currentChoice}{suffix}. {instructions}");
                        Speech.Log.Info($"[Dropdown] Ready. {choices.Count} choices, hierarchical={isHierarchical}, cursor={cursor}.");
                    }
                }
                else
                {
                    Speech.Log.Warn("[Dropdown] Popup visible but no choices found.");
                    Speech.SpeechPipeline.SpeakInterrupt("no options found");
                    rootAdapter.HidePopup();
                    _stack.Pop();
                }
            }
            else if (_waitFrames > 30)
            {
                Speech.Log.Warn("[Dropdown] Popup did not appear after 30 frames.");
                Speech.SpeechPipeline.SpeakInterrupt("dropdown failed to open");
                _stack.Pop();
            }
            return true;
        }

        if (_waitingForHide)
        {
            _waitFrames++;
            if (_waitFrames >= 2)
            {
                _rootAdapter.HideEntireHierarchy();
                Speech.SpeechPipeline.SpeakInterrupt(_pendingChoiceText);
                Speech.Log.Info($"[Dropdown] Deferred hide complete after {_waitFrames} frames.");
                _waitingForHide = false;
                _stack.Pop();
            }
            return true;
        }

        if (_waitingForScroll)
        {
            _waitFrames++;
            if (_waitFrames >= 2)
            {
                _waitingForScroll = false;
                var level = _levelStack.Peek();
                var text = level.Adapter.ReadChoiceAtIndex(level.CursorIndex);
                if (text == null)
                {
                    Speech.Log.Warn($"[Dropdown] Could not read choice at index {level.CursorIndex} after scroll.");
                    text = $"item {level.CursorIndex + 1}";
                }
                if (_scrollModeFirstRead)
                {
                    _scrollModeFirstRead = false;
                    text += ". up and down to change, enter to confirm.";
                }
                Speech.SpeechPipeline.SpeakInterrupt(text);
            }
            return true;
        }

        if (_waitingForChild)
        {
            _waitFrames++;
            var parentLevel = _levelStack.Peek();
            var childAdapter = parentLevel.Adapter.GetVisibleChildAdapter(parentLevel.CursorIndex);
            if (childAdapter != null)
            {
                if (_childVisibleFrame == 0)
                    _childVisibleFrame = _waitFrames;
                if (_waitFrames - _childVisibleFrame < 6)
                    return true;
                var choices = childAdapter.ReadChoices();
                if (choices.Count > 0)
                {
                    var hasChildren = childAdapter.BuildHasChildrenArray();
                    string parentChoice = parentLevel.Choices[parentLevel.CursorIndex];
                    _levelStack.Push(new DropdownLevel
                    {
                        Adapter = childAdapter,
                        Choices = choices,
                        HasChildren = hasChildren,
                        CursorIndex = 0,
                        LevelName = parentChoice,
                    });
                    _waitingForChild = false;
                    string suffix = hasChildren.Length > 0 && hasChildren[0] ? ", has submenu" : "";
                    Speech.SpeechPipeline.SpeakInterrupt($"{parentChoice} submenu. {choices[0]}{suffix}");
                    Speech.Log.Info($"[Dropdown] Child opened. {choices.Count} choices under \"{parentChoice}\".");
                }
                else
                {
                    Speech.Log.Warn("[Dropdown] Child popup visible but no choices.");
                    Speech.SpeechPipeline.SpeakInterrupt("no options found");
                    _waitingForChild = false;
                }
            }
            else if (_waitFrames > 30)
            {
                Speech.Log.Warn("[Dropdown] Child dropdown not visible after 30 frames.");
                _waitingForChild = false;
                SpeakCurrentChoice(_levelStack.Peek(), "submenu failed to open. ");
            }
            return true;
        }

        return false;
    }

    public void OnActivate()
    {
        Speech.Log.Info("[Input] Opening dropdown popup.");
        _levelStack.Peek().Adapter.ShowPopup();
        _waitFrames = 0;
    }

    public void OnDeactivate() { }

    private void ScrollAndRead(DropdownLevel level)
    {
        level.Adapter.ScrollToChoice(level.CursorIndex);
        _waitFrames = 0;
        _waitingForScroll = true;
    }

    private static void SpeakCurrentChoice(DropdownLevel level, string prefix = "")
    {
        string suffix = level.HasChildren.Length > level.CursorIndex && level.HasChildren[level.CursorIndex] ? ", has submenu" : "";
        Speech.SpeechPipeline.SpeakInterrupt($"{prefix}{level.Choices[level.CursorIndex]}{suffix}");
    }
}

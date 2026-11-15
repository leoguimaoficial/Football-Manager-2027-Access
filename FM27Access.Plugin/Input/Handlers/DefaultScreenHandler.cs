using FM27Access.Navigation;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Baseline handler at the bottom of the stack. Handles normal screen
/// navigation: arrow keys, Home/End, Ctrl+Left/Right panel cycling, Enter activation,
/// debug keys. On Enter, may push modal handlers for dropdowns, text
/// fields, date pickers.
/// </summary>
internal class DefaultScreenHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Action<int> _requestRescan;

    private int _messageTitleJumpFrames;
    private const int MessageTitleMinDelay = 15;
    private const int MessageTitleCheckInterval = 5;
    private const int MessageTitleMaxFrames = 200;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        // Mod navigation keys
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.LeftArrow, KeyboardKey.RightArrow,
        KeyboardKey.Enter,
        KeyboardKey.Home, KeyboardKey.End,

        // Debug tools
#if FM27_DEVELOPER_TOOLS
        KeyboardKey.F8, KeyboardKey.F9, KeyboardKey.F10, KeyboardKey.F11, KeyboardKey.F12,
#endif

        // Continue button shortcuts
        KeyboardKey.Tab,

        // Reserved for future navigation features (blocks game shortcuts)
        KeyboardKey.B, KeyboardKey.C, KeyboardKey.H, KeyboardKey.R, KeyboardKey.T,
    };

    public DefaultScreenHandler(HandlerStack stack, Action<int> requestRescan)
    {
        _stack = stack;
        _requestRescan = requestRescan;
    }

    public string DisplayName => "Screen";
    public bool CapturesAllInput => false;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        bool ctrlAlt = (modifiers & KeyModifiers.Ctrl) != 0 && (modifiers & KeyModifiers.Alt) != 0;
        bool inTable = NavigationManager.IsCurrentInTable();
        bool inCalendar = NavigationManager.IsCurrentInCalendar();

        switch (key)
        {
            case KeyboardKey.UpArrow:
                if (ctrlAlt && inTable)
                    NavigationManager.MoveTableCell(NavigationDirection.Up);
                else if (ctrlAlt)
                    NavigationManager.Speak("Not inside table");
                else if (inTable)
                    NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Up);
                else if (inCalendar)
                    NavigationManager.MoveCalendarDay(NavigationDirection.Up);
                else
                    NavigationManager.MoveCursor(NavigationDirection.Up);
                return true;
            case KeyboardKey.DownArrow:
                if (ctrlAlt && inTable)
                    NavigationManager.MoveTableCell(NavigationDirection.Down);
                else if (ctrlAlt)
                    NavigationManager.Speak("Not inside table");
                else if (inTable)
                    NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Down);
                else if (inCalendar)
                    NavigationManager.MoveCalendarDay(NavigationDirection.Down);
                else
                    NavigationManager.MoveCursor(NavigationDirection.Down);
                return true;
            case KeyboardKey.LeftArrow:
                if ((modifiers & (KeyModifiers.Ctrl | KeyModifiers.Shift)) == (KeyModifiers.Ctrl | KeyModifiers.Shift))
                {
                    NavigationManager.NavigateBack();
                    return true;
                }
                if (ctrlAlt && inTable)
                {
                    NavigationManager.MoveTableCell(NavigationDirection.Left);
                    return true;
                }
                if (ctrlAlt)
                {
                    NavigationManager.Speak("Not inside table");
                    return true;
                }
                if ((modifiers & KeyModifiers.Ctrl) != 0)
                {
                    NavigationManager.CyclePanelBackward();
                    return true;
                }
                if (inTable)
                    NavigationManager.MoveTableInnerCell(NavigationDirection.Left);
                else if (inCalendar)
                    NavigationManager.MoveCalendarInnerStop(NavigationDirection.Left);
                else
                    NavigationManager.MoveCursor(NavigationDirection.Left);
                return true;
            case KeyboardKey.RightArrow:
                if ((modifiers & (KeyModifiers.Ctrl | KeyModifiers.Shift)) == (KeyModifiers.Ctrl | KeyModifiers.Shift))
                {
                    NavigationManager.NavigateForward();
                    return true;
                }
                if (ctrlAlt && inTable)
                {
                    NavigationManager.MoveTableCell(NavigationDirection.Right);
                    return true;
                }
                if (ctrlAlt)
                {
                    NavigationManager.Speak("Not inside table");
                    return true;
                }
                if ((modifiers & KeyModifiers.Ctrl) != 0)
                {
                    NavigationManager.CyclePanelForward();
                    return true;
                }
                if (inTable)
                    NavigationManager.MoveTableInnerCell(NavigationDirection.Right);
                else if (inCalendar)
                    NavigationManager.MoveCalendarInnerStop(NavigationDirection.Right);
                else
                    NavigationManager.MoveCursor(NavigationDirection.Right);
                return true;

            case KeyboardKey.Home:
                NavigationManager.JumpToFirst();
                return true;
            case KeyboardKey.End:
                NavigationManager.JumpToLast();
                return true;

#if FM27_DEVELOPER_TOOLS
            case KeyboardKey.F8:
                DebugTools.ClearUnclassifiedTracker();
                return true;
            case KeyboardKey.F9:
                DebugTools.DumpFullTree();
                return true;
            case KeyboardKey.F10:
                DebugTools.DumpCursorContext();
                return true;
            case KeyboardKey.F11:
                DebugTools.DumpUnclassifiedTracker();
                return true;
            case KeyboardKey.F12:
                DebugTools.DumpAdHoc();
                return true;
#endif

            case KeyboardKey.Tab:
                if ((modifiers & KeyModifiers.Shift) != 0)
                    NavigationManager.JumpToContinueButton();
                else
                    NavigationManager.AnnounceContinueButton();
                return true;

            case KeyboardKey.Enter:
                HandleEnter();
                return true;

            case KeyboardKey.C:
                NavigationManager.JumpToMatch(
                    e => e.Role == "dropdown" || e.Role == "searchable dropdown" || e.Role == "combobox",
                    forward: (modifiers & KeyModifiers.Shift) == 0,
                    "no more dropdowns");
                return true;

            case KeyboardKey.B:
                NavigationManager.JumpToMatch(
                    e => e.Role == "button",
                    forward: (modifiers & KeyModifiers.Shift) == 0,
                    "no more buttons");
                return true;

            case KeyboardKey.T:
                NavigationManager.JumpToNextContainer(
                    NavigationManager.TableContextFinder,
                    forward: (modifiers & KeyModifiers.Shift) == 0,
                    "no more tables");
                return true;

            case KeyboardKey.H:
                if (NavigationManager.HeadingPredicate is { } isHeading)
                {
                    NavigationManager.JumpToMatch(
                        isHeading,
                        forward: (modifiers & KeyModifiers.Shift) == 0,
                        "no headings");
                }
                return true;

            case KeyboardKey.R:
                NavigationManager.JumpToMatch(
                    e => e.Role == "radio button",
                    forward: (modifiers & KeyModifiers.Shift) == 0,
                    "no more radio buttons");
                return true;

            case KeyboardKey.U:
                _messageTitleJumpFrames = 1;
                return false;

            default:
                return false;
        }
    }

    public bool Tick()
    {
        if (_messageTitleJumpFrames == 0) return false;

        _messageTitleJumpFrames++;

        if (_messageTitleJumpFrames > MessageTitleMaxFrames)
        {
            _messageTitleJumpFrames = 0;
            Speech.Log.Warn("[Input] U key: MessageTitle not found after timeout");
            return false;
        }

        if (_messageTitleJumpFrames < MessageTitleMinDelay) return false;
        if ((_messageTitleJumpFrames - MessageTitleMinDelay) % MessageTitleCheckInterval != 0) return false;

        var panel = NavigationManager.GetActivePanel();
        if (panel == null) return false;

        var stop = NavigationManager.FindFirstStop(panel);
        while (stop != null)
        {
            if (stop is Adapters.PluginAdapterBase adapter
                && adapter.BackingElement.name == "MessageTitle")
            {
                NavigationManager.SetCursorSilently(stop);
                NavigationManager.Speak(stop.GetAnnouncement());
                _messageTitleJumpFrames = 0;
                return true;
            }
            stop = NavigationManager.FindNextStop(stop, NavigationDirection.Down);
        }
        return false;
    }

    public void OnActivate() { }

    public void OnDeactivate() { }

    private void HandleEnter()
    {
        var current = NavigationManager.GetCurrentElement();
        if (current == null || !current.CanActivate) return;

        if (current.Role == "text field" || current.Role == "number field")
        {
            _stack.Push(new EditModeHandler(_stack, current));
        }
        else if (current is Adapters.ComboboxAdapter comboboxAdapter)
        {
            _stack.Push(new ComboboxHandler(_stack, comboboxAdapter));
        }
        else if (current is Adapters.SearchableDropdownAdapter searchAdapter)
        {
            _stack.Push(new SearchDropdownHandler(_stack, searchAdapter));
        }
        else if (current is Adapters.TacticsPlayerAdapter tacticsPlayer)
        {
            _stack.Push(new TacticsPlayerHandler(_stack, tacticsPlayer));
        }
        else if (current is Adapters.SquadPlayerButtonAdapter playerButtonAdapter)
        {
            _stack.Push(new DropdownHandler(_stack, playerButtonAdapter.GetChildDropdownAdapter()));
        }
        else if (current is Adapters.DropdownAdapter dropdownAdapter)
        {
            _stack.Push(new DropdownHandler(_stack, dropdownAdapter));
        }
        else if (current is Adapters.SearchBoxAdapter searchBoxAdapter)
        {
            _stack.Push(new SearchBoxHandler(_stack, searchBoxAdapter));
        }
        else if (current is Adapters.InfoButtonAdapter infoAdapter)
        {
            _stack.Push(new TooltipReadHandler(_stack, infoAdapter));
        }
        else if (current is Adapters.DateSelectorAdapter dateAdapter)
        {
            if (!dateAdapter.IsFloating())
            {
                Speech.Log.Warn("[Input] Inline date selector not yet supported.");
                Speech.SpeechPipeline.SpeakInterrupt("inline date picker not supported");
            }
            else
            {
                _stack.Push(new DatePickerHandler(_stack, dateAdapter));
            }
        }
        else if (current is Adapters.MessageButtonAdapter messageButton)
        {
            var subject = messageButton.GetSubject();
            current.Activate();
            messageButton.BackingElement.schedule.Execute((System.Action)(() =>
            {
                var panel = NavigationManager.GetActivePanel();
                if (panel == null) return;
                var stop = NavigationManager.FindFirstStop(panel);
                while (stop != null)
                {
                    if (stop is Adapters.PluginAdapterBase adapter
                        && adapter.BackingElement.name == "MessageTitle")
                    {
                        NavigationManager.SetCursorSilently(stop);
                        if (subject != null)
                            NavigationManager.Speak(subject);
                        return;
                    }
                    stop = NavigationManager.FindNextStop(stop, NavigationDirection.Down);
                }
            }));
        }
        else if (current is Adapters.TabItemAdapter)
        {
            current.Activate();
            _requestRescan(3);
        }
        else if (current is Adapters.ButtonAdapter)
        {
            current.Activate();
            var state = current.GetState();
            if (state.Contains("selected"))
                Speech.SpeechPipeline.SpeakInterrupt($"{current.GetLabel()} selected");
            _requestRescan(3);
        }
        else
        {
            Speech.Log.Info($"[Input] Activating: {current.Role}");
            current.Activate();
            // Checkbox/toggle/radio activation is intentionally silent: the companion (HTML)
            // surface is the source of truth and re-announces the new state via the screen reader,
            // so a Prism announcement here would just double up.
            _requestRescan(3);
        }
    }
}

using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Manages date picker interaction: select year, month, day in sequence
/// with up/down, confirm each with Enter, cancel with Escape. After day
/// is confirmed, applies the date via the adapter's UI controls.
/// </summary>
internal class DatePickerHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.DateSelectorAdapter _adapter;
    private int _waitFrames;
    private bool _waitingForPopup = true;

    private enum Phase { SelectYear, SelectMonth, SelectDay, Applying }
    private Phase _phase;

    private int _day, _month, _year;
    private List<int> _validYears;
    private List<int> _validMonths;

    // Apply sub-phases
    private enum ApplyPhase { SetYear, WaitYear, NavigateMonth, WaitMonthStep, SelectDay, WaitDay, Confirm, Done }
    private ApplyPhase _applyPhase;
    private int _applyWaitFrames;
    private int _monthStepsRemaining;
    // Set when a step of the apply refuses; Done speaks this instead of announcing a date that
    // was never committed.
    private string _applyFailure;

    private static readonly KeyboardKey[] _claimedKeys = new[]
    {
        KeyboardKey.UpArrow, KeyboardKey.DownArrow,
        KeyboardKey.Enter, KeyboardKey.Escape,
    };

    public DatePickerHandler(HandlerStack stack, Adapters.DateSelectorAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
    }

    public string DisplayName => "Date Picker";
    public bool CapturesAllInput => true;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        if (_waitingForPopup || _phase == Phase.Applying) return true;

        if (key == KeyboardKey.Escape)
        {
            Speech.Log.Info("[DatePicker] Cancelled.");
            _adapter.ClickCancel();
            Speech.SpeechPipeline.SpeakInterrupt("cancelled");
            _stack.Pop();
            return true;
        }

        switch (_phase)
        {
            case Phase.SelectYear:
                HandleYearInput(key);
                return true;
            case Phase.SelectMonth:
                HandleMonthInput(key);
                return true;
            case Phase.SelectDay:
                HandleDayInput(key);
                return true;
        }
        return true;
    }

    public bool Tick()
    {
        if (_waitingForPopup)
        {
            _waitFrames++;
            if (_adapter.IsPopupVisible())
            {
                SeedFields();
                _waitingForPopup = false;
                _phase = Phase.SelectYear;
                Speech.SpeechPipeline.SpeakInterrupt($"date picker. select year. {_year}. up and down to change, enter to confirm.");
                Speech.Log.Info($"[DatePicker] Active. Day={_day} Month={_month} Year={_year}");
            }
            else if (_waitFrames > 30)
            {
                Speech.Log.Warn("[DatePicker] Popup did not appear after 30 frames.");
                Speech.SpeechPipeline.SpeakInterrupt("date picker failed to open");
                _stack.Pop();
            }
            return true;
        }

        if (_phase == Phase.Applying)
        {
            TickApply();
            return true;
        }

        return false;
    }

    public void OnActivate()
    {
        Speech.Log.Info("[Input] Opening date picker popup.");
        _adapter.ShowPopup();
        _waitFrames = 0;
    }

    public void OnDeactivate() { }

    private void HandleYearInput(KeyboardKey key)
    {
        if (key == KeyboardKey.UpArrow)
        {
            AdjustYear(1);
            Speech.SpeechPipeline.SpeakInterrupt(_year.ToString());
        }
        else if (key == KeyboardKey.DownArrow)
        {
            AdjustYear(-1);
            Speech.SpeechPipeline.SpeakInterrupt(_year.ToString());
        }
        else if (key == KeyboardKey.Enter)
        {
            _phase = Phase.SelectMonth;
            Speech.SpeechPipeline.SpeakInterrupt($"year {_year}. select month. {MonthName()}. up and down to change, enter to confirm.");
        }
    }

    private void HandleMonthInput(KeyboardKey key)
    {
        if (key == KeyboardKey.UpArrow)
        {
            AdjustMonth(1);
            Speech.SpeechPipeline.SpeakInterrupt(MonthName());
        }
        else if (key == KeyboardKey.DownArrow)
        {
            AdjustMonth(-1);
            Speech.SpeechPipeline.SpeakInterrupt(MonthName());
        }
        else if (key == KeyboardKey.Enter)
        {
            ClampDay();
            _phase = Phase.SelectDay;
            int daysInMonth = DateTime.DaysInMonth(_year, _month);
            Speech.SpeechPipeline.SpeakInterrupt($"{MonthName()}. select day. {_day}. 1 to {daysInMonth} available. up and down to change, enter to confirm.");
        }
    }

    private void HandleDayInput(KeyboardKey key)
    {
        if (key == KeyboardKey.UpArrow)
        {
            AdjustDay(1);
            Speech.SpeechPipeline.SpeakInterrupt(_day.ToString());
        }
        else if (key == KeyboardKey.DownArrow)
        {
            AdjustDay(-1);
            Speech.SpeechPipeline.SpeakInterrupt(_day.ToString());
        }
        else if (key == KeyboardKey.Enter)
        {
            Speech.Log.Info($"[DatePicker] Applying: {MonthName()} {_day} {_year}");
            Speech.SpeechPipeline.SpeakInterrupt($"setting {MonthName()} {_day} {_year}");
            _phase = Phase.Applying;
            _applyPhase = ApplyPhase.SetYear;
            _applyWaitFrames = 0;
        }
    }

    private void TickApply()
    {
        switch (_applyPhase)
        {
            case ApplyPhase.SetYear:
                int yearIdx = -1;
                if (_validYears.Count > 0)
                    yearIdx = _validYears.IndexOf(_year);
                if (yearIdx >= 0)
                {
                    _adapter.SetYearDropdown(yearIdx);
                    Speech.Log.Info($"[DatePicker] Set year dropdown to index {yearIdx} for year {_year}");
                }
                else
                    Speech.Log.Warn($"[DatePicker] Year {_year} not found.");
                _applyPhase = ApplyPhase.WaitYear;
                _applyWaitFrames = 0;
                break;

            case ApplyPhase.WaitYear:
                _applyWaitFrames++;
                if (_applyWaitFrames >= 3)
                {
                    // Position in the month dropdown, which lists only the months the controller
                    // allows for the year just set — not month - 1. See TryGetMonthStepDelta.
                    if (!_adapter.TryGetMonthStepDelta(_month, out _monthStepsRemaining))
                    {
                        FailApply($"{MonthName()} is not selectable here");
                        break;
                    }
                    Speech.Log.Info($"[DatePicker] Month nav: steps={_monthStepsRemaining}");
                    _applyPhase = _monthStepsRemaining == 0 ? ApplyPhase.SelectDay : ApplyPhase.NavigateMonth;
                }
                break;

            case ApplyPhase.NavigateMonth:
                if (_monthStepsRemaining > 0)
                {
                    _adapter.ClickNextMonth();
                    _monthStepsRemaining--;
                }
                else if (_monthStepsRemaining < 0)
                {
                    _adapter.ClickPreviousMonth();
                    _monthStepsRemaining++;
                }
                Speech.Log.Info($"[DatePicker] Month step. Remaining={_monthStepsRemaining}");
                _applyPhase = ApplyPhase.WaitMonthStep;
                _applyWaitFrames = 0;
                break;

            case ApplyPhase.WaitMonthStep:
                _applyWaitFrames++;
                if (_applyWaitFrames >= 2)
                    _applyPhase = _monthStepsRemaining == 0 ? ApplyPhase.SelectDay : ApplyPhase.NavigateMonth;
                break;

            case ApplyPhase.SelectDay:
                if (!_adapter.SelectDate(_year, _month, _day))
                {
                    FailApply($"{MonthName()} {_day} is not available");
                    break;
                }
                _applyPhase = ApplyPhase.WaitDay;
                _applyWaitFrames = 0;
                break;

            case ApplyPhase.WaitDay:
                _applyWaitFrames++;
                if (_applyWaitFrames >= 2)
                    _applyPhase = ApplyPhase.Confirm;
                break;

            case ApplyPhase.Confirm:
                _adapter.ClickConfirm();
                _applyPhase = ApplyPhase.Done;
                break;

            case ApplyPhase.Done:
                if (_applyFailure != null)
                {
                    Speech.SpeechPipeline.SpeakInterrupt(_applyFailure);
                    Speech.Log.Warn($"[DatePicker] Apply refused: {_applyFailure}");
                }
                else
                {
                    Speech.SpeechPipeline.SpeakInterrupt($"{MonthName()} {_day} {_year} confirmed");
                    Speech.Log.Info($"[DatePicker] Confirmed: {MonthName()} {_day} {_year}");
                }
                _stack.Pop();
                break;
        }
    }

    // Abandons the apply without committing: closes the picker and routes the reason to Done, so
    // the player hears why instead of a confirmation for a date the game never took.
    private void FailApply(string reason)
    {
        _applyFailure = reason;
        _adapter.ClickCancel();
        _applyPhase = ApplyPhase.Done;
    }

    private void SeedFields()
    {
        if (_adapter.TryGetSelectedDateTime(out int day, out int month, out int year))
        {
            _day = day;
            _month = month;
            _year = year;
            Speech.Log.Info($"[DatePicker] Seeded from m_selectedDateTime: Day={day} Month={month} Year={year}");
        }
        else if (_adapter.TryParseDisplayedDate(out day, out month, out year))
        {
            _day = day;
            _month = month;
            _year = year;
            Speech.Log.Info($"[DatePicker] Seeded from displayed text: Day={day} Month={month} Year={year}");
        }
        else
        {
            Speech.Log.Error("[DatePicker] Could not read current date from adapter or displayed text.");
            throw new InvalidOperationException("DatePicker could not determine the current date.");
        }

        _validYears = _adapter.ReadControllerYears();
        _validMonths = _adapter.ReadControllerMonths();

        if (_validYears.Count == 0) Speech.Log.Warn("[DatePicker] No valid years from controller.");
        if (_validMonths.Count == 0) Speech.Log.Warn("[DatePicker] No valid months from controller.");
    }

    private string MonthName() =>
        System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(_month);

    private static int CycleIndex(List<int> list, int current, int direction)
    {
        int idx = list.IndexOf(current);
        if (idx < 0) idx = 0;
        idx += direction;
        if (idx >= list.Count) idx = 0;
        if (idx < 0) idx = list.Count - 1;
        return idx;
    }

    private void AdjustYear(int direction)
    {
        if (_validYears.Count > 0)
            _year = _validYears[CycleIndex(_validYears, _year, direction)];
        else
            _year += direction;
    }

    private void AdjustMonth(int direction)
    {
        if (_validMonths.Count > 0)
            _month = _validMonths[CycleIndex(_validMonths, _month, direction)];
        else
        {
            _month += direction;
            if (_month > 12) _month = 1;
            if (_month < 1) _month = 12;
        }
    }

    private void AdjustDay(int direction)
    {
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        _day += direction;
        if (_day > daysInMonth) _day = 1;
        if (_day < 1) _day = daysInMonth;
    }

    private void ClampDay()
    {
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        if (_day > daysInMonth)
            _day = daysInMonth;
    }
}

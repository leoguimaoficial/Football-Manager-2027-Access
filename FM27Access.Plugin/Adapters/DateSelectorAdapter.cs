using System;
using System.Globalization;
using System.Reflection;
using FM27Access.Navigation;
using HarmonyLib;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class DateSelectorAdapter : PluginAdapterBase
{
    private static PropertyInfo _floatingWidgetProperty;
    private static PropertyInfo _clickableProperty;
    private static PropertyInfo _monthDropdownProperty;
    private static PropertyInfo _yearDropdownProperty;
    private static PropertyInfo _confirmButtonProperty;
    private static PropertyInfo _cancelButtonProperty;
    private static PropertyInfo _selectedDateTimeProperty;
    private static PropertyInfo _controllerProperty;
    private static PropertyInfo _isFloatingWidgetProperty;

    private static MethodInfo _clickableInvokeMethod;
    private static MethodInfo _floatingElementHideMethod;
    private static PropertyInfo _floatingElementIsVisibleProperty;

    private static PropertyInfo _dropdownSelectedIndexProperty;
    private static MethodInfo _triggerSelectionEventMethod;

    private static PropertyInfo _nextMonthButtonProperty;
    private static PropertyInfo _previousMonthButtonProperty;
    private static PropertyInfo _controllerYearsProperty;
    private static PropertyInfo _controllerMonthsProperty;

    public DateSelectorAdapter(VisualElement element) : base(element)
    {
        EnsureReflection();
    }

    public override string Role => "date picker";
    public override bool CanActivate => true;

    internal string FieldLabel => FindFieldLabel();

    public override string GetLabel()
    {
        return FindChildText() ?? Element.name;
    }

    public override string GetState() => "";

    // The optional-date caption is the native unset state. The widget's
    // DateTime can still be MinValue until its lazy controller is opened.
    internal bool IsUnset
    {
        get
        {
            var caption = FindByName(Element, "dateNotSelectedText");
            return caption != null && ElementBridge.IsVisibleInHierarchy(caption);
        }
    }

    internal bool NeedsCalendarInitialization
        => ((FM.UI.Widgets.DateSelectorWidget)GetWidget()).m_controller?.DateTimeCollection == null;

    internal bool TryGetCalendarView(out int day, out int month, out int year)
    {
        if (!IsUnset && TryGetSelectedDateTime(out day, out month, out year)) return true;
        day = month = year = 0;
        var controller = GetController();
        var dates = controller?.DateTimeCollection;
        if (dates == null) return false;
        // Use the actual initialized grid/range, never today's wall-clock date
        // or year 1. A recall window can legitimately begin next January.
        for (int i = 0; i < dates.Count; i++)
        {
            var date = dates[i];
            if (!controller.IsDateEnabled(date)) continue;
            day = date.Day; month = date.Month; year = date.Year;
            return true;
        }
        return false;
    }

    public override void Activate()
    {
        // Activation is handled by AccessibilityBehaviour entering date picker mode
    }

    public bool IsFloating()
    {
        var widget = GetWidget();
        return (bool)_isFloatingWidgetProperty.GetValue(widget);
    }

    public void ShowPopup()
    {
        var widget = GetWidget();
        var clickable = _clickableProperty.GetValue(widget);
        var evt = ClickEvent.GetPooled();
        _clickableInvokeMethod.Invoke(clickable, new object[] { evt });
    }

    public void HidePopup()
    {
        var widget = GetWidget();
        var floatingElement = _floatingWidgetProperty.GetValue(widget);
        _floatingElementHideMethod.Invoke(floatingElement, null);
    }

    public bool IsPopupVisible()
    {
        var widget = GetWidget();
        var floatingElement = _floatingWidgetProperty.GetValue(widget);
        if (floatingElement == null) return false;
        return (bool)_floatingElementIsVisibleProperty.GetValue(floatingElement);
    }

    public bool TryGetSelectedDateTime(out int day, out int month, out int year)
    {
        day = 1;
        month = 1;
        year = 2000;
        try
        {
            // m_selectedDateTime is an IL2CPP DateTime; reflection boxes it as an
            // Il2CppSystem.DateTime. Casting to the managed System.DateTime throws
            // InvalidCastException (which silently forced the ambiguous display-text parse and
            // transposed day/month), so read the IL2CPP value's components directly.
            var widget = GetWidget();
            var dt = (Il2CppSystem.DateTime)_selectedDateTimeProperty.GetValue(widget);
            day = dt.Day;
            month = dt.Month;
            year = dt.Year;
            return true;
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[DateSelector] Failed to read m_selectedDateTime: {e.GetType().Name}");
            return false;
        }
    }

    // Whether the controller permits this date. Mirrors the game's own validity rule
    // (DateSelectorControllerBase.IsDateEnabled), so a month/year the picker disables is rejected
    // up front. The caller must pass a real calendar date — the Il2CppSystem.DateTime constructor
    // throws on an impossible day, exactly as System.DateTime does. A null controller means no
    // constraints, so the date is permitted.
    public bool IsDateEnabled(int year, int month, int day)
    {
        var controller = GetController();
        if (controller == null) return true;
        return controller.IsDateEnabled(new Il2CppSystem.DateTime(year, month, day));
    }

    // Per-day availability for a whole month, 1-indexed (length day-count + 1), for the calendar
    // grid. Fetches the controller once. A null controller means every day is permitted.
    public bool[] EnabledDays(int year, int month)
    {
        int days = DateTime.DaysInMonth(year, month);
        var result = new bool[days + 1];
        var controller = GetController();
        for (int d = 1; d <= days; d++)
            result[d] = controller == null || controller.IsDateEnabled(new Il2CppSystem.DateTime(year, month, d));
        return result;
    }

    // Whether a month has any selectable day — used to enable/disable the calendar's prev/next
    // year and month buttons. Years outside the real calendar range are never reachable.
    public bool MonthHasAnyEnabled(int year, int month)
    {
        if (year < 1 || year > 9999) return false;
        var controller = GetController();
        if (controller == null) return true;
        int days = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= days; d++)
            if (controller.IsDateEnabled(new Il2CppSystem.DateTime(year, month, d))) return true;
        return false;
    }

    private FM.UI.Widgets.DateSelectorControllerBase GetController()
    {
        var widget = GetWidget();
        var controller = _controllerProperty.GetValue(widget);
        return controller == null ? null : (FM.UI.Widgets.DateSelectorControllerBase)controller;
    }

    public bool TryParseDisplayedDate(out int day, out int month, out int year)
    {
        day = 1;
        month = 1;
        year = 2000;
        var text = FindChildText();
        if (string.IsNullOrEmpty(text)) return false;

        // Try common date formats
        if (DateTime.TryParseExact(text, new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d/M/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            day = parsed.Day;
            month = parsed.Month;
            year = parsed.Year;
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            day = parsed.Day;
            month = parsed.Month;
            year = parsed.Year;
            return true;
        }

        Speech.Log.Warn($"[DateSelector] Could not parse displayed date text: \"{text}\"");
        return false;
    }

    public void SetYearDropdown(int yearIndex)
    {
        var widget = GetWidget();
        var yearDropdown = _yearDropdownProperty.GetValue(widget);
        var currentIdx = (int)_dropdownSelectedIndexProperty.GetValue(yearDropdown);
        Speech.Log.Info($"[DateSelector] SetYearDropdown: currentIdx={currentIdx}, targetIdx={yearIndex}");
        _triggerSelectionEventMethod.Invoke(yearDropdown, new object[] { yearIndex });
        var afterIdx = (int)_dropdownSelectedIndexProperty.GetValue(yearDropdown);
        Speech.Log.Info($"[DateSelector] SetYearDropdown: afterIdx={afterIdx}");
    }

    // Presses the grid cell holding an exact date. The grid is a full wall calendar — five or six
    // weeks of cells including the previous and next month's overflow days — and every cell carries
    // the date it is bound to, so matching that date is unambiguous where matching the printed day
    // number is not (two cells in one grid print "1"). It also removes a frame-timing dependency:
    // stepping the month swaps each cell's bound date immediately but repaints its printed text
    // only on the following layout pass, so a text read can still be showing the month just left.
    //
    // False means the date is not on the grid at all (the wrong month is showing) or its cell is
    // disabled. Pressing nothing beats pressing the wrong day; the caller reports the refusal.
    public bool SelectDate(int year, int month, int day)
    {
        var widget = (FM.UI.Widgets.DateSelectorWidget)GetWidget();
        var grid = widget.m_dateGrid;
        var dates = widget.m_controller?.DateTimeCollection;
        if(grid==null||dates==null)return false;
        for (int i = 0; i < dates.Count; i++)
        {
            var cellDate = dates[i];
            if (cellDate.Year != year || cellDate.Month != month || cellDate.Day != day) continue;
            if(!widget.m_controller.IsDateEnabled(cellDate))return false;
            // FM27's DateGrid uses indices into the controller's DateTimeCollection.
            // This is the native day-selection entry, independent of recycled day-cell geometry.
            widget.OnGridDaySelected(i);
            Speech.Log.Info($"[DateSelector] Selected {DateFieldText.ToIso(year, month, day)} at grid index {i}.");
            return true;
        }

        Speech.Log.Warn($"[DateSelector] {DateFieldText.ToIso(year, month, day)} is not on the {dates.Count}-cell grid.");
        return false;
    }

    // The next/previous-month presses that reach targetMonth from the month dropdown's current
    // position. The dropdown holds only the months the controller allows for the selected year, so
    // this reads that set rather than assuming a full January..December list; see
    // DateFieldText.TryGetMonthStepDelta. Call after the year has settled — changing the year
    // refilters the month set.
    public bool TryGetMonthStepDelta(int targetMonth, out int delta)
        => DateFieldText.TryGetMonthStepDelta(
            ReadControllerMonths(), GetCurrentMonthDropdownIndex(), targetMonth, out delta);

    public void ClickNextMonth()
    {
        var widget = GetWidget();
        var btn = _nextMonthButtonProperty.GetValue(widget);
        var siBtn = new SI.Bindable.SIButton(((Il2CppSystem.Object)btn).Pointer);
        siBtn.SimulateButtonClick();
    }

    public void ClickPreviousMonth()
    {
        var widget = GetWidget();
        var btn = _previousMonthButtonProperty.GetValue(widget);
        var siBtn = new SI.Bindable.SIButton(((Il2CppSystem.Object)btn).Pointer);
        siBtn.SimulateButtonClick();
    }

    public int GetCurrentMonthDropdownIndex()
    {
        var widget = GetWidget();
        var monthDropdown = _monthDropdownProperty.GetValue(widget);
        return (int)_dropdownSelectedIndexProperty.GetValue(monthDropdown);
    }

    public void ClickConfirm()
    {
        var widget = GetWidget();
        var confirmButtonObj = _confirmButtonProperty.GetValue(widget);
        if (confirmButtonObj == null)
        {
            Speech.Log.Warn("[DateSelector] m_confirmButton is null.");
            return;
        }
        var confirmButton = new SI.Bindable.SIButton(((Il2CppSystem.Object)confirmButtonObj).Pointer);
        Speech.Log.Info($"[DateSelector] ClickConfirm: button name='{confirmButton.name}'");
        confirmButton.SimulateButtonClick();
        Speech.Log.Info("[DateSelector] ClickConfirm: SimulateButtonClick called.");
    }

    public void ClickCancel()
    {
        var widget = GetWidget();
        var cancelButtonObj = _cancelButtonProperty.GetValue(widget);
        if (cancelButtonObj == null)
        {
            Speech.Log.Warn("[DateSelector] m_cancelButton is null.");
            return;
        }
        var cancelButton = new SI.Bindable.SIButton(((Il2CppSystem.Object)cancelButtonObj).Pointer);
        cancelButton.SimulateButtonClick();
    }

    public System.Collections.Generic.List<int> ReadControllerYears()
    {
        var result = new System.Collections.Generic.List<int>();
        try
        {
            var widget = GetWidget();
            var controller = _controllerProperty.GetValue(widget);
            if (controller == null) return result;

            var yearsObj = _controllerYearsProperty.GetValue(controller);
            var years = (Il2CppSystem.Collections.Generic.List<int>)yearsObj;
            for (int i = 0; i < years.Count; i++)
                result.Add(years[i]);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[DateSelector] Failed to read controller years: {e.GetType().Name}");
        }
        return result;
    }

    public System.Collections.Generic.List<int> ReadControllerMonths()
    {
        var result = new System.Collections.Generic.List<int>();
        try
        {
            var widget = GetWidget();
            var controller = _controllerProperty.GetValue(widget);
            if (controller == null) return result;

            var monthsObj = _controllerMonthsProperty.GetValue(controller);
            var months = (Il2CppSystem.Collections.Generic.List<int>)monthsObj;
            for (int i = 0; i < months.Count; i++)
                result.Add(months[i]);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[DateSelector] Failed to read controller months: {e.GetType().Name}");
        }
        return result;
    }

    private object GetWidget()
    {
        return new FM.UI.Widgets.DateSelectorWidget(Element.Pointer);
    }

    private static bool _initialized;

    private static void EnsureReflection()
    {
        if (_initialized) return;
        _initialized = true;

        var widgetType = typeof(FM.UI.Widgets.DateSelectorWidget);
        var navType = typeof(SI.UI.NavigatableVisualElement);
        var dropdownType = typeof(SI.Bindable.SIDropdown);

        // m_clickable is on NavigatableVisualElement
        _clickableProperty = navType.GetProperty("m_clickable");
        if (_clickableProperty == null)
        {
            Speech.Log.Error("NavigatableVisualElement.m_clickable property not found.");
            throw new MissingMemberException("NavigatableVisualElement", "m_clickable");
        }

        // Resolve Clickable.Invoke(EventBase)
        var clickableType = _clickableProperty.PropertyType;
        _clickableInvokeMethod = clickableType.GetMethod("Invoke",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new Type[] { typeof(EventBase) }, null);
        if (_clickableInvokeMethod == null)
        {
            Speech.Log.Error("SIClickable.Invoke(EventBase) method not found.");
            throw new MissingMethodException("SIClickable", "Invoke");
        }

        // DateSelectorWidget fields
        _floatingWidgetProperty = widgetType.GetProperty("m_floatingWidget");
        RequireProperty(_floatingWidgetProperty, "DateSelectorWidget", "m_floatingWidget");

        _monthDropdownProperty = widgetType.GetProperty("m_monthDropdown");
        RequireProperty(_monthDropdownProperty, "DateSelectorWidget", "m_monthDropdown");

        _yearDropdownProperty = widgetType.GetProperty("m_yearDropdown");
        RequireProperty(_yearDropdownProperty, "DateSelectorWidget", "m_yearDropdown");

        _confirmButtonProperty = widgetType.GetProperty("m_confirmButton");
        RequireProperty(_confirmButtonProperty, "DateSelectorWidget", "m_confirmButton");

        _cancelButtonProperty = widgetType.GetProperty("m_cancelButton");
        RequireProperty(_cancelButtonProperty, "DateSelectorWidget", "m_cancelButton");

        _nextMonthButtonProperty = widgetType.GetProperty("m_nextMonthButton");
        RequireProperty(_nextMonthButtonProperty, "DateSelectorWidget", "m_nextMonthButton");

        _previousMonthButtonProperty = widgetType.GetProperty("m_previousMonthButton");
        RequireProperty(_previousMonthButtonProperty, "DateSelectorWidget", "m_previousMonthButton");

        _selectedDateTimeProperty = widgetType.GetProperty("m_selectedDateTime");
        RequireProperty(_selectedDateTimeProperty, "DateSelectorWidget", "m_selectedDateTime");

        _controllerProperty = widgetType.GetProperty("m_controller");
        RequireProperty(_controllerProperty, "DateSelectorWidget", "m_controller");

        _isFloatingWidgetProperty = AccessTools.Property(widgetType, "IsFloatingWidget");
        RequireProperty(_isFloatingWidgetProperty, "DateSelectorWidget", "IsFloatingWidget");

        // FloatingElement methods
        var feType = _floatingWidgetProperty.PropertyType;
        _floatingElementHideMethod = feType.GetMethod("Hide",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null);
        if (_floatingElementHideMethod == null)
        {
            Speech.Log.Error("FloatingElement.Hide() method not found.");
            throw new MissingMethodException("FloatingElement", "Hide");
        }

        _floatingElementIsVisibleProperty = feType.GetProperty("IsVisible");
        RequireProperty(_floatingElementIsVisibleProperty, "FloatingElement", "IsVisible");

        // SIDropdown reflection (the year dropdown is set by index; the month is stepped via the
        // next/previous-month buttons, so only SelectedIndex and TriggerSelectionEvent are needed).
        _dropdownSelectedIndexProperty = AccessTools.Property(dropdownType, "SelectedIndex");
        RequireProperty(_dropdownSelectedIndexProperty, "SIDropdown", "SelectedIndex");

        _triggerSelectionEventMethod = AccessTools.Method(dropdownType, "TriggerSelectionEvent", new Type[] { typeof(int) });
        if (_triggerSelectionEventMethod == null)
        {
            Speech.Log.Error("SIDropdown.TriggerSelectionEvent(int) method not found.");
            throw new MissingMethodException("SIDropdown", "TriggerSelectionEvent");
        }

        // Controller properties
        var controllerType = typeof(FM.UI.Widgets.DateSelectorControllerBase);
        _controllerYearsProperty = AccessTools.Property(controllerType, "Years");
        _controllerMonthsProperty = AccessTools.Property(controllerType, "Months");
        // These are non-fatal — controller access is best-effort
        if (_controllerYearsProperty == null)
            Speech.Log.Warn("[DateSelector] DateSelectorControllerBase.Years property not found. Year constraints unavailable.");
        if (_controllerMonthsProperty == null)
            Speech.Log.Warn("[DateSelector] DateSelectorControllerBase.Months property not found. Month constraints unavailable.");
    }

    private static void RequireProperty(PropertyInfo prop, string typeName, string propName)
    {
        if (prop == null)
        {
            Speech.Log.Error($"{typeName}.{propName} property not found.");
            throw new MissingMemberException(typeName, propName);
        }
    }
}

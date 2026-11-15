using System;
using System.Collections.Generic;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Server-side date application: the HTML surface's port of the speech DatePickerHandler's
/// apply phase. The HTML side has no year/month/day wizard — the calendar widget picks the whole
/// target date — so this is only the application machinery: validate the date, open the popup,
/// drive the year dropdown, step the month, click the day, confirm, then verify what the game
/// actually shows so the result is never a lie.
///
/// Like DropdownSessionManager, all game-object access runs on the main thread via
/// MainThreadDispatcher.Invoke; the request thread orchestrates the multi-frame waits with
/// sleeps between Invokes, standing in for the speech handler's frame counting. One apply runs
/// to completion per request and always leaves the popup closed.
/// </summary>
internal static class DateSelectorSession
{
    private const int VisibleTimeoutMs = 1000; // speech handler: 30 frames
    private const int FrameMs = 16;
    private const int PopupSettleMs = 120;     // popup visible -> fields readable
    private const int YearSettleMs = 120;      // year change refilters the month set
    private const int MonthStepSettleMs = 60;  // one next/prev-month click
    private const int DaySettleMs = 60;        // day grid press registers
    private const int ConfirmSettleMs = 200;   // confirm propagates into the bound value

    internal static string PrepareCalendar(string id, out DateSelectorAdapter adapter, out bool opened, out Func<string> validation, string expectedScreen = null, string expectedContext = null)
    {
        DateSelectorAdapter resolved = null;
        bool needsOpen = false;
        Func<string> guard = null;
        bool retry;
        long started = Environment.TickCount64;
        string error;
        do
        {
            retry = false;
            error = MainThreadDispatcher.Invoke<string>(() =>
        {
                var stale=ServerHandles.ValidateScreen(expectedScreen);if(stale!=null)return stale;
                resolved = ServerHandles.ResolveDateHandle(id, out var err, expectedContext);
                if (resolved == null)
            {
                    retry = ServerHandles.DispatchesAcrossFrames(id) && err == "That condition date is no longer available. Refresh.";
                    return err;
                }
                guard = ServerHandles.CaptureConditionValidation(id);
                needsOpen = resolved.NeedsCalendarInitialization;
                if (needsOpen) resolved.ShowPopup();
                return null;
            });
            if (!retry || Environment.TickCount64 - started >= 400) break;
            Thread.Sleep(FrameMs);
        } while (true);
        adapter = resolved;
        opened = needsOpen;
        validation = guard;
        if (error != null || !needsOpen) return error;
        if (!WaitFor(() => resolved.IsPopupVisible(), VisibleTimeoutMs))
            return "The date picker did not open.";
        Thread.Sleep(PopupSettleMs);
        return null;
    }

    internal static void ClosePreparedCalendar(DateSelectorAdapter adapter, string expectedScreen = null, Func<string> validation = null)
    {
        if (adapter == null) return;
        MainThreadDispatcher.Invoke<object>(() =>
        {
            if (ServerHandles.ValidateScreen(expectedScreen) == null && validation?.Invoke() == null && adapter.IsPopupVisible()) adapter.HidePopup();
            return null;
        });
    }

    public static string Apply(string id, string typed, string expectedScreen = null, string expectedContext = null)
    {
        if (!DateFieldText.TryParseDmy(typed, out int year, out int month, out int day))
            return "That date is not valid.";

        // Calendar validity first (pure): rejects Sept 31 and friends before any game access, and
        // guarantees the date the availability check builds an Il2CppSystem.DateTime from is real.
        if (!DateFieldText.IsRealCalendarDate(year, month, day))
            return "That date does not exist.";

        DateSelectorAdapter adapter = null;
        Func<string> conditionGuard = null, valueGuard = null;
        try
        {
            bool retry;
            long started = Environment.TickCount64;
            string err;
            do
            {
                retry = false;
                err = MainThreadDispatcher.Invoke<string>(() =>
            {
                    var stale=ServerHandles.ValidateScreen(expectedScreen);if(stale!=null)return stale;
                    adapter = ServerHandles.ResolveDateHandle(id, out var e, expectedContext);
                    if (adapter == null)
                {
                        retry = ServerHandles.DispatchesAcrossFrames(id) && e == "That condition date is no longer available. Refresh.";
                        return e;
                    }
                    conditionGuard = ServerHandles.CaptureConditionValidation(id);
                    valueGuard = ServerHandles.CaptureConditionValueValidation(id);
                    DropdownSessionManager.CloseCurrentOnMainThread();
                    // Reject a controller-disabled month/year up front, with no popup flicker. This is
                    // a pre-check for a crisp message, not the correctness gate (the post-apply verify
                    // is): if it throws, warn and proceed so the verify still decides truthfully.
                    try
                {
                        if (!adapter.IsDateEnabled(year, month, day))
                            return "That date is not available.";
                    }
                    catch (Exception ex)
                {
                        Speech.Log.Warn($"[DateField] IsDateEnabled threw, proceeding to apply: {ex.GetType().Name}");
                    }
                    adapter.ShowPopup();
                    return null;
                });
                if (!retry || Environment.TickCount64 - started >= 400) break;
                Thread.Sleep(FrameMs);
            } while (true);
            if (err != null) return err;

            if (!WaitFor(() => adapter.IsPopupVisible(), VisibleTimeoutMs))
                return FailAndClose(adapter, "The date picker did not open.", expectedScreen, conditionGuard);
            Thread.Sleep(PopupSettleMs);

            err = MainThreadDispatcher.Invoke<string>(() =>
            {
                var stale = ServerHandles.ValidateScreen(expectedScreen) ?? conditionGuard?.Invoke();
                if (stale != null) return stale;
                var years = adapter.ReadControllerYears();
                if (years.Count == 0) return null; // unconstrained: leave the year dropdown as-is
                int idx = years.IndexOf(year);
                if (idx < 0) return $"{year} is not selectable here.";
                adapter.SetYearDropdown(idx);
                return null;
            });
            if (err != null) return FailAndClose(adapter, err, expectedScreen, conditionGuard);
            Thread.Sleep(YearSettleMs);

            // Month is stepped by POSITION in the month dropdown, which holds only the months the
            // controller allows for the year just selected — a friendly window covering June to
            // August is a three-item list. Read it here, after the year settle, because changing
            // the year refilters the set. Mirrors the speech apply phase (DatePickerHandler).
            int steps = 0;
            err = MainThreadDispatcher.Invoke<string>(() =>
                ServerHandles.ValidateScreen(expectedScreen)
                    ?? conditionGuard?.Invoke()
                    ?? (adapter.TryGetMonthStepDelta(month, out steps) ? null : "That month is not selectable here."));
            if (err != null) return FailAndClose(adapter, err, expectedScreen, conditionGuard);

            bool forward = steps > 0;
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                err = MainThreadDispatcher.Invoke<string>(() =>
                {
                    var stale = ServerHandles.ValidateScreen(expectedScreen) ?? conditionGuard?.Invoke();
                    if (stale != null) return stale;
                    if (forward) adapter.ClickNextMonth(); else adapter.ClickPreviousMonth();
                    return null;
                });
                if (err != null) return FailAndClose(adapter, err, expectedScreen, conditionGuard);
                Thread.Sleep(MonthStepSettleMs);
            }

            err = MainThreadDispatcher.Invoke<string>(() =>
                ServerHandles.ValidateScreen(expectedScreen)
                    ?? conditionGuard?.Invoke()
                    ?? (adapter.SelectDate(year, month, day) ? null : "That date is not available."));
            if (err != null) return FailAndClose(adapter, err, expectedScreen, conditionGuard);
            Thread.Sleep(DaySettleMs);

            err = MainThreadDispatcher.Invoke<string>(() =>
            {
                var stale = ServerHandles.ValidateScreen(expectedScreen) ?? conditionGuard?.Invoke();
                if (stale != null) return stale;
                adapter.ClickConfirm(); return null;
            });
            if (err != null) return FailAndClose(adapter, err, expectedScreen, conditionGuard);
            Thread.Sleep(ConfirmSettleMs);

            return MainThreadDispatcher.Invoke<string>(() =>
            {
                if (ServerHandles.ValidateScreen(expectedScreen) != null)
                    return "The screen changed before the date change could be confirmed. Check the current game state.";
                var changed = valueGuard?.Invoke();
                if (changed != null) return changed;
                bool read = adapter.TryGetSelectedDateTime(out int gd, out int gm, out int gy);
                bool ok = read && gd == day && gm == month && gy == year;
                if (!ok)
                {
                    string shown = read ? DateFieldText.ToIso(gy, gm, gd) : "an unreadable value";
                    Speech.Log.Warn($"[DateField] Apply mismatch: wanted {DateFieldText.ToIso(year, month, day)}, picker shows {shown}.");
                }
                if (adapter.IsPopupVisible()) adapter.HidePopup();
                return ok ? "Set to " + adapter.GetLabel() : "Could not set that date.";
            });
        }
        catch (Exception ex)
        {
            return FailAndClose(adapter, ServerText.Failure("Date change failed.", ex), expectedScreen, conditionGuard);
        }
    }

    private static string FailAndClose(DateSelectorAdapter adapter, string message, string expectedScreen, Func<string> validation = null)
    {
        try
        {
            MainThreadDispatcher.Invoke<object>(() =>
            {
                if (adapter != null && ServerHandles.ValidateScreen(expectedScreen) == null && validation?.Invoke() == null && adapter.IsPopupVisible()) adapter.ClickCancel();
                return null;
            });
        }
        catch (Exception ex) { ServerText.LogBackground("date close after failure failed: " + ex.Message); }
        return message;
    }

    private static bool WaitFor(Func<bool> conditionOnMainThread, int timeoutMs)
    {
        // Wall-clock, not sleep-count: each Invoke costs a frame on top of the sleep, so charging
        // only the sleep stretched the nominal timeout to ~2x or worse (see DropdownSession.WaitFor).
        long start = Environment.TickCount64;
        while (true)
        {
            if (MainThreadDispatcher.Invoke(conditionOnMainThread)) return true;
            if (Environment.TickCount64 - start >= timeoutMs) return false;
            Thread.Sleep(FrameMs);
        }
    }
}

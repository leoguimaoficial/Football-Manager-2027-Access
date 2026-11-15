using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The date selector as an HTML-surface feature: a disclosure button showing the current date that
/// opens an inline calendar panel. The calendar is computed here from the real-world calendar
/// (CalendarGrid) plus the controller's per-day availability; navigating year/month just re-renders
/// a grid (no game popup), and picking a day applies it through DateSelectorSession (which drives
/// the real DateSelectorWidget popup invisibly and verifies the result).
///
/// Availability is asked of the controller one day at a time, so the editor serves any controller
/// whatever shape its allowed set has: a contiguous range (a birth date) and a sparse discrete set
/// (Arrange Friendly's ~47 permitted match days) render identically, with disallowed days greyed
/// out. Navigation uses the controller's real year/day availability to reach the next permitted
/// month or year, including across empty intervening periods, retaining the original grid HTML.
/// </summary>
internal static class DateField
{
    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        routes["/date/set"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (DateSelectorSession.Apply(Query.Str(q, "id"), Query.Str(q, "value"), Query.Str(q, "screen"), Query.Str(q, "context")), null),
        };
        routes["/date/cal"] = new Route
        {
            ContentType = "text/html",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (Cal(Query.Str(q, "id"), Query.Int(q, "year"), Query.Int(q, "month"), Query.Str(q, "screen"), Query.Str(q, "context")), null),
        };
    }

    // Builds the calendar panel fragment for a month/year (defaulting to the current selection's
    // month when year/month are absent). Runs on the main thread: reads the live selected date and
    // probes the controller for per-day availability and the nav-button bounds.
    private static string Cal(string id, int year, int month, string expectedScreen, string expectedContext)
    {
        Adapters.DateSelectorAdapter prepared = null;
        bool opened = false;
        System.Func<string> validation = null;
        try
        {
            var error = DateSelectorSession.PrepareCalendar(id, out prepared, out opened, out validation, expectedScreen, expectedContext);
            if (error != null) return error;
            return MainThreadDispatcher.Invoke(() =>
            {
                var stale = ServerHandles.ValidateScreen(expectedScreen);
                if (stale != null) return stale;
                var date = ServerHandles.ResolveDateHandle(id, out var err, expectedContext);
                if (date == null) return err;
                if (!date.TryGetCalendarView(out int selDay, out int selMonth, out int selYear))
                    return "Could not read the current date.";

                int vy = year > 0 ? year : selYear;
                int vm = (month >= 1 && month <= 12) ? month : selMonth;
                if (vy < 1 || vy > 9999) return "That year is out of range.";

                var enabled = date.EnabledDays(vy, vm);
                var years = date.ReadControllerYears();
                var observed = new Dictionary<(int, int), bool>();
                bool Available(int y, int m)
                {
                    if (!observed.TryGetValue((y, m), out var any))
                        observed[(y, m)] = any = date.MonthHasAnyEnabled(y, m);
                    return any;
                }
                var py = DateCalendarNavigation.Find(vy, vm, -1, years, Available, byYear: true);
                var ny = DateCalendarNavigation.Find(vy, vm, 1, years, Available, byYear: true);
                var pm = DateCalendarNavigation.Find(vy, vm, -1, years, Available);
                var nm = DateCalendarNavigation.Find(vy, vm, 1, years, Available);
                int mark = !date.IsUnset && (vy == selYear && vm == selMonth) ? selDay : 0;

                string Target((int Year, int Month)? target) => target.HasValue ? target.Value.Year + "/" + target.Value.Month : "";
                return "<div data-cal-nav data-py=\"" + Target(py) + "\" data-ny=\"" + Target(ny)
                    + "\" data-pm=\"" + Target(pm) + "\" data-nm=\"" + Target(nm) + "\">"
                    + CalendarGrid.BuildPanel(id, vy, vm, enabled, mark, py.HasValue, ny.HasValue, pm.HasValue, nm.HasValue) + "</div>";
            });
        }
        catch (System.Exception ex)
        {
            return ServerText.Failure("Calendar unavailable.", ex);
        }
        finally
        {
            if (opened) DateSelectorSession.ClosePreparedCalendar(prepared, expectedScreen, validation);
        }
    }

    public static void Render(StringBuilder sb, Adapters.DateSelectorAdapter date)
    {
        // Editable only when the structured date reads cleanly. Seed strictly from the structured
        // DateTime, never the displayed text (a numeric date like "10/1/1995" is format-ambiguous —
        // the game's own date format is a user preference — and would transpose day/month). If the
        // structured read fails, show the live date text read-only — honest omission of the editor.
        // Native-disabled optional dates can hold DateTime.MinValue while the
        // game displays a real caption such as No Expiry Date. Keep that caption
        // read-only instead of exposing an active 01/01/0001 calendar button.
        if (ServerText.IsDisabled(date) || !date.TryGetSelectedDateTime(out int day, out int month, out int year))
        {
            string roId = LiveValues.RegisterReadOnly(date);
            sb.Append("<p id=\"").Append(roId).Append("\">")
              .Append(ServerText.Escape(DisplayText.Of(date))).Append("</p>");
            return;
        }

        // A disclosure: the button shows the current date (its accessible name) and aria-expanded
        // signals it opens the calendar; the empty panel div is filled by /date/cal on open. The
        // field's native label also names the disclosure, so control navigation retains context.
        string id = ServerHandles.Register(new DateFieldHandle { Date = date }, date);
        string dmy = date.IsUnset ? date.GetLabel() : DateFieldText.ToDmy(year, month, day);
        RenderControl(sb, id, dmy, date.FieldLabel);
    }

    // Reuse the identical disclosure/calendar for virtualized condition dates;
    // their registered handle realizes and validates the native owning rule.
    internal static void RenderControl(StringBuilder sb, string id, string dmy, string fieldLabel, string context = null, bool paragraph = true)
    {
        if (paragraph) sb.Append("<p>");
        sb.Append("<button type=\"button\" id=\"").Append(id).Append('"');
        if (context != null) sb.Append(" data-fm-command-context=\"").Append(context).Append('"');
        if (!string.IsNullOrWhiteSpace(fieldLabel))
            sb.Append(" aria-label=\"").Append(ServerText.EscapeText(fieldLabel + ": " + dmy)).Append('"');
        sb.Append(" aria-expanded=\"false\" onclick=\"fmdate('").Append(id).Append("')\">")
          .Append(ServerText.Escape(dmy)).Append("</button>");
        if (paragraph) sb.Append("</p>");
        sb.Append("<div id=\"").Append(id).Append("-panel\" class=\"fm-date-panel\"></div>");

    }
}

internal sealed class DateFieldRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
        => ServerText.SafeRole(el) == "date picker" && el is Adapters.DateSelectorAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
        => DateField.Render(sb, (Adapters.DateSelectorAdapter)el);
}

// Resolves the ordinary native date field through the existing session.
internal sealed class DateFieldHandle : ServerHandles.Handle
{
    public Adapters.DateSelectorAdapter Date;
    public override bool TryValidate(out string error)
    {
        error = null;
        var be = Date.BackingElement;
        if (be.panel == null || !ElementBridge.IsVisibleInHierarchy(be))
        {
            error = "That date field is no longer on screen. Refresh.";
            return false;
        }
        if (ServerText.IsDisabled(Date))
        {
            error = "That date field is disabled in the game.";
            return false;
        }
        return true;
    }
    public override bool TryGetDateAdapter(out Adapters.DateSelectorAdapter adapter, out string error)
    {
        adapter = Date;
        error = null;
        return true;
    }
}

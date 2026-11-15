using System;
using System.Collections.Generic;
using System.Threading;
using FM27Access.Plugin.Contexts;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads every responsibility group of the active Delegation section on the request (background)
/// thread, filling a walked <see cref="ScreenWalker.ResponsibilitiesTableItem"/>'s Rows. The
/// group list virtualises, so only the groups near the scroll position exist in the tree at any
/// moment; this scrolls each one in and waits a frame or two for its texts to bind, which a
/// single blocking main-thread read cannot do. Same shape as StreamedTableLoader: every game
/// touch is marshalled through <see cref="MainThreadDispatcher"/> and the per-poll wait is a real
/// <see cref="Thread.Sleep"/>, so game frames advance between polls.
/// </summary>
internal static class ResponsibilitiesLoader
{
    private const int StepMs = 16;              // ~one frame
    private const int GroupReadyBudgetMs = 500; // scroll -> the group's name text has bound
    private const int SweepBudgetMs = 6000;     // overall cap (< the route's main-thread timeouts)
    internal const int MaxGroups = 40;          // runaway guard; real sections hold under ten

    // The sweep drives the list's scroll position across frames, which is shared mutable UI state:
    // two sweeps at once (an agent /body racing the companion's SSE re-render) would scroll each
    // other's groups out mid-read. Serialise, exactly as the streamed-table read does. Held only by
    // background request threads, so a waiting reader queues and then reads fresh.
    private static readonly object _readLock = new object();

    /// <summary>
    /// The Overview tab's duty list for the selected staff member. Same sweep, far simpler: the
    /// rows carry no controls, so each is read as text at its own index and nothing needs
    /// re-realising afterwards.
    /// </summary>
    internal static void LoadStaffDuties(ScreenWalker.StaffDutiesTableItem item)
    {
        var ctx = item.Context;
        try
        {
            lock (_readLock)
            {
                long deadline = Environment.TickCount64 + SweepBudgetMs;
                int total = MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    return ctx.StaffDutyCount();
                });
                if (total < 0) { item.LoadFailed = true; return; }

                var rows = new List<ResponsibilitiesContext.StaffDuty>(total);
                for (int i = 0; i < total; i++)
                {
                    ResponsibilitiesContext.StaffDuty row = null;
                    long rowDeadline = Environment.TickCount64 + GroupReadyBudgetMs;
                    while (true)
                    {
                        row = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            return ctx.ReadStaffDuty(i);
                        });
                        if (row != null) break;
                        if (Environment.TickCount64 >= rowDeadline || Environment.TickCount64 >= deadline) break;
                        Thread.Sleep(StepMs);
                    }
                    if (row == null) item.Truncated = true;
                    else rows.Add(row);
                    if (Environment.TickCount64 >= deadline)
                    {
                        if (i < total - 1) item.Truncated = true;
                        break;
                    }
                }
                item.Duties = rows;
            }
        }
        catch (ContextMenuGuard.AbortException)
        {
            ServerText.LogBackground("[ResponsibilitiesLoader] staff duty read paused: context menu active");
            item.Duties = new List<ResponsibilitiesContext.StaffDuty>();
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"[ResponsibilitiesLoader] staff duty sweep failed: {ex.Message}");
            item.LoadFailed = true;
        }
    }

    internal static void Load(ScreenWalker.ResponsibilitiesTableItem item)
    {
        var ctx = item.Context;
        try
        {
            lock (_readLock)
            {
                long deadline = Environment.TickCount64 + SweepBudgetMs;

                int total = MainThreadDispatcher.Invoke(() =>
                {
                    if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                    return ctx.GroupCount();
                });
                if (total < 0)
                {
                    item.LoadFailed = true;
                    return;
                }
                if (total > MaxGroups) { item.Truncated = true; total = MaxGroups; }

                var groups = new List<ResponsibilitiesContext.Group>(total);
                for (int g = 0; g < total; g++)
                {
                    long groupDeadline = Environment.TickCount64 + GroupReadyBudgetMs;
                    while (true)
                    {
                        bool ready = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            return ctx.ScrollToGroupReady(g);
                        });
                        if (ready) break;
                        if (Environment.TickCount64 >= groupDeadline || Environment.TickCount64 >= deadline) break;
                        Thread.Sleep(StepMs);
                    }

                    // Re-read until the group's people have bound too: a group scrolled in this
                    // frame reports its name before its assignee dropdowns fill, and accepting that
                    // first read would record every duty as unassigned.
                    ResponsibilitiesContext.Group group = null;
                    long bindDeadline = Environment.TickCount64 + GroupReadyBudgetMs;
                    while (true)
                    {
                        group = MainThreadDispatcher.Invoke(() =>
                        {
                            if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                            return ctx.ReadGroup(g);
                        });
                        if (group == null) break;
                        if (ResponsibilitiesContext.AssigneesBound(group)
                            && ResponsibilitiesContext.DutiesComplete(group)) break;
                        if (Environment.TickCount64 >= bindDeadline || Environment.TickCount64 >= deadline) break;
                        Thread.Sleep(StepMs);
                    }

                    // Confirm the control states have settled: re-read a frame later and accept
                    // them as trustworthy only if nothing moved. An unsettled group still renders,
                    // with its controls as last read — it just does not get to claim a duty is
                    // unchangeable, which is the one reading that would mislead.
                    if (group != null)
                    {
                        string sig = ResponsibilitiesContext.ControlSignature(group);
                        while (Environment.TickCount64 < bindDeadline && Environment.TickCount64 < deadline)
                        {
                            Thread.Sleep(StepMs);
                            var again = MainThreadDispatcher.Invoke(() =>
                            {
                                if (ContextMenuGuard.MenuActive()) throw new ContextMenuGuard.AbortException();
                                return ctx.ReadGroup(g);
                            });
                            if (again == null) break;
                            string nextSig = ResponsibilitiesContext.ControlSignature(again);
                            group = again;
                            if (nextSig == sig) { group.ControlsStable = true; break; }
                            sig = nextSig;
                        }
                    }
                    // A group that never realised inside its budget is recorded as missing rather
                    // than skipped, so the table can say so instead of quietly shrinking.
                    if (group == null) item.Truncated = true;
                    else
                    {
                        // A group whose own list still reports more duties than were realised is
                        // short: say so rather than let the table read as the whole picture.
                        if (!ResponsibilitiesContext.DutiesComplete(group)) item.Truncated = true;
                        groups.Add(group);
                    }

                    if (Environment.TickCount64 >= deadline)
                    {
                        if (g < total - 1) item.Truncated = true;
                        break;
                    }
                }

                item.Groups = groups;
            }
        }
        catch (ContextMenuGuard.AbortException)
        {
            // Deliberate stand-down: a context menu opened and the sweep's scrolling would recycle
            // its anchor row and kill it. The table renders empty behind the modal menu; the
            // post-close re-render reads it fresh.
            ServerText.LogBackground("[ResponsibilitiesLoader] read paused: context menu active");
            item.Groups = new List<ResponsibilitiesContext.Group>();
        }
        catch (Exception ex)
        {
            // Background thread: Log.* would hard-crash off the main thread.
            ServerText.LogBackground($"[ResponsibilitiesLoader] sweep failed: {ex.Message}");
            item.LoadFailed = true;
        }
    }
}

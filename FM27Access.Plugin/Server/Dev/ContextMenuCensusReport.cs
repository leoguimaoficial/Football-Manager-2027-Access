using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// The context-menu census: every menu mark the active screen would render, with the evidence behind
/// it — which holder answered, how far away it sat, and what <see cref="ContextMenuScope"/> makes of
/// that pairing.
///
/// Its purpose is a game-wide before/after diff. Sweeping every ribbon destination and concatenating
/// these reports gives a baseline; after a change to the scoping rule the same sweep must show every
/// changed row falling into "dropped a mismatch" or "kept a match", with the unchanged majority
/// untouched. That is what makes a universal fix reviewable without visiting screens one at a time.
///
/// It drives the SAME pipeline /body renders from — ScreenWalker's items with streamed tables
/// materialized — because a census that skips streamed rows would miss every table NAME CELL, which
/// is precisely the population of correct marks a scoping change must leave alone. Runs its phases on
/// the request thread like <see cref="ReadingView"/> (walk on the main thread, load tables across
/// frames, probe on the main thread), since the table load sleeps between frames.
/// </summary>
internal static class ContextMenuCensusReport
{
    private const int MaxListed = 200;

    public static string Run()
    {
        ContextMenuCensus.Begin();
        try { return RunRecorded(); }
        finally { ContextMenuCensus.End(); }
    }

    private static string RunRecorded()
    {
        var titles = new List<string>();

        // Phase 1 — walk every interactive panel under its own profile. Table cell marks are resolved
        // during the walk and the streamed load below, so the recorder is already armed for them.
        // Page handles are preserved: the census registers nothing, so the companion's live ids must
        // survive a dev probe.
        var sections = MainThreadDispatcher.Invoke(() =>
        {
            var panels = InteractivePanels.Current();
            HtmlRenderer.BeginBody(preservePageHandles: true);
            var active = NavigationManager.GetActivePanel();

            var built = new List<(object Panel, List<ScreenWalker.RenderItem> Items)>();
            foreach (var pv in panels)
            {
                AccessibilityServer.ActivateProfileFor(pv.Panel);
                titles.Add(pv.Title);
                built.Add((pv.Panel, HtmlRenderer.WalkPanel(pv.Panel)));
            }
            AccessibilityServer.ActivateProfileFor(active);
            return built;
        }, 10000);

        if (sections.Count == 0) return "no interactive panels\n";

        // Phase 2 — materialize streamed content, each panel under its own profile (streamed rows bake
        // their cell labels at read time), exactly as ComposeBody and ReadingView do.
        foreach (var (panel, items) in sections)
        {
            bool activated = false;
            foreach (var item in items)
            {
                bool streamed = item is ScreenWalker.TableItem { Context: not null }
                    or ScreenWalker.FixtureScheduleItem or ScreenWalker.ConditionTableItem;
                if (!streamed) continue;
                if (!activated)
                {
                    MainThreadDispatcher.Invoke(() => { AccessibilityServer.ActivateProfileFor(panel); return true; }, 10000);
                    activated = true;
                }
                try
                {
                    if (item is ScreenWalker.TableItem t && t.Context != null)
                        StreamedTableLoader.Load(t);
                    else if (item is ScreenWalker.FixtureScheduleItem fs)
                        FixtureScheduleLoader.Load(fs);
                    else if (item is ScreenWalker.ConditionTableItem ct)
                        ConditionTableLoader.Load(ct);
                }
                catch (Exception ex) { Log.Warn($"[ctxcensus] streamed load failed, marks from that table skipped: {ex.Message}"); }
            }
        }

        // Phase 3 — the marks the RENDERER (not the walk) asks for: every plain stop's own.
        MainThreadDispatcher.Invoke(() =>
        {
            var active = NavigationManager.GetActivePanel();
            foreach (var (panel, items) in sections)
            {
                AccessibilityServer.ActivateProfileFor(panel);
                foreach (var item in items)
                {
                    if (item is not ScreenWalker.StopItem stop) continue;
                    try { _ = stop.Element.HasContextMenu; }
                    catch (Exception ex) { Log.Warn($"[ctxcensus] HasContextMenu threw: {ex.Message}"); }
                }
            }
            AccessibilityServer.ActivateProfileFor(active);
            return true;
        }, 10000);

        return Render(titles, ContextMenuCensus.Snapshot());
    }

    private static string Render(List<string> titles, List<ContextMenuCensus.Entry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("========== CONTEXT MENU CENSUS ==========\n");
        sb.Append("panels=").Append(string.Join(" | ", titles)).Append('\n');

        int marked = 0;
        var byVerdict = new Dictionary<MenuScopeVerdict, int>();
        var depthByKind = new Dictionary<string, Dictionary<int, int>>();
        var rows = new List<string>();

        foreach (var e in entries)
        {
            if (!e.Marked) continue;
            marked++;

            var verdict = ContextMenuScope.Judge(
                e.OwnerLabel, e.HolderText, e.HolderType == "SIText", e.Depth, e.HolderCount,
                e.Kind == MenuHolderKind.Self, ContextMenuOpener.IsSelfContainedEntityHolder(e.HolderType));

            byVerdict.TryGetValue(verdict, out int n);
            byVerdict[verdict] = n + 1;

            string kindKey = e.Kind + "/" + (e.HolderType == "SIText" ? "text" : "binding");
            if (!depthByKind.TryGetValue(kindKey, out var hist)) depthByKind[kindKey] = hist = new Dictionary<int, int>();
            hist.TryGetValue(e.Depth, out int d);
            hist[e.Depth] = d + 1;

            rows.Add(Row(e, verdict));
        }

        sb.Append("resolved=").Append(entries.Count).Append(" marked=").Append(marked);
        if (ContextMenuCensus.Dropped > 0) sb.Append(" (census buffer overflow: +").Append(ContextMenuCensus.Dropped).Append(" unrecorded)");
        sb.Append('\n');

        sb.Append("VERDICTS:");
        foreach (MenuScopeVerdict v in Enum.GetValues(typeof(MenuScopeVerdict)))
        {
            byVerdict.TryGetValue(v, out int n);
            if (n > 0) sb.Append(' ').Append(v).Append('=').Append(n);
        }
        sb.Append('\n');

        foreach (var kind in depthByKind)
        {
            sb.Append("DEPTH ").Append(kind.Key).Append(':');
            var depths = new List<int>(kind.Value.Keys);
            depths.Sort();
            foreach (var d in depths) sb.Append(" d").Append(d).Append('=').Append(kind.Value[d]);
            sb.Append('\n');
        }

        int shown = Math.Min(rows.Count, MaxListed);
        for (int i = 0; i < shown; i++) sb.Append("  ").Append(rows[i]).Append('\n');
        if (rows.Count > shown) sb.Append("  ... +").Append(rows.Count - shown).Append(" more marks\n");

        return sb.ToString();
    }

    private static string Row(ContextMenuCensus.Entry e, MenuScopeVerdict verdict) =>
        (ContextMenuScope.Accepts(verdict) ? "KEEP " : "DROP ")
        + verdict
        + " | " + Pretty(e.OwnerLabel)
        + " <- " + e.HolderType + " d" + e.Depth + " n" + e.HolderCount
        + (string.IsNullOrEmpty(e.HolderText) ? "" : " " + Pretty(e.HolderText))
        + " [" + e.OwnerType + "]";

    private static string Pretty(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        var one = TextFilters.CleanForDisplay(s).Replace("\n", " ");
        if (one.Length > 48) one = one.Substring(0, 48) + "...";
        return "\"" + one + "\"";
    }
}

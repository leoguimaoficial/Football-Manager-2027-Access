using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// The dev driver's accessibility audit: it walks the live interactive panel set the SAME way the
/// HTML surface renders it (ScreenWalker), reduces every stop to its rendered (role, name,
/// activatable), and runs <see cref="AuditRules"/> to bucket the screen into the recurring defect
/// flavours — so the autonomous run gets a generated, categorised worklist instead of an agent
/// eyeballing /classify against /body screen by screen.
///
/// Two kinds of output, deliberately separated:
///   FINDINGS  — the closed set of flavours the rules decide with high confidence (empty-name,
///               degrade-marker, image-stop, orphan-label, dup-adjacent). Re-running after a
///               structural fix and watching a category fall to zero ACROSS every screen is the
///               regression guard that proves a fix generalised.
///   LEADS     — open-set nets for a flavour the closed rules do not yet model: id-shaped names
///               (a soft signal, real proper nouns trip it too) and the live /unclassified count
///               (new element types). These never read as "clean"; they point a human at what to
///               look at, and a confirmed new flavour becomes one new rule in AuditRules that then
///               covers every screen retroactively.
/// Main-thread only (it classifies live elements), mirroring Introspect.DumpClassification.
/// </summary>
internal static class Audit
{
    // Per-category listing cap: enough specifics to act on, bounded so a pathological screen cannot
    // bloat the response. Overflow is noted, never silently dropped.
    private const int MaxListedPerCategory = 25;

    public static string RunActive()
    {
        var panels = InteractivePanels.Current();
        if (panels.Count == 0)
            return "no interactive panels\n";

        var active = NavigationManager.GetActivePanel();
        var sb = new StringBuilder();
        var totals = new Tally();

        foreach (var pv in panels)
        {
            // Classify under each panel's own profile (hide/rename rules differ), restoring the
            // speech panel's profile after — exactly as the multi-panel body build and /classify do.
            AccessibilityServer.ActivateProfileFor(pv.Panel);
            AuditPanel(sb, pv.Title, pv.Panel, totals);
        }
        AccessibilityServer.ActivateProfileFor(active);

        sb.Append("========== AUDIT SUMMARY ==========\n");
        sb.Append("panels=").Append(panels.Count).Append('\n');
        totals.AppendCounts(sb);
        AppendUnclassified(sb);
        return sb.ToString();
    }

    private static void AuditPanel(StringBuilder sb, string title, object panel, Tally totals)
    {
        List<ScreenWalker.RenderItem> items;
        try { items = ScreenWalker.WalkScreen(panel); }
        catch (Exception ex) { sb.Append("========== PANEL: \"").Append(title).Append("\" (walk failed: ").Append(ex.Message).Append(") ==========\n"); return; }

        // Reduce every render item to the rule's view (role, name, activatable), keeping the rendered
        // name for the report. Group items (table, tablist, radiogroup, scoreboard) map to a
        // non-interactive stop so they correctly break orphan/dup runs and are never flagged.
        var stops = new List<AuditRules.Stop>(items.Count);
        var names = new List<string>(items.Count);
        foreach (var item in items)
        {
            var (role, name, canActivate) = Reduce(item);
            stops.Add(new AuditRules.Stop(role, name, canActivate));
            names.Add(name);
        }

        var panelFindings = new Findings();
        int interactive = 0, informational = 0, groups = 0;

        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            if (IsGroupRole(s.Role)) groups++;
            else if (s.Interactive) interactive++;
            else informational++;

            switch (AuditRules.ClassifyStop(s.Role, s.Name, s.CanActivate))
            {
                case AuditRules.Finding.EmptyName: panelFindings.EmptyName.Add(Entry(i, s.Role, "(no name)")); break;
                case AuditRules.Finding.DegradeMarker: panelFindings.Degrade.Add(Entry(i, s.Role, names[i])); break;
                case AuditRules.Finding.ImageStop: panelFindings.Image.Add(Entry(i, s.Role, names[i])); break;
            }

            // LEAD, not a finding: an id-shaped name on a control the rules judged Ok. Skipped for the
            // degrade markers (which legitimately read like phrases) and only for activatable stops.
            if (s.Interactive && !AuditRules.IsDegradeMarker(s.Name) && AuditRules.LooksLikeDeveloperId(s.Name))
                panelFindings.IdShaped.Add(Entry(i, s.Role, names[i]));

            if (i + 1 < stops.Count && AuditRules.IsOrphanLabel(s, stops[i + 1]))
                panelFindings.Orphan.Add(Entry(i, s.Role, names[i] + " -> [" + stops[i + 1].Role + "] " + Pretty(names[i + 1])));

            if (i + 1 < stops.Count && AuditRules.IsDuplicateAdjacent(s, stops[i + 1]))
                panelFindings.Dup.Add(Entry(i, s.Role, names[i]));

            // LEAD, not a finding: this control has a menu the game armed but the scope rule suppresses
            // as mislabeled (the holder names a different entity than the label — a summary tile over a
            // buried player, a section header over a name). Correct to drop today; listed so a NEW
            // mislabel shape from a game update surfaces here on its own instead of by ear. Needs the
            // live element, so it reuses the same ContextMenuScope decision the mark itself makes.
            if (items[i] is ScreenWalker.StopItem stopItem)
            {
                var note = MenuSuppressedNote(stopItem.Element, names[i]);
                if (note != null) panelFindings.MenuSuppressed.Add(Entry(i, s.Role, note));
            }
        }

        sb.Append("========== PANEL: \"").Append(title).Append("\" ==========\n");
        sb.Append("stops=").Append(stops.Count)
          .Append(" interactive=").Append(interactive)
          .Append(" informational=").Append(informational)
          .Append(" groups=").Append(groups).Append('\n');
        sb.Append("FINDINGS: empty-name=").Append(panelFindings.EmptyName.Count)
          .Append(" degrade=").Append(panelFindings.Degrade.Count)
          .Append(" image=").Append(panelFindings.Image.Count)
          .Append(" orphan-label=").Append(panelFindings.Orphan.Count)
          .Append(" dup-adjacent=").Append(panelFindings.Dup.Count)
          .Append(" | LEADS: id-shaped=").Append(panelFindings.IdShaped.Count)
          .Append(" menu-suppressed=").Append(panelFindings.MenuSuppressed.Count).Append('\n');

        AppendList(sb, "empty-name", panelFindings.EmptyName);
        AppendList(sb, "degrade", panelFindings.Degrade);
        AppendList(sb, "image", panelFindings.Image);
        AppendList(sb, "orphan-label", panelFindings.Orphan);
        AppendList(sb, "dup-adjacent", panelFindings.Dup);
        AppendList(sb, "id-shaped (lead)", panelFindings.IdShaped);
        AppendList(sb, "menu-suppressed (lead)", panelFindings.MenuSuppressed);

        totals.Add(panelFindings);
    }

    // Reduce a render item to (role, name, activatable). For a stop, the role is "heading" when the
    // profile/heading rule promotes it (the renderer emits <h3>), else the adapter's own role; the
    // name is the rendered accessible name (rename first, then the live primary label).
    private static (string role, string name, bool canActivate) Reduce(ScreenWalker.RenderItem item)
    {
        switch (item)
        {
            case ScreenWalker.StopItem s:
            {
                var el = s.Element;
                string role = ServerText.IsHeading(el) ? "heading" : ServerText.SafeRole(el);
                string name = ResolveName(el);
                bool canActivate;
                try { canActivate = el.CanActivate; }
                catch (Exception ex) { Log.Warn($"[Audit] CanActivate threw: {ex.Message}"); canActivate = false; }
                return (role, name, canActivate);
            }
            case ScreenWalker.TableItem _: return ("table", "", false);
            case ScreenWalker.TabBarItem _: return ("tablist", "", false);
            case ScreenWalker.RadioGroupItem rg: return ("radiogroup", rg.Label ?? "", false);
            case ScreenWalker.ScoreboardItem _: return ("scoreboard", "", false);
            case ScreenWalker.HeadingItem h: return ("heading", h.Text, false);
            default: return ("", "", false);
        }
    }

    // The rendered accessible name: a profile rename if one applies (as the renderer prefers for
    // dropdowns/icon controls), else the live primary label. An unreadable label degrades to the
    // honest marker so it is flagged, never silently blank.
    private static string ResolveName(IAccessibleElement el)
    {
        var rename = ServerText.RenameOr(el, null);
        if (!string.IsNullOrEmpty(rename)) return rename;
        try { var l = el.GetPrimaryLabel(); if (!string.IsNullOrEmpty(l)) return l; }
        catch (Exception ex) { Log.Warn($"[Audit] GetPrimaryLabel threw: {ex.Message}"); return Speech.Degrade.Unreadable; }
        try { return el.GetLabel() ?? ""; }
        catch (Exception ex) { Log.Warn($"[Audit] GetLabel threw: {ex.Message}"); return Speech.Degrade.Unreadable; }
    }

    // A note for a stop whose game-armed menu the scope rule suppresses, else null. Resolves the
    // menu on the live element and judges it against the rendered label exactly as the mark does;
    // reports the holder the menu would have opened, so the lead is actionable ("Medical Center ->
    // menu of Alessandro Grant"). A non-PluginAdapterBase stop (no menu path) yields null.
    private static string MenuSuppressedNote(IAccessibleElement el, string label)
    {
        if (el is not Adapters.PluginAdapterBase p) return null;
        Adapters.MenuResolution r;
        try { r = Adapters.ContextMenuOpener.Resolve(p.BackingElement); }
        catch (Exception ex) { Log.Warn($"[Audit] menu resolve threw: {ex.Message}"); return null; }
        if (r.Target == null) return null;

        var verdict = Adapters.ContextMenuOpener.Verdict(r, label);
        if (ContextMenuScope.Accepts(verdict)) return null;   // the mark was kept — nothing suppressed

        string holder = string.IsNullOrEmpty(r.HolderText) ? r.HolderType : Pretty(ServerText.Clean(r.HolderText));
        return "menu of " + holder + " [" + verdict + " d" + r.Depth + "]";
    }

    private static bool IsGroupRole(string role) =>
        role is "table" or "tablist" or "radiogroup" or "scoreboard";

    private static string Entry(int index, string role, string text) => "#" + index + " [" + role + "] " + Pretty(text);

    private static string Pretty(string s) => string.IsNullOrEmpty(s) ? "(empty)" : "\"" + s.Replace("\n", " ") + "\"";

    private static void AppendList(StringBuilder sb, string label, List<string> entries)
    {
        if (entries.Count == 0) return;
        int shown = Math.Min(entries.Count, MaxListedPerCategory);
        for (int i = 0; i < shown; i++)
            sb.Append("  ").Append(label).Append(' ').Append(entries[i]).Append('\n');
        if (entries.Count > shown)
            sb.Append("  ").Append(label).Append(" ... +").Append(entries.Count - shown).Append(" more\n");
    }

    private static void AppendUnclassified(StringBuilder sb)
    {
        // Fold in the open-set net for new ELEMENT TYPES: the classifier could not map these, so they
        // are exactly the candidates for a flavour the closed rules do not model yet. Count only —
        // the full list is /unclassified.
        try
        {
            int n = UnclassifiedTracker.Entries.Count;
            sb.Append("unclassified-types=").Append(n);
            if (n > 0) sb.Append(" (see /unclassified)");
            sb.Append('\n');
        }
        catch (Exception ex) { sb.Append("unclassified-types=? (").Append(ex.Message).Append(")\n"); }
    }

    private sealed class Findings
    {
        public readonly List<string> EmptyName = new();
        public readonly List<string> Degrade = new();
        public readonly List<string> Image = new();
        public readonly List<string> Orphan = new();
        public readonly List<string> Dup = new();
        public readonly List<string> IdShaped = new();
        public readonly List<string> MenuSuppressed = new();
    }

    private sealed class Tally
    {
        private int _empty, _degrade, _image, _orphan, _dup, _idShaped, _menuSuppressed;

        public void Add(Findings f)
        {
            _empty += f.EmptyName.Count;
            _degrade += f.Degrade.Count;
            _image += f.Image.Count;
            _orphan += f.Orphan.Count;
            _dup += f.Dup.Count;
            _idShaped += f.IdShaped.Count;
            _menuSuppressed += f.MenuSuppressed.Count;
        }

        public void AppendCounts(StringBuilder sb)
        {
            sb.Append("FINDINGS: empty-name=").Append(_empty)
              .Append(" degrade=").Append(_degrade)
              .Append(" image=").Append(_image)
              .Append(" orphan-label=").Append(_orphan)
              .Append(" dup-adjacent=").Append(_dup)
              .Append(" | LEADS: id-shaped=").Append(_idShaped)
              .Append(" menu-suppressed=").Append(_menuSuppressed).Append('\n');
        }
    }
}

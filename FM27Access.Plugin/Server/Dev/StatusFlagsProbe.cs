using System;
using System.Reflection;
using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for SI.Bindable.ExpandingList — the widget behind the squad table's "Inf"
/// (StatusFlags) column. Collapsed, it puts exactly ONE item in the row tree and builds the rest
/// inside a FloatingElement only on hover/click, so the walker sees one badge and the cell reads
/// as complete. This dumps the widget's own backing state (m_items, the collapsible/preview
/// elements, the expanded flag) to establish whether the hidden flags can be read without
/// driving the game's expand.
///
/// GET /statusflagsprobe dumps per widget; ?mode=compact gives one line per cell, resolving every
/// flag's short name, full name and reason. Both are read-only.
///
/// ?mode=clickN is NOT read-only: it fires the Nth widget's first hidden badge to test whether a
/// detached control can be driven. It established that one cannot — the button is real and enabled
/// but its panel is null, so SimulateButtonClick's bound command has no context to resolve through.
///
/// Nothing here ever calls Expand/ShowFloatingElement, and nothing ever casts m_floatingElement
/// (a FloatingElement is not a VisualElement — casting one hard-crashes the process).
/// </summary>
internal static class StatusFlagsProbe
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static;

    private static bool _compact;

    internal static string Run(string mode)
    {
        _compact = mode == "compact";
        var panel = NavigationManager.GetActivePanel();
        if (panel is not VisualElement root) return "no active panel\n";

        _clickCell = -1;
        if (mode != null && mode.StartsWith("click", StringComparison.Ordinal))
            int.TryParse(mode.Substring(5), out _clickCell);

        var sb = new StringBuilder();
        int found = 0, budget = 0;
        Walk(root, sb, ref found, ref budget);
        return found > 0 ? sb.ToString() : "no ExpandingList on the active panel\n";
    }

    // mode=clickN: SimulateButtonClick the first HIDDEN flag's button on the Nth ExpandingList.
    // That button lives in m_collapsibleElements, reparented into the floating popup and invisible —
    // the whole question being whether the game's own control can be driven without expanding.
    private static int _clickCell = -1;

    private static void ClickHidden(VisualElement el, int index, StringBuilder sb)
    {
        var t = typeof(SI.Bindable.ExpandingList);
        var typed = SafeGet(() => el.TryCast<SI.Bindable.ExpandingList>());
        if (typed == null) { sb.Append('[').Append(index).Append("] cast failed\n"); return; }

        var collProp = t.GetProperty("m_collapsibleElements", All);
        var els = collProp == null ? null
            : SafeGet(() => collProp.GetValue(typed) as Il2CppSystem.Collections.Generic.List<VisualElement>);
        int n = els == null ? -1 : SafeInt(() => els.Count);
        if (n <= 0) { sb.Append('[').Append(index).Append("] no collapsible elements\n"); return; }

        var button = FindButton(els[0], 0);
        if (button == null) { sb.Append('[').Append(index).Append("] no SIButton in collapsible[0]\n"); return; }

        string label = SafeCall(() => AllTexts(button));
        sb.Append('[').Append(index).Append("] hidden flag \"").Append(label)
          .Append("\" panel=").Append(SafeCall(() => button.panel == null ? "null" : "live"))
          .Append(" enabled=").Append(SafeCall(() => button.enabledInHierarchy.ToString()))
          .Append(" parent=").Append(SafeCall(() => button.hierarchy.parent == null ? "null" : button.hierarchy.parent.GetIl2CppType().Name))
          .Append('\n');
        try
        {
            button.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
            // The mod's normal activation follows SimulateButtonClick with a ClickEvent, but only
            // while the element is still live in a panel; a detached one would dispatch through
            // freed UI state.
            if (button.panel != null)
            {
                var click = UnityEngine.UIElements.ClickEvent.GetPooled();
                button.SendEvent(click);
                sb.Append("    sent supplementary ClickEvent\n");
            }
            sb.Append('[').Append(index).Append("] clicked hidden flag \"").Append(label).Append("\"\n");
        }
        catch (Exception e)
        {
            sb.Append('[').Append(index).Append("] click threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    private static VisualElement FindButton(VisualElement el, int depth)
    {
        if (el == null || depth > 16) return null;
        try
        {
            if (el.TryCast<SI.Bindable.SIButton>() != null) return el;
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var found = FindButton(el.hierarchy[i], depth + 1);
                if (found != null) return found;
            }
        }
        catch { return null; }
        return null;
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int found, ref int budget)
    {
        if (budget++ > 40000) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }

        if (type == "ExpandingList")
        {
            found++;
            if (_clickCell >= 0) { if (found == _clickCell) ClickHidden(el, found, sb); return; }
            if (_compact) { Compact(el, found, sb); return; }
            sb.Append("========== ExpandingList ").Append(found)
              .Append(" name=\"").Append(el.name ?? "").Append("\" ==========\n");
            Dump(el, sb);
            return;
        }

        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            VisualElement child;
            try { child = el.hierarchy[i]; } catch { return; }
            Walk(child, sb, ref found, ref budget);
        }
    }

    // One line per cell: the visible badge, then every hidden item's short and long name read
    // straight off m_items. Surveys a whole table without the per-field dump.
    private static void Compact(VisualElement el, int index, StringBuilder sb)
    {
        var t = typeof(SI.Bindable.ExpandingList);
        object list;
        try { list = el.Cast<SI.Bindable.ExpandingList>(); }
        catch (Exception e) { sb.Append('[').Append(index).Append("] cast failed ").Append(e.GetType().Name).Append('\n'); return; }

        string visible = "";
        var avProp = t.GetProperty("m_alwaysVisibleElement", All);
        if (avProp != null)
        {
            try { if (avProp.GetValue(list) is VisualElement av) visible = AllTexts(av); }
            catch (Exception e) { visible = "err:" + e.GetType().Name; }
        }

        var itemsProp = t.GetProperty("m_items", All);
        var items = itemsProp == null ? null
            : SafeGet(() => itemsProp.GetValue(list) as Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>);
        int n = items == null ? -1 : SafeInt(() => items.Count);

        sb.Append('[').Append(index).Append("] visible=\"").Append(visible).Append("\"");
        if (avProp != null)
        {
            try
            {
                if (avProp.GetValue(list) is VisualElement av2 && av2.GetIl2CppType().Name.Contains("BindingRoot"))
                    sb.Append(" avBinding={").Append(BindingValues(av2.Cast<SI.Bindable.BindingRoot>())).Append('}');
            }
            catch (Exception e) { sb.Append(" avBinding=err:").Append(e.GetType().Name); }
        }
        sb.Append(" hidden=").Append(n);
        for (int i = 0; i < n && i < 12; i++)
            sb.Append(" | ").Append(Describe(items[i]));
        // Can the hidden flags be ACTED on? Their elements are reparented into the floating popup,
        // but a reparented element is still a VisualElement, and SimulateButtonClick is documented
        // to work on invisible ones. Dump what buttons the collapsible set actually holds.
        var collProp = t.GetProperty("m_collapsibleElements", All);
        if (collProp != null)
        {
            var els = SafeGet(() => collProp.GetValue(list) as Il2CppSystem.Collections.Generic.List<VisualElement>);
            int cn = els == null ? -1 : SafeInt(() => els.Count);
            sb.Append(" || collapsible=").Append(cn);
            for (int i = 0; i < cn && i < 8; i++)
            {
                var ve = SafeGet(() => els[i]);
                sb.Append(" [").Append(i).Append(' ').Append(ve == null ? "null" : SafeCall(() => ve.GetIl2CppType().Name))
                  .Append(" texts=").Append(ve == null ? "" : SafeCall(() => AllTexts(ve)))
                  .Append(" buttons=").Append(ve == null ? "" : SafeCall(() => ButtonSummary(ve)))
                  .Append(']');
            }
        }
        var listProp = t.GetProperty("m_list", All);
        if (listProp != null)
        {
            var vl = SafeGet(() => listProp.GetValue(list) as Il2CppSystem.Object);
            sb.Append(" || m_list=").Append(vl == null ? "null" : SafeCall(() => vl.GetIl2CppType().Name));
        }
        sb.Append("\n    ancestors: ").Append(SourceList(el)).Append('\n');
    }

    // The row binding that FEEDS SetList: walking up to the nearest ancestor BindingRoot and
    // listing its node's children establishes whether m_items is the complete overflow or only the
    // one item the preview peeks at.
    private static string SourceList(VisualElement el)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return "no BindingSubsystem";
        var found = new System.Collections.Generic.List<string>();
        var cur = el.hierarchy.parent;
        for (int i = 0; i < 30 && cur != null; i++, cur = cur.hierarchy.parent)
        {
            var root = SafeGet(() => cur.TryCast<SI.Bindable.BindingRoot>());
            if (root == null) continue;
            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) continue;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var child in node.Children)
            {
                var ck = child.Key;
                var v = SafeGet(() => bindings.Get(ref ck));
                parts.Add(child.Name + "[" + (v == null ? "null" : SafeCall(() => ListLength(v))) + "]");
                if (parts.Count > 20) break;
            }
            found.Add(SafeCall(() => cur.GetIl2CppType().Name) + "{" + string.Join(",", parts) + "}");
            if (found.Count >= 4) break;
        }
        return found.Count == 0 ? "no ancestor BindingRoot" : string.Join(" >> ", found);
    }

    // A bound value's element count when it is a list, else its type name — enough to spot the
    // property that carries the whole flag list.
    private static string ListLength(Il2CppSystem.Object v)
    {
        var coll = v.TryCast<Il2CppSystem.Collections.ICollection>();
        if (coll != null) return "count=" + coll.Count;
        var tv = v.TryCast<SI.Core.TypedValue>();
        if (tv != null)
        {
            var inner = (tv.Get() as Il2CppSystem.Object)?.TryCast<Il2CppSystem.Collections.ICollection>();
            if (inner != null) return "tv.count=" + inner.Count;
            return "tv:" + SafeCall(() => tv.DataType?.Name);
        }
        return v.GetIl2CppType().Name;
    }

    // The always-visible badge's own bound values, read through the binding subsystem the same way
    // MatchSquadReadout reads a row node. m_items excludes the always-visible item, so this is the
    // only route to that badge's full name and reason.
    private static string BindingValues(SI.Bindable.BindingRoot root)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return "no BindingSubsystem";
        var key = root.Key;
        if (!bindings.TryGetNode(ref key, out var node)) return "no node";
        // The node's "StatusFlag" child holds the same DynamicReference Record the hidden items
        // carry, so the visible badge resolves through exactly the same read.
        foreach (var child in node.Children)
        {
            if (child.Name != "StatusFlag") continue;
            var childKey = child.Key;
            var value = SafeGet(() => bindings.Get(ref childKey));
            if (value == null) return "StatusFlag null";
            return Describe(value.TryCast<SI.Core.TypedValue>());
        }
        return "no StatusFlag child";
    }

    // short / full / reason off one item's Record, keyed by the game's property NAMES.
    private static string Describe(SI.Core.TypedValue tv)
    {
        var rec = SafeGet(() => (tv?.Get() as Il2CppSystem.Object)?.TryCast<SI.Core.Record>());
        if (rec == null) return "not a Record";
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        string shortName = null, full = null, reason = null;
        try
        {
            var en = rec.GetEnumerator();
            int guard = 0;
            while (en.MoveNext() && guard++ < 40)
            {
                var kv = en.Current;
                string name = SafeCall(() => reg.GetInfo(kv.Key).Name);
                if (name == "StatusTitleShort") shortName = SafeCall(() => kv.Value?.AsString());
                else if (name == "StatusTitleFull") full = SafeCall(() => kv.Value?.AsString());
                else if (name == "StatusReason") reason = SafeCall(() => kv.Value?.AsString());
            }
        }
        catch (Exception e) { return "err:" + e.GetType().Name; }
        return $"{shortName} = {full} ({reason})";
    }

    // Every SIButton under an element, with its text and whether it is enabled — the question being
    // whether a hidden flag has a real control that a click could be routed to.
    private static string ButtonSummary(VisualElement el)
    {
        var found = new System.Collections.Generic.List<string>();
        CollectButtons(el, found, 0);
        return found.Count == 0 ? "none" : string.Join("/", found);
    }

    private static void CollectButtons(VisualElement el, System.Collections.Generic.List<string> into, int depth)
    {
        if (el == null || depth > 16 || into.Count >= 8) return;
        try
        {
            if (el.TryCast<SI.Bindable.SIButton>() != null)
                into.Add("SIButton(enabled=" + el.enabledInHierarchy + ",text=" + AllTexts(el) + ")");
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++) CollectButtons(el.hierarchy[i], into, depth + 1);
        }
        catch { }
    }

    private static T SafeGet<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }
    private static int SafeInt(Func<int> f) { try { return f(); } catch { return -1; } }

    private static void Dump(VisualElement el, StringBuilder sb)
    {
        var t = typeof(SI.Bindable.ExpandingList);
        object list;
        try { list = el.Cast<SI.Bindable.ExpandingList>(); }
        catch (Exception e) { sb.Append("  cast failed: ").Append(e.GetType().Name).Append('\n'); return; }

        sb.Append("  RowParamName=").Append(Read(t, list, "RowParamName")).Append('\n');
        sb.Append("  m_expanded=").Append(Read(t, list, "m_expanded"))
          .Append(" m_isPreviewElementBound=").Append(Read(t, list, "m_isPreviewElementBound"))
          .Append(" m_isAlwaysVisibleElementBound=").Append(Read(t, list, "m_isAlwaysVisibleElementBound"))
          .Append('\n');

        // The full item list. TypedValue is an abstract CLASS (SI.Core.TypedValue), so this is a
        // generic over a reference type — none of the IL2CPP struct-generic hazard applies.
        DumpItems(t, list, sb);

        DumpElement(t, list, "m_alwaysVisibleElement", sb);
        DumpElement(t, list, "m_previewElement", sb);
        DumpCollapsibles(t, list, sb);

        // Presence only. Never cast: FloatingElement is not a VisualElement.
        var fe = t.GetProperty("m_floatingElement", All);
        object feVal = null;
        if (fe != null) { try { feVal = fe.GetValue(list); } catch (Exception e) { sb.Append("  m_floatingElement read threw ").Append(e.GetType().Name).Append('\n'); } }
        sb.Append("  m_floatingElement=").Append(fe == null ? "PROP NOT FOUND" : feVal == null ? "null" : "present").Append('\n');
    }

    private static void DumpItems(Type t, object list, StringBuilder sb)
    {
        var prop = t.GetProperty("m_items", All);
        if (prop == null) { sb.Append("  m_items: PROP NOT FOUND\n"); return; }

        object raw;
        try { raw = prop.GetValue(list); }
        catch (Exception e) { sb.Append("  m_items read threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); return; }
        if (raw == null) { sb.Append("  m_items: null\n"); return; }

        sb.Append("  m_items managed type: ").Append(raw.GetType().FullName).Append('\n');

        var items = raw as Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>;
        if (items == null)
        {
            sb.Append("  m_items: not List<TypedValue> as declared; trying ICollection count\n");
            try
            {
                var coll = (raw as Il2CppSystem.Object)?.TryCast<Il2CppSystem.Collections.ICollection>();
                sb.Append("  m_items count via ICollection: ").Append(coll == null ? "n/a" : coll.Count.ToString()).Append('\n');
            }
            catch (Exception e) { sb.Append("  ICollection count threw ").Append(e.GetType().Name).Append('\n'); }
            return;
        }

        int n;
        try { n = items.Count; }
        catch (Exception e) { sb.Append("  m_items.Count threw ").Append(e.GetType().Name).Append('\n'); return; }
        sb.Append("  m_items.Count=").Append(n).Append('\n');

        for (int i = 0; i < n && i < 20; i++)
        {
            sb.Append("    [").Append(i).Append("] ");
            try
            {
                var tv = items[i];
                if (tv == null) { sb.Append("null\n"); continue; }
                sb.Append("dataType=").Append(SafeCall(() => tv.DataType?.ToString()))
                  .Append(" isNull=").Append(SafeCall(() => tv.IsNull.ToString()))
                  .Append('\n');
                DumpRecord(tv, sb);
            }
            catch (Exception e) { sb.Append("read threw ").Append(e.GetType().Name).Append('\n'); }
        }
    }

    // A DynamicReference is a Record, i.e. Dictionary&lt;uint, TypedValue&gt; — the item's bound
    // properties. If the badge's short text or its flag id lives here, the hidden flags can be
    // read straight off the widget's data with no expand at all.
    private static void DumpRecord(SI.Core.TypedValue tv, StringBuilder sb)
    {
        object obj;
        try { obj = tv.Get(); }
        catch (Exception e) { sb.Append("      Get() threw ").Append(e.GetType().Name).Append('\n'); return; }
        if (obj == null) { sb.Append("      Get() null\n"); return; }

        var rec = (obj as Il2CppSystem.Object)?.TryCast<SI.Core.Record>();
        if (rec == null) { sb.Append("      not a Record (").Append(obj.GetType().FullName).Append(")\n"); return; }

        int count;
        try { count = rec.Count; }
        catch (Exception e) { sb.Append("      Record.Count threw ").Append(e.GetType().Name).Append('\n'); return; }
        sb.Append("      Record.Count=").Append(count).Append('\n');
        try
        {
            // Resolve each property id to its NAME through the game's own registry, the same read
            // LeagueHistoryChartReader does — so a binding can key off names, not magic numbers.
            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var en = rec.GetEnumerator();
            int guard = 0;
            while (en.MoveNext() && guard++ < 40)
            {
                var kv = en.Current;
                sb.Append("        key=").Append(kv.Key)
                  .Append(" name=").Append(SafeCall(() => reg.GetInfo(kv.Key).Name))
                  .Append(" type=").Append(SafeCall(() => kv.Value?.DataType?.ToString()))
                  .Append(" value=\"").Append(SafeCall(() => kv.Value?.AsString())).Append("\"\n");
            }
        }
        catch (Exception e) { sb.Append("      enumerate threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
    }

    private static void DumpElement(Type t, object list, string member, StringBuilder sb)
    {
        var prop = t.GetProperty(member, All);
        if (prop == null) { sb.Append("  ").Append(member).Append(": PROP NOT FOUND\n"); return; }
        object raw;
        try { raw = prop.GetValue(list); }
        catch (Exception e) { sb.Append("  ").Append(member).Append(" read threw ").Append(e.GetType().Name).Append('\n'); return; }
        if (raw is not VisualElement ve) { sb.Append("  ").Append(member).Append(": ").Append(raw == null ? "null" : raw.GetType().Name).Append('\n'); return; }
        sb.Append("  ").Append(member).Append(": name=\"").Append(ve.name ?? "")
          .Append("\" type=").Append(SafeCall(() => ve.GetIl2CppType().Name))
          .Append(" visible=").Append(SafeCall(() => ve.visible.ToString()))
          .Append(" inTree=").Append(SafeCall(() => InExpandingListTree(ve).ToString()))
          .Append(" display=").Append(SafeCall(() => ve.resolvedStyle.display.ToString()))
          .Append(" children=").Append(SafeCall(() => ve.hierarchy.childCount.ToString()))
          .Append(" texts=[").Append(SafeCall(() => AllTexts(ve))).Append("]\n");
    }

    // Whether the element is still parented under an ExpandingList in the row's own tree, or has
    // been reparented into the floating popup. Decides whether a walker could ever reach it.
    private static bool InExpandingListTree(VisualElement ve)
    {
        var cur = ve;
        for (int i = 0; i < 40 && cur != null; i++)
        {
            if (cur.GetIl2CppType().Name == "ExpandingList") return true;
            cur = cur.hierarchy.parent;
        }
        return false;
    }

    // Every text under an element, comma-joined. Deep enough to clear the badge templates'
    // binding wrappers, which a shallow first-text read was cutting short.
    private static string AllTexts(VisualElement el)
    {
        var into = new System.Collections.Generic.List<string>();
        Collect(el, into, 0);
        return string.Join(", ", into);
    }

    private static void Collect(VisualElement el, System.Collections.Generic.List<string> into, int depth)
    {
        if (el == null || depth > 16 || into.Count >= 12) return;
        try
        {
            if (el.GetIl2CppType().Name == "SIText")
            {
                var text = el.Cast<SI.Bindable.SIText>().text;
                if (!string.IsNullOrEmpty(text)) into.Add(text);
            }
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++) Collect(el.hierarchy[i], into, depth + 1);
        }
        catch { }
    }

    private static void DumpCollapsibles(Type t, object list, StringBuilder sb)
    {
        var prop = t.GetProperty("m_collapsibleElements", All);
        if (prop == null) { sb.Append("  m_collapsibleElements: PROP NOT FOUND\n"); return; }
        object raw;
        try { raw = prop.GetValue(list); }
        catch (Exception e) { sb.Append("  m_collapsibleElements read threw ").Append(e.GetType().Name).Append('\n'); return; }
        if (raw == null) { sb.Append("  m_collapsibleElements: null\n"); return; }

        var els = raw as Il2CppSystem.Collections.Generic.List<VisualElement>;
        if (els == null) { sb.Append("  m_collapsibleElements: unexpected type ").Append(raw.GetType().FullName).Append('\n'); return; }
        int n;
        try { n = els.Count; }
        catch (Exception e) { sb.Append("  m_collapsibleElements.Count threw ").Append(e.GetType().Name).Append('\n'); return; }
        sb.Append("  m_collapsibleElements.Count=").Append(n).Append('\n');
        for (int i = 0; i < n && i < 20; i++)
        {
            try
            {
                var ve = els[i];
                sb.Append("    [").Append(i).Append("] name=\"").Append(ve?.name ?? "")
                  .Append("\" type=").Append(ve == null ? "n/a" : SafeCall(() => ve.GetIl2CppType().Name))
                  .Append(" visible=").Append(ve == null ? "n/a" : SafeCall(() => ve.visible.ToString()))
                  .Append(" inTree=").Append(ve == null ? "n/a" : SafeCall(() => InExpandingListTree(ve).ToString()))
                  .Append(" texts=[").Append(ve == null ? "" : SafeCall(() => AllTexts(ve))).Append("]\n");
            }
            catch (Exception e) { sb.Append("    [").Append(i).Append("] read threw ").Append(e.GetType().Name).Append('\n'); }
        }
    }

    private static string Read(Type t, object obj, string member)
    {
        var p = t.GetProperty(member, All);
        if (p == null) return "PROP NOT FOUND";
        try { return p.GetValue(obj)?.ToString() ?? "null"; }
        catch (Exception e) { return "err:" + e.GetType().Name; }
    }

    private static string SafeCall(Func<string> read)
    {
        try { return read() ?? "null"; }
        catch (Exception e) { return "err:" + e.GetType().Name; }
    }
}

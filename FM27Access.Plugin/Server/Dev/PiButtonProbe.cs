using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only: where the squad-table Player Instructions button's REAL click payload lives, and what
/// it says. The button ignores every synthetic UI event (input-action driven), and its SIButton
/// Events list is empty - so the TacticsPitchPositionEventContext a real click fills must be wired
/// somewhere else in the row. This dumps every event owner in a row's subtree (SIButton EventLists
/// and ContextShortcutsElements), and the resolved fields of any TacticsPitchPositionEventContext
/// found, so the adapter can be pointed at the game's own payload instead of guessing the index.
/// GET /pibtnprobe?row=N (default 1, the D (R) row on the Tactics table).
/// </summary>
internal static class PiButtonProbe
{
    /// <summary>Prime and SEND the row's PI button's own EventToPluginContext, to establish
    /// whether the game honours the bare dispatch (the quick-change FMX events did not).</summary>
    public static string Send(int row)
    {
        var list = FindSquadList();
        if (list == null) return "no squad-table VirtualisedList\n";
        int n = list.GetActiveCount();
        for (int i = 0; i < n; i++)
        {
            var item = list.GetActiveItem(i);
            if (item == null || item.Index != row || item.Content == null) continue;
            VisualElement piBtn = null;
            FindPiButton(item.Content, 0, ref piBtn);
            if (piBtn == null) return "row has no PI button\n";
            var btn = piBtn.Cast<SI.Bindable.SIButton>();
            var contexts = btn.Events?.Events;
            if (contexts == null) return "PI button has no events\n";
            foreach (var ctx in contexts)
            {
                var plug = ctx?.TryCast<FM.GamePlugin.EventToPluginContext>();
                if (plug == null) continue;
                plug.PrepareToSend();
                if (!plug.IsSendable()) return "context not sendable\n";
                var evt = plug.GetPooledEvent()?.TryCast<FM.GamePlugin.EventToPlugin>();
                if (evt == null) return "no pooled event\n";
                if (evt.Target == null) { evt.Dispose(); return "target null, refusing to send\n"; }
                string id = evt.ID.ToString();
                var events = SI.Core.GameSubsystems.Get<SI.Core.EventSubsystem>();
                events.SendEvent(evt);
                return "sent " + id + " for row " + row + "\n";
            }
            return "no EventToPluginContext on the PI button\n";
        }
        return "row not active\n";
    }

    private static void FindPiButton(VisualElement el, int d, ref VisualElement match)
    {
        if (match != null || d > 30) return;
        if (el.TryCast<SI.Bindable.SIButton>() != null
            && Adapters.PlayerInstructionsButtonAdapter.IsPlayerInstructionsButton(el)) { match = el; return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) FindPiButton(el.hierarchy[i], d + 1, ref match);
    }

    /// <summary>Every active row: virtualised index, active-slot position, and leading text - the
    /// raw material for working out which index space the game's open event actually uses.</summary>
    public static string ListRows()
    {
        var sb = new StringBuilder();
        var list = FindSquadList();
        if (list == null) return "no squad-table VirtualisedList\n";
        int n = list.GetActiveCount();
        sb.Append("active=").Append(n).Append('\n');
        for (int i = 0; i < n; i++)
        {
            var item = list.GetActiveItem(i);
            var content = item?.Content;
            if (content == null) continue;
            sb.Append("slot ").Append(i).Append(" vIndex=").Append(item.Index)
              .Append(" text=").Append(FirstText(content, 0) ?? "").Append('\n');
        }
        return sb.ToString();
    }

    private static string FirstText(VisualElement el, int d)
    {
        if (d > 25) return null;
        var t = el.TryCast<TextElement>();
        if (t != null && !string.IsNullOrEmpty(t.text)) return ServerText.Clean(t.text);
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var r = FirstText(el.hierarchy[i], d + 1);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>A row's full binding node: every child name and value, and every property of the
    /// row's DynamicReference - hunting the property that carries the game's true selection index.</summary>
    public static string DumpNode(int row)
    {
        var sb = new StringBuilder();
        var list = FindSquadList();
        if (list == null) return "no squad-table VirtualisedList\n";
        int n = list.GetActiveCount();
        for (int i = 0; i < n; i++)
        {
            var item = list.GetActiveItem(i);
            if (item == null || item.Index != row || item.Content == null) continue;
            var root = FindBindingRoot(item.Content, 0);
            if (root == null) return "row has no BindingRoot\n";
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) return "TryGetNode failed\n";
            sb.Append("row ").Append(row).Append(" node children:\n");
            foreach (var child in node.Children)
            {
                var ck = child.Key;
                var v = bindings.Get(ref ck);
                sb.Append("  ").Append(child.Name).Append(" = ").Append(v == null ? "null" : v.ToString()).Append('\n');
                if (child.Name != "binding" || v == null) continue;
                var reference = v.Get();
                var dynRef = reference?.TryCast<SI.Bindable.DynamicReference>();
                if (dynRef == null) continue;
                var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
                var keys = dynRef.Keys.GetEnumerator();
                while (keys.MoveNext())
                {
                    var k = keys.Current;
                    string name;
                    try { name = reg.GetInfo((uint)k).Name; } catch { name = "?" + k; }
                    string val;
                    try { val = dynRef[k]?.ToString() ?? "null"; } catch (System.Exception ex) { val = "(" + ex.GetType().Name + ")"; }
                    sb.Append("    ref.").Append(name).Append(" = ").Append(val).Append('\n');
                }
            }
            return sb.ToString();
        }
        return "row not active\n";
    }

    private static SI.Bindable.BindingRoot FindBindingRoot(VisualElement el, int d)
    {
        if (d > 8) return null;
        var r = el.TryCast<SI.Bindable.BindingRoot>();
        if (r != null) return r;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var f = FindBindingRoot(el.hierarchy[i], d + 1);
            if (f != null) return f;
        }
        return null;
    }

    public static string Run(int row)
    {
        var sb = new StringBuilder();
        var list = FindSquadList();
        if (list == null) return "no squad-table VirtualisedList (needs >= 15 active rows) on an open panel\n";

        int n = list.GetActiveCount();
        sb.Append("squad list: ").Append(n).Append(" active rows\n");
        if (row < 0) row = 1;
        SI.UI.VirtualisedList.VirtualisedElement item = null;
        for (int i = 0; i < n; i++)
        {
            var cand = list.GetActiveItem(i);
            if (cand != null && cand.Index == row) { item = cand; break; }
        }
        if (item?.Content == null) return sb.Append("row ").Append(row).Append(" not active\n").ToString();

        sb.Append("row ").Append(row).Append(" event owners:\n");
        DumpOwners(item.Content, sb, 0);
        return sb.ToString();
    }

    private static SI.UI.VirtualisedList FindSquadList()
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null) continue;
            SI.UI.VirtualisedList match = null;
            FindListRec(pv.Panel, 0, ref match);
            if (match != null) return match;
        }
        return null;
    }

    private static void FindListRec(VisualElement el, int depth, ref SI.UI.VirtualisedList match)
    {
        if (match != null || depth > 60) return;
        var vl = el.TryCast<SI.UI.VirtualisedList>();
        if (vl != null)
        {
            if (vl.GetActiveCount() >= 15) match = vl;
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) FindListRec(el.hierarchy[i], depth + 1, ref match);
    }

    private static void DumpOwners(VisualElement el, StringBuilder sb, int d)
    {
        if (d > 30) return;
        var btn = el.TryCast<SI.Bindable.SIButton>();
        if (btn != null)
        {
            string tag = el.name;
            if (Adapters.PlayerInstructionsButtonAdapter.IsPlayerInstructionsButton(el)) tag += " <-- PI BUTTON";
            AppendList(el, tag + " (SIButton.Events)", btn.Events, sb);
            AppendList(el, tag + " (SIButton.RightClickEvents)", btn.RightClickEvents, sb);
        }
        var shortcuts = el.TryCast<FM.UI.ContextShortcutsElement>();
        if (shortcuts != null) AppendList(el, el.name + " (ContextShortcuts.ShortcutEvent)", shortcuts.ShortcutEvent, sb);
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpOwners(el.hierarchy[i], sb, d + 1);
    }

    private static void AppendList(VisualElement owner, string label, SI.Bindable.EventList list, StringBuilder sb)
    {
        if (list == null) return;
        var contexts = list.Events;
        if (contexts == null || contexts.Length == 0) return;
        sb.Append("  ").Append(label).Append(":\n");
        for (int i = 0; i < contexts.Length; i++)
        {
            var ctx = contexts[i];
            sb.Append("    [").Append(i).Append("] ").Append(ctx == null ? "null" : ctx.GetIl2CppType().Name);
            var pos = ctx?.TryCast<FM.UI.TacticsPitchPositionEventContext>();
            if (pos != null)
            {
                sb.Append(" panel=").Append(pos.m_panelToOpen)
                  .Append(" teamSelectionIndex=").Append(pos.m_teamSelectionIndex)
                  .Append(" inPossession=").Append(pos.m_inPossession);
            }
            // The FMX bridge: prime it (fills Target and payload from the row's live bindings)
            // and report what it WOULD send, without sending. Dispose the pooled event after.
            var plug = ctx?.TryCast<FM.GamePlugin.EventToPluginContext>();
            if (plug != null)
            {
                try
                {
                    plug.PrepareToSend();
                    sb.Append(" sendable=").Append(plug.IsSendable());
                    var pooled = plug.GetPooledEvent();
                    var evt = pooled?.TryCast<FM.GamePlugin.EventToPlugin>();
                    if (evt != null)
                    {
                        sb.Append(" id=").Append(evt.ID.ToString())
                          .Append(" target=").Append(evt.Target == null ? "null" : evt.Target.GetIl2CppType().Name);
                        evt.Dispose();
                    }
                }
                catch (System.Exception ex)
                {
                    sb.Append(" (prime failed: ").Append(ex.GetType().Name).Append(')');
                }
            }
            sb.Append('\n');
        }
    }
}

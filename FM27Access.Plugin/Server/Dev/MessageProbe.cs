using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the message inbox rows (the subject-line field bug: on slow machines the
/// compose races the rows' async bind and bakes subject-less cells). Dumps, per realised
/// NewsItemButton row, what each named field reads right now — subject, sender, time, status —
/// so a mismatch between the drawn row and the adapter's reads is directly visible.
///
/// GET /msgprobe. ?blank=1 additionally clears the FIRST row's MessageHeadline text (visual
/// only, SetValueWithoutNotify — the binding is not touched and reopening the screen restores
/// it), to exercise the unreadable-subject degrade path on a machine that never loses the race.
/// ?blank=all clears EVERY row's headline, reproducing the field shape (100% of subjects
/// unreadable) that arms the one-shot row snapshot in ScreenWalker. ?settext=... writes the
/// FIRST row's MessageHeadline text (the inverse of blank), to exercise the value-channel heal:
/// blank, compose (marker renders and the cell is tracked), settext, then watch the values
/// event push the subject into the rendered button.
/// Main thread.
/// </summary>
internal static class MessageProbe
{
    internal static string Run(bool blank, string setText = null, bool blankAll = false)
    {
        var sb = new StringBuilder();
        var rows = new List<VisualElement>();
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null) continue;
            Collect(pv.Panel, rows, 0);
        }
        sb.Append("rows=").Append(rows.Count).Append('\n');
        for (int i = 0; i < rows.Count; i++)
        {
            var adapter = new Adapters.MessageButtonAdapter(rows[i]);
            sb.Append("[").Append(i).Append("] subject=").Append(Show(adapter.GetSubject()))
              .Append(" | preview=").Append(Show(adapter.GetPreview()))
              .Append(" | sender=").Append(Show(adapter.GetSender()))
              .Append(" | time=").Append(Show(adapter.GetTime()))
              .Append(" | status=").Append(Show(adapter.GetStatusText()))
              .Append('\n');
        }
        if (blankAll && rows.Count > 0)
        {
            int cleared = 0;
            foreach (var row in rows)
            {
                var h = FindNamed(row, "MessageHeadline", 10)?.TryCast<TextElement>();
                if (h == null) continue;
                h.text = "";
                cleared++;
            }
            sb.Append("blank=all: cleared ").Append(cleared).Append(" of ").Append(rows.Count)
              .Append(" row headlines (visual only)\n");
        }
        else if ((blank || !string.IsNullOrEmpty(setText)) && rows.Count > 0)
        {
            var headline = FindNamed(rows[0], "MessageHeadline", 10);
            var text = headline?.TryCast<TextElement>();
            if (text == null)
            {
                sb.Append("blank/settext: first row has no MessageHeadline TextElement\n");
            }
            else if (blank)
            {
                text.text = "";
                sb.Append("blank: first row's MessageHeadline cleared (visual only)\n");
            }
            else
            {
                text.text = setText;
                sb.Append("settext: first row's MessageHeadline set (visual only)\n");
            }
        }
        return sb.ToString();
    }

    private static string Show(string s)
        => s == null ? "<null>" : "\"" + ServerText.Clean(s) + "\"";

    private static void Collect(VisualElement el, List<VisualElement> rows, int depth)
    {
        if (depth > 70 || !ElementBridge.IsVisible(el)) return;
        if (el.name == "NewsItemButton") { rows.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], rows, depth + 1);
    }

    private static VisualElement FindNamed(VisualElement root, string name, int depth)
    {
        if (root.name == name) return root;
        if (depth <= 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindNamed(root.hierarchy[i], name, depth - 1);
            if (found != null) return found;
        }
        return null;
    }
}

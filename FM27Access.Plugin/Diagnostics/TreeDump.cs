using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Diagnostics;

/// <summary>
/// The one raw VisualElement-tree formatter. Every "dump the tree" path routes through here:
/// the dev driver's /gui and /panels endpoints, F9's f9_tree.txt file dump, and the startup
/// log dump. They differ only in <see cref="Options"/> (depth, how much per-node detail) and
/// where the resulting text goes (HTTP string, file, or log), not in how the tree is walked.
///
/// Deliberately the RAW tree (IL2CPP type, name, visibility, text) with no classification, so a
/// reader sees exactly what the cleaned surfaces hide. Classification is the separate concern of
/// <see cref="ClassificationDump"/>.
/// </summary>
internal static class TreeDump
{
    /// <summary>Per-call formatting knobs. Defaults match the dev driver's lean /gui dump.</summary>
    public sealed class Options
    {
        public int MaxDepth = 200;
        // 0 means unbounded. The walk stops emitting once this many nodes are written and notes
        // the truncation; a runaway tree can never produce an unbounded response or file.
        public int MaxNodes = 4000;
        public int TextMaxLen = 80;

        // Lean by default (what /gui shows). The file dump turns the rest on for diagnosis.
        public bool ShowDepthIndex = true;   // "[3] " prefix after the indent
        public bool ShowChildCount = false;
        public bool ShowEnabled = false;     // " DISABLED" when !enabledSelf
        public bool ShowOpacity = false;     // " opacity=0.50" when < 1
        public bool ShowInterfaces = false;
        public bool ShowUss = false;

        // When set, per-node counts accumulate here for a stats summary the caller prints.
        public TreeStats Stats;
    }

    /// <summary>Aggregate counts collected during a walk when <see cref="Options.Stats"/> is set.</summary>
    public sealed class TreeStats
    {
        public int TotalElements;
        public int VisibleElements;
        public int HiddenElements;
        public int MaxDepthReached;
        public int TextElementsWithContent;
        public readonly Dictionary<string, int> TypeCounts = new();
    }

    /// <summary>
    /// Walk <paramref name="root"/> depth-first, appending one indented line per element to
    /// <paramref name="sb"/>. Returns true if the walk was truncated by <see cref="Options.MaxNodes"/>.
    /// </summary>
    public static bool Write(VisualElement root, StringBuilder sb, Options opts)
    {
        int count = 0;
        Walk(root, 0, sb, opts, ref count);
        return opts.MaxNodes > 0 && count >= opts.MaxNodes;
    }

    private static void Walk(VisualElement el, int depth, StringBuilder sb, Options opts, ref int count)
    {
        if (depth > opts.MaxDepth) return;
        if (opts.MaxNodes > 0 && count >= opts.MaxNodes) return;
        count++;

        var type = el.GetIl2CppType().Name;
        bool visible = ElementBridge.IsVisible(el);

        if (opts.Stats != null)
        {
            var s = opts.Stats;
            s.TotalElements++;
            if (visible) s.VisibleElements++; else s.HiddenElements++;
            if (depth > s.MaxDepthReached) s.MaxDepthReached = depth;
            s.TypeCounts.TryGetValue(type, out int tc);
            s.TypeCounts[type] = tc + 1;
        }

        sb.Append(' ', depth * 2);
        if (opts.ShowDepthIndex) sb.Append('[').Append(depth).Append("] ");
        sb.Append(type);

        var name = el.name;
        if (!string.IsNullOrEmpty(name)) sb.Append(" name=\"").Append(name).Append('"');

        sb.Append(" visible=").Append(visible);

        if (opts.ShowEnabled && !el.enabledSelf) sb.Append(" DISABLED");
        if (opts.ShowOpacity)
        {
            float opacity = el.resolvedStyle.opacity;
            if (opacity < 1.0f) sb.Append(" opacity=").Append(opacity.ToString("F2"));
        }

        int childCount = el.hierarchy.childCount;
        if (opts.ShowChildCount) sb.Append(" children=").Append(childCount);

        var text = NodeText(el);
        if (text != null)
        {
            if (opts.Stats != null) opts.Stats.TextElementsWithContent++;
            sb.Append(" text=\"").Append(Truncate(text, opts.TextMaxLen)).Append('"');
        }

        // Interfaces / USS are noisy and only the file dump asks for them. Plain layout types
        // carry nothing useful, so skip them even when the flags are on.
        if ((opts.ShowInterfaces || opts.ShowUss) && type != "VisualElement" && type != "TemplateContainer")
        {
            if (opts.ShowInterfaces)
            {
                var ifaces = el.GetIl2CppType().GetInterfaces();
                if (ifaces != null && ifaces.Length > 0)
                {
                    sb.Append(" interfaces=[");
                    for (int i = 0; i < ifaces.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(ifaces[i].Name);
                    }
                    sb.Append(']');
                }
            }
            if (opts.ShowUss)
            {
                var uss = UssClasses(el);
                if (uss.Count > 0) sb.Append(" uss=[").Append(string.Join(",", uss)).Append(']');
            }
        }

        sb.Append('\n');

        for (int i = 0; i < childCount; i++)
        {
            Walk(el.hierarchy[i], depth + 1, sb, opts, ref count);
            if (opts.MaxNodes > 0 && count >= opts.MaxNodes) break;
        }
    }

    // Text on TextElement subclasses, newlines flattened so a multi-line label stays one dump line.
    // Shared by the file dumps (DebugTools) and the classification dump so all three diagnostics
    // read element text the same way. Returns null when there is none.
    internal static string NodeText(VisualElement element)
    {
        var textEl = element.TryCast<TextElement>();
        if (textEl == null) return null;
        var text = textEl.text;
        if (string.IsNullOrEmpty(text)) return null;
        return text.Replace("\r", "").Replace("\n", "\\n");
    }

    private static readonly string[] KnownUss =
    {
        "sibutton", "sicheckbox", "sislider", "sidropdown",
        "sitext", "siprogressbar", "siradialgauge", "sistepper",
        "siimage", "siradiobutton", "sitoggle", "siinput",
        "unity-toggle", "unity-button", "unity-label",
        "unity-text-field", "unity-slider", "unity-foldout",
        "si-tile", "si-card", "selected", "focused", "disabled",
        "active", "hover", "checked", "hidden",
    };

    private static List<string> UssClasses(VisualElement element)
    {
        var classes = new List<string>();
        foreach (var cls in KnownUss)
            if (element.ClassListContains(cls)) classes.Add(cls);
        return classes;
    }

    internal static string Truncate(string s, int max)
    {
        if (s == null) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}

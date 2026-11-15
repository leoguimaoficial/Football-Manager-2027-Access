using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only: how the binding system's own property request (SI.Bindable.BindingUtils.Request with a
/// string path) behaves for a player scatter point's person: whether the value is there at once or
/// arrives later, its form, and what node it leaves behind. GET /namerequest?op=request&amp;path=Name
/// (one request per point of ours, under the point's own node), op=read (re-read the stored keys and
/// list the callbacks seen), op=ids (each of our points' references and node sizes). There is no
/// remove: the game deletes a request's node itself once it answers, and its key is dead after.
///
/// The callback is a static readonly field: the game holds a raw pointer to it
/// (feedback_il2cpp_delegate_lifetime).
/// </summary>
internal static class NameRequestProbe
{
    private static readonly SI.Bindable.Bindings.ValueChangedCallback Callback = new Action<SI.Bindable.Bindings.Key, SI.Core.TypedValue>(OnValue);
    private static readonly List<SI.Bindable.Bindings.Key> _keys = new();
    private static readonly List<string> _seen = new();

    private static void OnValue(SI.Bindable.Bindings.Key key, SI.Core.TypedValue value)
    {
        string text;
        try { text = value == null ? "(null)" : FormChartProbe.DescribeValue(value); }
        catch (Exception e) { text = "describe threw " + e.GetType().Name; }
        lock (_seen) _seen.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " callback: " + text);
    }

    internal static string Run(string op, string path)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var sb = new StringBuilder();
        switch (op)
        {
            case "request":
            {
                if (string.IsNullOrEmpty(path)) path = "Name";
                foreach (var (node, player) in OurPoints())
                {
                    var parent = node.Key;
                    var key = SI.Bindable.BindingUtils.Request(bindings, parent, player, path, null, Callback);
                    _keys.Add(key);
                    sb.Append("requested ").Append(path).Append(" -> now: ").Append(Describe(bindings, key)).Append('\n');
                }
                break;
            }
            case "ids":
                foreach (var (node, person) in OurPoints())
                {
                    var p = BindingRecords.PersonOf(person);
                    sb.Append("CombinedIndexAndType = 0x").Append(p == null ? "null" : p.CombinedIndexAndType.ToString("x8")).Append("  ");
                    var stack = new Stack<SI.Bindable.IReadOnlyNode>();
                    stack.Push(node);
                    while (stack.Count > 0)
                    {
                        var n = stack.Pop();
                        foreach (var c in n.Children)
                        {
                            if (c.Name == "UniqueId")
                                sb.Append(n.Name).Append(".UniqueId = ").Append(BindingRecords.NumberOf(BindingRecords.ValueOf(c))).Append("  ");
                            stack.Push(c);
                        }
                    }
                    sb.Append("| children=").Append(node.ChildCount).Append('\n');
                }
                break;
            case "read":
                foreach (var key in _keys) sb.Append("read: ").Append(Describe(bindings, key)).Append('\n');
                lock (_seen) foreach (var s in _seen) sb.Append(s).Append('\n');
                break;
            default:
                return "op=request&path=Name | op=read | op=ids\n";
        }
        return sb.Length > 0 ? sb.ToString() : "no points of ours found\n";
    }

    private static string Describe(SI.Bindable.BindingSubsystem bindings, SI.Bindable.Bindings.Key key)
    {
        SI.Core.TypedValue tv;
        try { tv = bindings.Get(ref key); }
        catch (Exception e) { return "get threw " + e.GetType().Name; }
        return tv == null ? "(no value)" : FormChartProbe.DescribeValue(tv);
    }

    // Our points on the open player scatter: each point's node and its person value.
    private static List<(SI.Bindable.IReadOnlyNode, SI.Core.TypedValue)> OurPoints()
    {
        var result = new List<(SI.Bindable.IReadOnlyNode, SI.Core.TypedValue)>();
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            var container = ScreenWalker.FindDescendantNamed(root, "scatter_chart");
            if (container == null) continue;
            var points = PlayerScatterPoints.Read(container, out _);
            if (points == null) continue;
            foreach (var p in points)
                if (p.Ours && p.Node != null && p.Person != null) result.Add((p.Node, p.Person));
        }
        return result;
    }
}

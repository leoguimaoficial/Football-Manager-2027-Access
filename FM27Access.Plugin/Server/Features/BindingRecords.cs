using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Typed reads of the game's binding data: the records, lists and numbers a chart or drawing is
/// bound to, which hold the figures the screen only draws. One path for every reader (radar, xG
/// story, match momentum, pass maps), so the IL2CPP-sensitive parts live in one place:
/// numbers are read as <c>SI.Core.DynamicNumber</c> and never parsed from text (whose decimal mark
/// follows the game's culture); references are typed casts; nothing is reflected.
///
/// Main thread only. Every read is live; nothing here caches game state.
/// </summary>
internal static class BindingRecords
{
    private static SI.Bindable.BindingSubsystem Bindings => SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();

    /// <summary>The binding node behind a BindingRoot element, or null.</summary>
    internal static SI.Bindable.IReadOnlyNode NodeOf(VisualElement el)
    {
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root == null) return null;
        var key = root.Key;
        return Bindings.TryGetNode(ref key, out var node) ? node : null;
    }

    /// <summary>The first node named <paramref name="name"/> below <paramref name="node"/>, depth-limited.</summary>
    internal static SI.Bindable.IReadOnlyNode FindNode(SI.Bindable.IReadOnlyNode node, string name, int depth)
    {
        if (depth < 0) return null;
        foreach (var child in node.Children)
        {
            if (child.Name == name) return child;
            var found = FindNode(child, name, depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    internal static SI.Core.TypedValue ValueOf(SI.Bindable.IReadOnlyNode node)
    {
        var key = node.Key;
        return Bindings.Get(ref key);
    }

    // Actual native record identity, independent of mutable captions/values or
    // virtualized display indices. Context identifiers are valid pairs even
    // when they do not belong to the property-name registry.
    internal static string ReferenceIdentity(SI.Interop.InteropReference reference)
    {
        if (reference?.m_id == null || reference.m_id.Count == 0) return null;
        var pairs = new List<string>();
        for (int i = 0; i < reference.m_id.Count; i++)
        {
            var pair = reference.m_id[i];
            pairs.Add(pair.ID.ToString(System.Globalization.CultureInfo.InvariantCulture) + "="
                + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        pairs.Sort(StringComparer.Ordinal);
        return reference.GetIl2CppType().FullName + "|" + string.Join(";", pairs);
    }

    // Inline table records can contain real references alongside mutable display
    // values and context keys absent from the property-name registry. Retain the
    // actual reference fields by their native numeric keys; resolving every key
    // to a name is neither required for identity nor valid for those records.
    internal static string InlineReferenceIdentity(SI.Core.TypedValue binding)
    {
        var record = RecordOf(binding);
        if (record == null) return null;
        var references = new List<string>();
        var fields = record.GetEnumerator();
        while (fields.MoveNext())
        {
            var field = fields.Current;
            var identity = ReferenceIdentity(field.Value?.Get()?.TryCast<SI.Interop.InteropReference>());
            if (identity != null)
                references.Add(field.Key.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + identity);
        }
        references.Sort(StringComparer.Ordinal);
        return references.Count == 0 ? null : "inline|" + string.Join("|", references);
    }

    // FM27 chart/map components expose the exact resolved dataset key they draw.
    // Prefer it to guessing a renamed observation somewhere above their template.
    internal static SI.Core.TypedValue WidgetValue(VisualElement element)
    {
        if(element.GetIl2CppType().Name=="FMScatterChart")
        {
            // FM27 no longer places its graph record under a BindingRoot.
            // Read the actual typed widget handler's resolved request instead
            // of searching unrelated ancestor records by name.
            var chart=element.TryCast<FM.UI.FMScatterChart>();
            var handler=chart?.m_bindingHandler?.Cast<Il2CppSystem.Object>()
                ?.TryCast<SI.Bindable.BindingHandler<FM.UI.FMScatterChart>>();
            if(handler?.IsBound!=true||handler.m_binding.Kind!=SI.Bindable.BindingMethodKind.VisualFunction)return null;
            var bag=handler.m_state?.VisualFunction?.Bag;
            SI.Core.TypedValue dataset=null;var seen=new HashSet<IntPtr>();
            for(var request=bag?.m_head;request!=null;request=request.Next)
            {
                if(!seen.Add(request.Pointer)||seen.Count>128)return null;
                if(request.m_kind!=SI.Bindable.BindingBag.Request.Kind.Binding)continue;
                var value=request.Value;var record=RecordOf(value);if(record==null)continue;
                var fields=Fields(record);
                if(!fields.ContainsKey("XaxisData")||!fields.ContainsKey("YaxisData")||!fields.ContainsKey("CombinedRecordList"))continue;
                if(dataset!=null)return null;
                dataset=value;
            }
            return dataset;
        }
        SI.Bindable.Bindings.Key key;
        switch (element.GetIl2CppType().Name)
        {
            case "FMXGMatchStoryChart": key = element.Cast<FM.UI.FMXGMatchStoryChart>().m_xgDatasetKey; break;
            case "FMMatchMomentumChart": key = element.Cast<FM.UI.FMMatchMomentumChart>().m_matchMomentumDatasetKey; break;
            case "FMPassMapPanel": key = element.Cast<FM.UI.FMPassMapPanel>().m_bindingDataKey; break;
            case "FMAveragePositionsMap": key = element.Cast<FM.UI.FMAveragePositionsMap>().m_bindingDataKey; break;
            case "FMActionZonesPanel": key = element.Cast<FM.UI.FMActionZonesPanel>().m_bindingDataKey; break;
            case "FMHeatMap": key = element.Cast<FM.UI.FMHeatMap>().m_bindingDataKey; break;
            default: return null;
        }
        return Bindings.Get(ref key);
    }

    /// <summary>
    /// The record bound at the nearest node named <paramref name="nodeName"/> under any BindingRoot
    /// above <paramref name="from"/>: the observation a Data Hub card is built from. Null when none.
    /// </summary>
    internal static SI.Core.Record RecordAbove(VisualElement from, string nodeName, int ancestorBudget = 40, int depth = 4)
    {
        var up = from.hierarchy.parent;
        for (int i = 0; i < ancestorBudget && up != null; i++, up = up.hierarchy.parent)
        {
            var node = NodeOf(up);
            if (node == null) continue;
            var found = FindNode(node, nodeName, depth);
            if (found != null) return RecordOf(ValueOf(found));
        }
        return null;
    }

    /// <summary>A record's fields by property name.</summary>
    internal static Dictionary<string, SI.Core.TypedValue> Fields(SI.Core.Record rec)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var fields = new Dictionary<string, SI.Core.TypedValue>(StringComparer.Ordinal);
        var en = rec.GetEnumerator();
        while (en.MoveNext())
        {
            var kv = en.Current;
            fields[reg.GetInfo(kv.Key).Name] = kv.Value;
        }
        return fields;
    }

    internal static SI.Core.TypedValue Field(Dictionary<string, SI.Core.TypedValue> fields, string name)
        => fields.TryGetValue(name, out var v) ? v : null;

    internal static SI.Core.Record RecordOf(SI.Core.TypedValue tv) => tv?.Get()?.TryCast<SI.Core.Record>();

    internal static Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> ListOf(SI.Core.TypedValue tv)
        => tv?.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();

    internal static string TextOf(SI.Core.TypedValue tv) => tv?.AsString();

    /// <summary>A numeric binding (an <c>SI.Core.DynamicNumber</c>), or null for anything else.</summary>
    internal static double? NumberOf(SI.Core.TypedValue tv)
    {
        var inner = tv?.Get();
        if (inner == null || !inner.GetIl2CppType().Equals(Il2CppInterop.Runtime.Il2CppType.Of<SI.Core.DynamicNumber>()))
            return null;
        return inner.Unbox<SI.Core.DynamicNumber>().GetDouble();
    }

    /// <summary>A boolean binding, or null for anything else.</summary>
    internal static bool? FlagOf(SI.Core.TypedValue tv)
    {
        var inner = tv?.Get();
        if (inner == null || !inner.GetIl2CppType().Equals(Il2CppInterop.Runtime.Il2CppType.Of<bool>()))
            return null;
        return inner.Unbox<bool>();
    }

    internal static FM.UI.PersonReference PersonOf(SI.Core.TypedValue tv) => tv?.Get()?.TryCast<FM.UI.PersonReference>();

    internal static FM.UI.TeamReference TeamOf(SI.Core.TypedValue tv) => tv?.Get()?.TryCast<FM.UI.TeamReference>();
}

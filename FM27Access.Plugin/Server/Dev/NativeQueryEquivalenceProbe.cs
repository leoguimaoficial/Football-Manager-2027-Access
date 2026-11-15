using System;
using System.Collections.Generic;
using System.Linq;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

// Readonly oracle for replacing managed interop walks with Unity's own query.
// Both paths read the same native frame; no state, binding or selection is changed.
internal static class NativeQueryEquivalenceProbe
{
    internal static string Compare()
    {
        var result = new List<object>();
        foreach (var view in InteractivePanels.Current())
        {
            CompareType<BindableTextEditBox>(view.Panel, null, result);
            CompareType<TabbedGridLayoutElement>(view.Panel, null, result);
            CompareType<StreamedObjectList>(view.Panel, "FixturesList", result);
            CompareType<SIText>(view.Panel, "CurrentStep", result);
            CompareType<SIText>(view.Panel, "text", result);
        }
        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    private static void CompareType<T>(VisualElement root, string name, List<object> result) where T : VisualElement
    {
        var old = new List<IntPtr>();
        void Walk(VisualElement element)
        {
            if (!ElementBridge.IsVisible(element)) return;
            var match = element.TryCast<T>();
            if (match != null && (name == null || element.name == name)) old.Add(element.Pointer);
            for (int i = 0; i < element.hierarchy.childCount; i++) Walk(element.hierarchy[i]);
        }
        Walk(root);
        var current = NativeTreeQuery.All<T>(root, name)
            .Where(ElementBridge.IsVisibleInHierarchy).Select(x => x.Pointer).ToArray();
        result.Add(new { panel = root.name, type = typeof(T).Name, name, count = old.Count,
            equal = old.SequenceEqual(current) });
    }
}

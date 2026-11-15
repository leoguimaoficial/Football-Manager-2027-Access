using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

// One current native query, materialized only for its caller's Unity dispatch.
// Never cache these elements across frames or replace the caller's visibility,
// owner, binding or uniqueness checks. Unity walks its own tree without a managed
// IL2CPP round trip for every unrelated descendant.
internal static class NativeTreeQuery
{
    internal static List<T> All<T>(VisualElement root, string name = null) where T : VisualElement
    {
        var result = new List<T>();
        if (root == null) return result;
        var own = root.TryCast<T>();
        if (own != null && (name == null || own.name == name)) result.Add(own);
        var native = root.Query<T>(name: name, className: null).ToList();
        for (int i = 0; i < native.Count; i++)
        {
            var element = native[i];
            if (own != null && element.Pointer == own.Pointer) continue;
            result.Add(element);
        }
        return result;
    }
}

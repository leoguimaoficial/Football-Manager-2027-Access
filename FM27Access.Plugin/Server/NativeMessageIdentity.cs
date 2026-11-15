using System;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// Row and reading-pane aliases both resolve to the game's NewsItemReference.
// Read its actual identifier; subject/sender/time are not unique message keys.
internal static class NativeMessageIdentity
{
    // Recycled rows move within the same list's realised child window. Their
    // physical row path is not record identity: keep the list's logical owner
    // and actual NewsItem id while the message stays available in that window.
    internal static VisualElement ListOwner(VisualElement element)
    {
        for (var up = element; up != null; up = up.hierarchy.parent)
            if (up.TryCast<SI.UI.VirtualisedList>() != null) return up;
        return element;
    }

    internal static int? Read(VisualElement element)
    {
        try
        {
            for (var up = element; up != null; up = up.hierarchy.parent)
            {
                var root = BindingRecords.NodeOf(up);
                if (root == null) continue;
                foreach (var child in root.Children)
                {
                    if (child.Name != "NewsItem") continue;
                    var reference = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.NewsItemReference>();
                    if (reference?.m_id == null) continue;
                    var properties = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
                    for (int i = 0; i < reference.m_id.Count; i++)
                    {
                        var pair = reference.m_id[i];
                        // Interop references also contain context identifiers,
                        // which are not entries in the property-name registry.
                        if (properties.HasID(pair.ID) && properties.GetInfo(pair.ID).Name == "UniqueId" && pair.Value > 0)
                            return pair.Value;
                    }
                }
            }
        }
        catch (Exception ex) { Speech.Log.Warn($"[MessageIdentity] Native message identifier unavailable: {ex.GetType().Name}: {ex.Message}"); }
        return null;
    }
}

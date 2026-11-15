using System.Reflection;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// FM26/earlier FM27 expose one applied class; current FM27 exposes a list.
// Read the native controller's actual applied classes, never guessed colours.
internal static class StyleSetterClasses
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo Single = typeof(SI.Bindable.SIStyleSetter).GetProperty("m_addedClass", Flags);
    private static readonly PropertyInfo Multiple = typeof(SI.Bindable.SIStyleSetter).GetProperty("m_addedClasses", Flags);

    internal static string Read(VisualElement element)
    {
        var setter = element?.TryCast<SI.Bindable.SIStyleSetter>();
        if (setter == null) return null;
        if (Single != null) return Single.GetValue(setter) as string;
        if (Multiple == null) throw new MissingMemberException(typeof(SI.Bindable.SIStyleSetter).FullName, "m_addedClass/m_addedClasses");
        var classes = Multiple.GetValue(setter) as Il2CppSystem.Collections.Generic.List<string>;
        if (classes == null) return null;
        var values = new List<string>(classes.Count);
        for (int i = 0; i < classes.Count; i++) values.Add(classes[i]);
        return string.Join(" ", values);
    }
}

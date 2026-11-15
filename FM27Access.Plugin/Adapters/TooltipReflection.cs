using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal static class TooltipReflection
{
    private static bool _initialized;
    private static PropertyInfo _manipulatorProp;
    private static MethodInfo _showMethod;
    private static MethodInfo _hideMethod;
    private static PropertyInfo _sVisualsProp;
    private static PropertyInfo _sFloatingElementProp;
    private static PropertyInfo _floatingContentContainerProp;

    internal static bool IsAvailable =>
        _manipulatorProp != null && _showMethod != null && _hideMethod != null &&
        _sVisualsProp != null && _sFloatingElementProp != null && _floatingContentContainerProp != null;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var tooltipType = typeof(SI.Bindable.Tooltip);
        var allFlags = BindingFlags.Public | BindingFlags.NonPublic |
                       BindingFlags.Instance | BindingFlags.Static;

        _manipulatorProp = tooltipType.GetProperty("m_manipulator", allFlags);
        if (_manipulatorProp == null)
        {
            Speech.Log.Warn("[TooltipReflection] Tooltip.m_manipulator not found.");
            return;
        }

        var manipType = _manipulatorProp.PropertyType;

        _showMethod = manipType.GetMethod("Show", allFlags, null, Type.EmptyTypes, null);
        if (_showMethod == null)
            Speech.Log.Warn("[TooltipReflection] TooltipManipulator.Show not found.");

        _hideMethod = manipType.GetMethod("Hide", allFlags, null, Type.EmptyTypes, null);
        if (_hideMethod == null)
            Speech.Log.Warn("[TooltipReflection] TooltipManipulator.Hide not found.");

        _sVisualsProp = tooltipType.GetProperty("s_visuals", allFlags);
        if (_sVisualsProp == null)
        {
            Speech.Log.Warn("[TooltipReflection] Tooltip.s_visuals not found.");
            return;
        }

        var visualsType = _sVisualsProp.PropertyType;
        _sFloatingElementProp = visualsType.GetProperty("s_floatingElement", allFlags);
        if (_sFloatingElementProp == null)
            Speech.Log.Warn("[TooltipReflection] Visuals.s_floatingElement not found.");

        if (_sFloatingElementProp != null)
        {
            var feType = _sFloatingElementProp.PropertyType;
            _floatingContentContainerProp = feType.GetProperty("ContentContainer",
                BindingFlags.Public | BindingFlags.Instance);
            if (_floatingContentContainerProp == null)
                Speech.Log.Warn("[TooltipReflection] FloatingElement.ContentContainer not found.");
        }

        Speech.Log.Info($"[TooltipReflection] Init OK: available={IsAvailable}");
    }

    internal static object GetManipulator(SI.Bindable.Tooltip tooltip)
    {
        return _manipulatorProp.GetValue(tooltip);
    }

    internal static void Show(object manipulator)
    {
        _showMethod.Invoke(manipulator, null);
    }

    internal static void Hide(object manipulator)
    {
        _hideMethod.Invoke(manipulator, null);
    }

    internal static string TryReadFloatingContent()
    {
        var visuals = _sVisualsProp.GetValue(null);
        if (visuals == null) return null;

        var floatingElement = _sFloatingElementProp.GetValue(null);
        if (floatingElement == null) return null;

        var contentContainer = (VisualElement)_floatingContentContainerProp.GetValue(floatingElement);
        if (contentContainer == null) return null;

        var texts = new List<string>();
        CollectAllTexts(contentContainer, texts);

        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    private static void CollectAllTexts(VisualElement root, List<string> texts)
    {
        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
            {
                texts.Add(textEl.text);
                return;
            }
        }
        catch { }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectAllTexts(root.hierarchy[i], texts);
    }
}

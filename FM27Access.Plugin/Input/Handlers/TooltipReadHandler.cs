using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation.Input;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Briefly pushes onto the stack to trigger a tooltip, read its content, and speak it.
/// Auto-pops after reading or after a timeout.
/// </summary>
internal class TooltipReadHandler : IInputHandler
{
    private readonly HandlerStack _stack;
    private readonly Adapters.InfoButtonAdapter _adapter;
    private int _tickCount;
    private bool _triggered;

    private static readonly KeyboardKey[] _claimedKeys = Array.Empty<KeyboardKey>();

    private static bool _reflectionInitialized;
    private static PropertyInfo _manipulatorProp;
    private static MethodInfo _manipulatorShowMethod;
    private static MethodInfo _manipulatorHideMethod;
    private static PropertyInfo _sVisualsProp;
    private static PropertyInfo _sFloatingElementProp;
    private static PropertyInfo _floatingContentContainerProp;

    public TooltipReadHandler(HandlerStack stack, Adapters.InfoButtonAdapter adapter)
    {
        _stack = stack;
        _adapter = adapter;
        EnsureReflection();
    }

    public string DisplayName => "Tooltip";
    public bool CapturesAllInput => false;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => _claimedKeys;

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers) => false;

    public void OnActivate()
    {
        TriggerTooltip();
    }

    public void OnDeactivate() { }

    public bool Tick()
    {
        if (!_triggered)
        {
            _stack.Pop();
            return false;
        }

        _tickCount++;

        if (_tickCount < 5)
            return false;

        ReadAndSpeak();
        HideTooltip();
        _stack.Pop();
        return false;
    }

    private void TriggerTooltip()
    {
        if (_manipulatorProp == null || _manipulatorShowMethod == null)
        {
            Speech.SpeechPipeline.SpeakInterrupt("tooltip not available");
            return;
        }

        var tooltip = _adapter.FindTooltip();
        if (tooltip == null)
        {
            Speech.Log.Warn("[TooltipRead] Could not find Tooltip element.");
            Speech.SpeechPipeline.SpeakInterrupt("no tooltip found");
            return;
        }

        try
        {
            var manipulator = _manipulatorProp.GetValue(tooltip);
            if (manipulator == null)
            {
                Speech.Log.Warn("[TooltipRead] m_manipulator is null.");
                Speech.SpeechPipeline.SpeakInterrupt("tooltip not available");
                return;
            }

            _manipulatorShowMethod.Invoke(manipulator, null);
            _triggered = true;
            Speech.Log.Info("[TooltipRead] Triggered tooltip via manipulator.Show().");
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[TooltipRead] Failed to trigger tooltip: {e.GetType().Name}: {e.Message}");
            Speech.SpeechPipeline.SpeakInterrupt("tooltip failed");
        }
    }

    private void ReadAndSpeak()
    {
        try
        {
            var visuals = _sVisualsProp.GetValue(null);
            if (visuals == null)
            {
                Speech.Log.Warn("[TooltipRead] s_visuals still null after Show.");
                Speech.SpeechPipeline.SpeakInterrupt("tooltip empty");
                return;
            }

            var floatingElement = _sFloatingElementProp.GetValue(null);
            if (floatingElement == null)
            {
                Speech.Log.Warn("[TooltipRead] s_floatingElement is null.");
                Speech.SpeechPipeline.SpeakInterrupt("tooltip empty");
                return;
            }

            var contentContainer = (VisualElement)_floatingContentContainerProp.GetValue(floatingElement);
            if (contentContainer == null)
            {
                Speech.SpeechPipeline.SpeakInterrupt("tooltip empty");
                return;
            }

            var texts = new List<string>();
            CollectAllTexts(contentContainer, texts);

            if (texts.Count == 0)
            {
                Speech.SpeechPipeline.SpeakInterrupt("tooltip empty");
                return;
            }

            var content = string.Join(". ", texts);
            Speech.Log.Info($"[TooltipRead] Content: {content}");
            Speech.SpeechPipeline.SpeakInterrupt(content);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[TooltipRead] Failed to read tooltip: {e.GetType().Name}: {e.Message}");
            Speech.SpeechPipeline.SpeakInterrupt("tooltip read failed");
        }
    }

    private void HideTooltip()
    {
        try
        {
            var tooltip = _adapter.FindTooltip();
            if (tooltip == null) return;

            var manipulator = _manipulatorProp.GetValue(tooltip);
            if (manipulator != null)
                _manipulatorHideMethod.Invoke(manipulator, null);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[TooltipRead] Failed to hide tooltip: {e.GetType().Name}: {e.Message}");
        }
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

    private static void EnsureReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        var tooltipType = typeof(SI.Bindable.Tooltip);
        var allFlags = BindingFlags.Public | BindingFlags.NonPublic |
                       BindingFlags.Instance | BindingFlags.Static;

        // Tooltip.m_manipulator (instance property)
        _manipulatorProp = tooltipType.GetProperty("m_manipulator", allFlags);
        if (_manipulatorProp == null)
        {
            Speech.Log.Warn("[TooltipRead] Tooltip.m_manipulator not found.");
            return;
        }

        var manipType = _manipulatorProp.PropertyType;

        _manipulatorShowMethod = manipType.GetMethod("Show", allFlags,
            null, Type.EmptyTypes, null);
        if (_manipulatorShowMethod == null)
            Speech.Log.Warn("[TooltipRead] TooltipManipulator.Show not found.");

        _manipulatorHideMethod = manipType.GetMethod("Hide", allFlags,
            null, Type.EmptyTypes, null);
        if (_manipulatorHideMethod == null)
            Speech.Log.Warn("[TooltipRead] TooltipManipulator.Hide not found.");

        // Tooltip.s_visuals (static property) — for reading content after Show
        _sVisualsProp = tooltipType.GetProperty("s_visuals", allFlags);
        if (_sVisualsProp == null)
        {
            Speech.Log.Warn("[TooltipRead] Tooltip.s_visuals not found.");
            return;
        }

        // Visuals.s_floatingElement (static property)
        var visualsType = _sVisualsProp.PropertyType;
        _sFloatingElementProp = visualsType.GetProperty("s_floatingElement", allFlags);
        if (_sFloatingElementProp == null)
            Speech.Log.Warn("[TooltipRead] Visuals.s_floatingElement not found.");

        // FloatingElement.ContentContainer
        if (_sFloatingElementProp != null)
        {
            var feType = _sFloatingElementProp.PropertyType;
            _floatingContentContainerProp = feType.GetProperty("ContentContainer",
                BindingFlags.Public | BindingFlags.Instance);
            if (_floatingContentContainerProp == null)
                Speech.Log.Warn("[TooltipRead] FloatingElement.ContentContainer not found.");
        }

        Speech.Log.Info($"[TooltipRead] Reflection OK: manipulator={_manipulatorProp != null}, " +
            $"Show={_manipulatorShowMethod != null}, Hide={_manipulatorHideMethod != null}, " +
            $"s_visuals={_sVisualsProp != null}, s_floatingElement={_sFloatingElementProp != null}, " +
            $"ContentContainer={_floatingContentContainerProp != null}");
    }
}

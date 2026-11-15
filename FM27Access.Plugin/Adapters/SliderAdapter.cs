using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class SliderAdapter : PluginAdapterBase
{
    public SliderAdapter(VisualElement element) : base(element) { }

    public override string Role => "slider";
    public override bool CanActivate => false;

    public override string GetLabel()
    {
        var label = Element.Cast<Slider>().label;
        if (!string.IsNullOrEmpty(label))
            return label;

        // This is a typed native timeline, not a form row. During playback a
        // nearby commentary bubble can satisfy the ordinary field-label search.
        // Its own BindingExpect caption must win over that unrelated text.
        var timelineCaption=MatchTimelineInput.Caption(Element);
        if(!string.IsNullOrWhiteSpace(timelineCaption))return timelineCaption;

        // The row's field label names the slider when it has no native label, instead of falling
        // through to the internal element name.
        var fieldLabel = FindFieldLabel();
        if (fieldLabel != null) return fieldLabel;

        return FindChildText() ?? Element.name;
    }

    internal override IReadOnlyList<string> FoldedTexts()
    {
        // Both of the row's texts fold into the slider: the label names it and the value label
        // ("0.75x") is carried in its state, so neither should read as a stray line beside an
        // already-complete control.
        var list = new List<string>();
        if(MatchTimelineInput.IsTimeline(Element))return list;
        var fl = FindFieldLabel();
        if (!string.IsNullOrEmpty(fl)) list.Add(fl);
        var rowTexts = FindRowTexts();
        if (rowTexts != null)
            foreach (var t in rowTexts)
                if (!list.Contains(t)) list.Add(t);
        return list;
    }

    public override string GetState()
    {
        // The game zeroes and disables a slider that does not apply (Camera Height/Zoom under a
        // fixed camera); its 0-to-0 range is meaningless, so the state is just the truth.
        if (!Element.enabledInHierarchy) return "disabled";

        if(MatchTimelineInput.IsTimeline(Element)&&GetValueText() is { } clock)return clock;

        var slider = Element.Cast<Slider>();
        var value = slider.value;
        var low = slider.lowValue;
        var high = slider.highValue;

        var siSlider = Element.Cast<SI.Bindable.SISlider>();
        string raw = siSlider.HasSteps && siSlider.StepSize >= 1.0f && siSlider.StepSize % 1.0f == 0f
            ? $"{(int)value} of {(int)low} to {(int)high}"
            : $"{value:F1} of {low:F1} to {high:F1}";

        // Lead with the row's value label ("0.75x") — the slider's real meaning; the raw position
        // stays so the player knows where they are in the range.
        var valueText = GetValueText();
        return valueText == null ? raw : valueText + ", " + raw;
    }

    public override void Activate() =>
        throw new NotSupportedException("Slider adjustment is directional; handled by the input system.");

    public override string GetDescription()=>MatchTimelineInput.IsTimeline(Element)?MatchTimelineInput.Help:base.GetDescription();
    internal bool IsTimeline=>MatchTimelineInput.IsTimeline(Element);

    public (float Value, float Low, float High, float Step) GetRange()
    {
        var slider = Element.Cast<Slider>();
        float step = 0f;
        var siSlider = Element.Cast<SI.Bindable.SISlider>();
        if (siSlider.HasSteps)
            step = siSlider.StepSize;
        return (slider.value, slider.lowValue, slider.highValue, step);
    }

    /// <summary>Sets the slider, returning the value it actually took after clamping/snapping.</summary>
    public float SetValue(float value)
    {
        var slider = Element.Cast<Slider>();
        if (value < slider.lowValue) value = slider.lowValue;
        if (value > slider.highValue) value = slider.highValue;
        slider.value = value;
        return slider.value;
    }

    /// <summary>
    /// The row's live value label ("0.75x", "40x"): the game's settings slider template pairs the
    /// slider with a label SIText and a value SIText in a sibling frame, and the value label
    /// updates in the same frame as a value write (verified live). Null when this slider sits in
    /// no such row — callers fall back to the raw numeric value, which is complete, just less
    /// friendly.
    /// </summary>
    public string GetValueText()
    {
        var clock=MatchTimelineInput.ValueText(Element);
        if(clock!=null)return clock;
        var texts = FindRowTexts();
        return texts != null && texts.Count >= 2 ? texts[texts.Count - 1] : null;
    }

    // The label+value texts of this slider's settings row, or null. Walks a few levels up looking
    // for a sibling branch holding at least two visible leaf texts (the row's label frame); the
    // slider's own template subtree contains no visible text, so the first such branch is the
    // row pair. Read live on every call, never cached.
    private List<string> FindRowTexts()
    {
        try
        {
            var node = Element;
            for (int up = 0; up < 4; up++)
            {
                var parent = node.hierarchy.parent;
                if (parent == null) return null;
                for (int i = 0; i < parent.hierarchy.childCount; i++)
                {
                    var texts = new List<string>();
                    CollectVisibleLeafTexts(parent.hierarchy[i], texts);
                    if (texts.Count >= 2) return texts;
                }
                node = parent;
            }
            return null;
        }
        catch (Exception ex)
        {
            Speech.Degrade.Note("[SliderAdapter] row text read failed: " + ex.Message);
            return null;
        }
    }

    private static void CollectVisibleLeafTexts(VisualElement root, List<string> texts)
    {
        if (texts.Count >= 4 || !ElementBridge.IsVisible(root)) return;
        var t = root.TryCast<TextElement>();
        if (t != null)
        {
            if (!string.IsNullOrEmpty(t.text)) texts.Add(t.text);
            return;
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
            CollectVisibleLeafTexts(root.hierarchy[i], texts);
    }
}


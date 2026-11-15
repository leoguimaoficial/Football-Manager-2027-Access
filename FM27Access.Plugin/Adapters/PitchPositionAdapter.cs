using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class PitchPositionAdapter : PluginAdapterBase
{
    public PitchPositionAdapter(VisualElement element) : base(element) { }

    public override string Role => "toggle";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        var positionText = FindByName(Element, "PositionText");
        if (positionText != null)
        {
            var textEl = positionText.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }

        var indicator = ElementBridge.FindAncestorByTypeName(Element, "FMPitchIndicator");
        if (indicator != null && !string.IsNullOrEmpty(indicator.name))
            return indicator.name;

        return "unknown position";
    }

    public override string GetState() => IsSelected() ? "selected" : "not selected";

    public override void Activate()
    {
        Element.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
    }

    private bool IsSelected()
    {
        var dataPoint = FindByName(Element, "input-squad-planner-data-point-regular");
        if (dataPoint == null)
        {
            Speech.Log.Warn("[PitchPositionAdapter] Could not find input-squad-planner-data-point-regular child.");
            return false;
        }

        var bg = dataPoint.resolvedStyle.backgroundColor;
        return bg.a > 0.9f && (bg.r > 0.1f || bg.g > 0.1f || bg.b > 0.1f);
    }

    public static bool IsPitchPositionButton(VisualElement element)
    {
        var current = element.hierarchy.parent;
        bool foundIndicator = false;
        for (int i = 0; i < 10 && current != null; i++)
        {
            var typeName = current.GetIl2CppType().Name;
            if (typeName == "FMPitchIndicator")
                foundIndicator = true;
            if (typeName == "FMTacticsPanel")
            {
                if (!foundIndicator) return false;
                // Only match recruitment focus context, not regular tactics
                return IsPositionPickerPitch(current);
            }
            current = current.hierarchy.parent;
        }
        return false;
    }

    /// <summary>
    /// True when an FMTacticsPanel is a position PICKER (the Recruitment Focus dialog's "choose the
    /// positions" pitch, the transfer-advert pitch) rather than a lineup. The two are the same widget
    /// with opposite meanings: on a lineup each slot holds a player, here each slot is a filter
    /// toggle. The native slot template identifies this shared component even
    /// when FM27 moves it into a renamed block in the focus/advertisement cards.
    /// </summary>
    internal static bool IsPositionPickerPitch(VisualElement pitch)
    {
        return FindByName(pitch, "input-squad-planner-data-point-regular") != null
            && FindByName(pitch, "PositionText") != null;
    }
}

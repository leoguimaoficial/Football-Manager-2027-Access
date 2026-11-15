using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class InfoButtonAdapter : ButtonAdapter
{
    public InfoButtonAdapter(VisualElement element) : base(element) { }

    // A single computed label, not a primary/secondary split.
    protected override bool SplitsLabel => false;

    public override string GetLabel()
    {
        var settingName = FindSettingName();
        if (settingName != null)
            return $"{settingName} info, click for tooltip";
        return "info, click for tooltip";
    }

    public static bool IsInfoButton(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        return parent != null && (parent.name == "ui concepts-more info_small"
            || parent.parent?.name == "InfoIcon"
            // FM27 leaves this wrapper unnamed; its native template identity
            // still identifies the same tooltip-only information widget.
            || parent.parent?.TryCast<TemplateContainer>()?.templateId == "info-widget-small");
    }

    public SI.Bindable.Tooltip FindTooltip()
    {
        // Structure: SIButton → parent "ui concepts-more info_small" → parent TemplateContainer
        //   sibling TemplateContainer → child Tooltip
        var infoContainer = Element.hierarchy.parent;
        if (infoContainer == null) return null;

        var templateContainer = infoContainer.hierarchy.parent;
        if (templateContainer == null) return null;

        int count = templateContainer.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var sibling = templateContainer.hierarchy[i];
            if (sibling == infoContainer) continue;

            // Search this sibling for a Tooltip
            var tooltip = FindTooltipRecursive(sibling);
            if (tooltip != null) return tooltip;
        }

        return null;
    }

    private string FindSettingName()
    {
        // Navigate up to the "Frame" container that holds both the info button
        // and the setting label SIText siblings
        var infoContainer = Element.hierarchy.parent; // "ui concepts-more info_small"
        if (infoContainer == null) return null;

        var templateContainer = infoContainer.hierarchy.parent; // TemplateContainer
        if (templateContainer == null) return null;

        var frame = templateContainer.hierarchy.parent; // "Frame ..."
        if (frame == null) return null;

        // Look for the first SIText sibling in the frame
        int count = frame.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = frame.hierarchy[i];
            if (child == templateContainer) continue;

            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                    return textEl.text;
            }
            catch { }
        }

        return null;
    }

    private static SI.Bindable.Tooltip FindTooltipRecursive(VisualElement root)
    {
        try
        {
            var tooltip = root.TryCast<SI.Bindable.Tooltip>();
            if (tooltip != null) return tooltip;
        }
        catch { }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var result = FindTooltipRecursive(root.hierarchy[i]);
            if (result != null) return result;
        }
        return null;
    }
}

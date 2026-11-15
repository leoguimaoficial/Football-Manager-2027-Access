using System.Collections.Generic;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal sealed class CalendarDayAdapter : PluginAdapterBase
{
    private readonly string _dayName;
    private readonly VisualElement _dayCellRoot;

    public CalendarDayAdapter(VisualElement dayCellRoot, string dayName)
        : base(FindDateText(dayCellRoot) ?? dayCellRoot)
    {
        _dayCellRoot = dayCellRoot;
        _dayName = dayName;
    }

    public override string Role => "calendar day";
    public override bool CanActivate => false;

    public override string GetLabel()
    {
        var dateText = FindDateText(_dayCellRoot);
        string date = dateText != null ? dateText.Cast<TextElement>().text : "unknown";
        return $"{_dayName} {date}";
    }

    public override string GetState()
    {
        if (IsCurrentDate())
            return "current date";
        if (IsPastDate())
            return "already simulated";
        return "";
    }

    public override string GetAnnouncement()
    {
        var label = GetLabel();
        var events = CollectEventSummary();
        var state = GetState();

        var parts = new List<string> { label };
        if (!string.IsNullOrEmpty(state))
            parts.Add(state);
        if (!string.IsNullOrEmpty(events))
            parts.Add(events);

        return string.Join(", ", parts);
    }

    public override void Activate() { }

    internal VisualElement DayCellRoot => _dayCellRoot;

    private string CollectEventSummary()
    {
        var eventsList = FindByName(_dayCellRoot, "EventsList");
        if (eventsList == null) return null;

        var texts = new List<string>();
        CollectEventTexts(eventsList, texts);
        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    private static void CollectEventTexts(VisualElement root, List<string> texts)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;

            var typeName = child.GetIl2CppType().Name;

            if (typeName == "SIButton" || typeName == "Button")
            {
                var buttonText = CollectButtonLabel(child);
                if (!string.IsNullOrEmpty(buttonText))
                    texts.Add(buttonText);
                continue;
            }

            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                {
                    texts.Add(textEl.text);
                    continue;
                }
            }
            catch { }

            CollectEventTexts(child, texts);
        }
    }

    private static string CollectButtonLabel(VisualElement button)
    {
        var texts = new List<string>();
        CollectAllTextsRecursive(button, texts);
        if (texts.Count > 0)
            return string.Join(", ", texts);

        var name = button.name;
        if (!string.IsNullOrEmpty(name) && name != "FMVisual" && !name.Contains('-'))
            return name;

        return "event";
    }

    private static void CollectAllTextsRecursive(VisualElement root, List<string> texts)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                {
                    texts.Add(textEl.text);
                    continue;
                }
            }
            catch { }
            CollectAllTextsRecursive(child, texts);
        }
    }

    private bool IsCurrentDate()
    {
        var header = FindByName(_dayCellRoot, "Header");
        if (header == null) return false;

        var gradientVisible = FindByName(header, "GradientHeaderVisible");
        if (gradientVisible == null) return false;

        if (!ElementBridge.IsVisible(gradientVisible)) return false;

        int childCount = gradientVisible.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = gradientVisible.hierarchy[i];
            if (ElementBridge.IsVisible(child) && child.GetIl2CppType().Name == "SIGradient")
                return true;
        }

        return false;
    }

    private bool IsPastDate()
    {
        var body = FindByName(_dayCellRoot, "Body");
        if (body == null) return false;

        try
        {
            return body.resolvedStyle.opacity < 0.9f;
        }
        catch
        {
            return false;
        }
    }

    private static VisualElement FindDateText(VisualElement dayCellRoot)
        => Contexts.CalendarTree.FindDateTextElement(dayCellRoot);

    private static VisualElement FindByNameStatic(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.name == name)
                return child;
            var deeper = FindByNameStatic(child, name);
            if (deeper != null)
                return deeper;
        }
        return null;
    }
}

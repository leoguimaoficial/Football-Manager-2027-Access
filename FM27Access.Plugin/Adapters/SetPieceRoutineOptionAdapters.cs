using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The set-piece screen's routine strip draws one always-present "active" hero toggle (number
/// display flipped per routine, name text, management dropdown) plus per-slot inactive
/// representations: a number chip beside a textless <c>SIButton</c> that switches to that
/// routine. These two adapters give the strip radio semantics for the HTML surface: the active
/// routine reads checked with its number and name; an inactive routine reads as
/// "&lt;strip label&gt; &lt;number&gt;" ("Routine 2") — the game itself shows only the number
/// until a routine is selected, so the number is all the honest label there is.
/// All text is read live from the elements at call time, never cached.
/// </summary>
internal sealed class ActiveRoutineOptionAdapter : PluginAdapterBase
{
    public ActiveRoutineOptionAdapter(VisualElement element) : base(element) { }

    public override string Role => "radio button";
    public override bool CanActivate => true;
    public override string GetState() => "selected";

    // The already-selected routine has no clickable element (the game removes the button from
    // the active representation), and a checked radio needs no action — selecting it again is
    // a no-op by definition, not a failure.
    public override void Activate() { }

    // Number chip first ("1"), routine name second ("Near post inswinger"). The gauge, chevron
    // and management dropdown inside the toggle carry no visible text, so the first two visible
    // texts are exactly those; anything past them is ignored rather than guessed at.
    public override string GetLabel()
    {
        var texts = new List<string>(2);
        CollectVisibleTexts(Element, texts);
        if (texts.Count == 0)
        {
            Speech.Degrade.Note("set-piece routine strip: active routine has no readable text");
            return Speech.Degrade.Value("routine name unreadable");
        }
        return texts.Count == 1 ? texts[0] : texts[0] + ", " + texts[1];
    }

    public override string GetAnnouncement() => GetLabel() + ", " + Role + ", " + GetState();

    private static void CollectVisibleTexts(VisualElement el, List<string> texts)
    {
        if (texts.Count >= 2 || !ElementBridge.IsVisible(el)) return;
        var textEl = el.TryCast<TextElement>();
        if (textEl != null)
        {
            var clean = Speech.TextFilters.CleanForDisplay(textEl.text);
            if (!string.IsNullOrEmpty(clean)) texts.Add(clean);
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectVisibleTexts(el.hierarchy[i], texts);
    }
}

/// <summary>An unselected routine's switch button, labelled from the strip's own label element
/// and the number chip beside the button. See <see cref="ActiveRoutineOptionAdapter"/>.</summary>
internal sealed class InactiveRoutineOptionAdapter : ButtonAdapter
{
    private readonly VisualElement _stripLabel;

    public InactiveRoutineOptionAdapter(VisualElement element, VisualElement stripLabel)
        : base(element)
    {
        _stripLabel = stripLabel;
    }

    public override string Role => "radio button";
    public override string GetState() => "";

    public override string GetLabel()
    {
        string number = FindNumberText();
        string strip = ReadText(_stripLabel);
        if (number == null)
        {
            Speech.Degrade.Note("set-piece routine strip: inactive routine number unreadable");
            number = Speech.Degrade.Value("routine number unreadable");
        }
        return string.IsNullOrEmpty(strip) ? number : strip + " " + number;
    }

    public override string GetAnnouncement() => GetLabel() + ", " + Role;

    // The number chip lives in the "-inactve-r" wrapper (the game template's own spelling)
    // that shares this button's parent.
    private string FindNumberText()
    {
        var parent = Element.hierarchy.parent;
        if (parent == null) return null;
        return FindFirstVisibleText(parent);
    }

    private static string FindFirstVisibleText(VisualElement el)
    {
        if (!ElementBridge.IsVisible(el)) return null;
        var textEl = el.TryCast<TextElement>();
        if (textEl != null)
        {
            var clean = Speech.TextFilters.CleanForDisplay(textEl.text);
            if (!string.IsNullOrEmpty(clean)) return clean;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindFirstVisibleText(el.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    private static string ReadText(VisualElement el)
    {
        var textEl = el?.TryCast<TextElement>();
        return textEl == null ? null : Speech.TextFilters.CleanForDisplay(textEl.text);
    }
}

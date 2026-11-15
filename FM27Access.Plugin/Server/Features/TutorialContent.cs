using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// FM27 starts a tutorial in TutorialDialog, then moves guided steps to the
// handler's floating block outside that Panel. Read the actually displayed
// block through its own controller; never show, reparent or dismiss it here.
internal static class TutorialContent
{
    internal static bool OwnsFloatingRoot(VisualElement element)
    {
        if (element.name != "TutorialFloatingBlock") return false;
        var handler = FM.UI.FMTutorialHandler.Instance;
        return handler?.m_tutorialFloatingBlock?.Pointer == element.Pointer
            && handler.m_tutorialRoot?.name == "TutorialDialog";
    }

    internal static VisualElement RootFor(object panel)
    {
        if (panel is not SI.Bindable.Panel p || p.PanelID.name != "TutorialDialog")
            return panel as VisualElement;
        var handler = FM.UI.FMTutorialHandler.Instance;
        if (handler?.m_tutorialRoot?.Pointer != p.Pointer
            || handler.m_floatingElement?.IsVisible != true) return p;
        var block = handler.m_tutorialFloatingBlock;
        return block?.panel != null && ElementBridge.IsVisible(block) ? block : p;
    }

    internal static string TitleFor(SI.Bindable.Panel panel)
    {
        var root = RootFor(panel);
        if (root == null || root.Pointer == panel.Pointer) return null;
        var title = Text(root, "Title");
        var steps = Text(root, "StepsText");
        return string.IsNullOrWhiteSpace(title) ? null
            : string.IsNullOrWhiteSpace(steps) ? title : title + " " + steps;
    }

    internal static string Signature(object panel)
    {
        if (panel is not SI.Bindable.Panel p || p.PanelID.name != "TutorialDialog") return null;
        var handler = FM.UI.FMTutorialHandler.Instance;
        var tutorial = handler?.m_tutorialRoot?.Pointer == p.Pointer
            ? SI.Bindable.TutorialManager.Instance?.m_currentTutorial.Value : null;
        // Several sections use the same title, and whole-screen steps may have
        // identical trees. Native step identity still changes in both cases.
        return (TitleFor(p) ?? "dialog") + ":" + tutorial?.TutorialID + ":" + tutorial?.CurrentStepNumber;
    }

    private static string Text(VisualElement root, string name)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.name == name) return ServerText.Clean(root.TryCast<TextElement>()?.text);
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var text = Text(root.hierarchy[i], name);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }
}

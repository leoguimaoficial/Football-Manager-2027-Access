using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The recommendation settings dialog draws its delivery choices as two textless radio columns.
/// Their names live once in the column header ("Show in Recruitment" / "Send to Inbox"), while
/// the row's subject is a separate sibling. Reconnect those visual relationships for accessibility.
/// All text is read from the live UI so it follows the active game language.
/// </summary>
internal sealed class RecommendationsDeliveryRadioButtonAdapter : RadioButtonAdapter
{
    private const string PanelName = "PlayerRecommendationsAdvancedSettingsDialog";
    private const string DeliveryListName = "DeliveryOptionsStreamedObjectList";
    private const string HeaderContainerName = "HeaderTextContainer";

    internal RecommendationsDeliveryRadioButtonAdapter(VisualElement element) : base(element) { }

    public override string GetLabel()
    {
        var group = FindAncestorOfType(Element, "SIRadioGroup");
        if (group != null)
        {
            int index = FindRadioIndex(group, Element);
            var headers = FindColumnLabels(group);
            if (index >= 0 && index < headers.Count)
                return headers[index];
        }

        return base.GetLabel();
    }

    internal static bool IsDeliveryOptionRadio(VisualElement element)
        => element.GetIl2CppType().Name == "SIRadioButton"
           && ElementBridge.IsInPanel(element, PanelName)
           && HasAncestorNamed(element, DeliveryListName);

    internal static string GetGroupLabel(VisualElement group)
    {
        if (group == null
            || group.GetIl2CppType().Name != "SIRadioGroup"
            || !ElementBridge.IsInPanel(group, PanelName)
            || !HasAncestorNamed(group, DeliveryListName))
            return null;

        // In every streamed row the group sits inside a VisualElement whose parent SIEnabled also
        // contains the row's visible SIText. Excluding the group prevents a future labelled radio
        // template from being mistaken for the row subject.
        var row = group.hierarchy.parent?.hierarchy.parent;
        return row == null ? null : FindFirstVisibleText(row, group);
    }

    private static List<string> FindColumnLabels(VisualElement group)
    {
        for (var current = group.hierarchy.parent; current != null; current = current.hierarchy.parent)
        {
            var header = FindDirectChildNamed(current, HeaderContainerName);
            if (header == null) continue;

            var labels = new List<string>();
            CollectVisibleTexts(header, labels);
            return labels;
        }

        return new List<string>();
    }

    private static int FindRadioIndex(VisualElement group, VisualElement target)
    {
        var radios = new List<VisualElement>();
        CollectRadios(group, radios);
        for (int i = 0; i < radios.Count; i++)
            if (radios[i].Pointer == target.Pointer) return i;
        return -1;
    }

    private static void CollectRadios(VisualElement root, List<VisualElement> radios)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "SIRadioButton") radios.Add(child);
            else CollectRadios(child, radios);
        }
    }

    private static VisualElement FindAncestorOfType(VisualElement element, string typeName)
    {
        for (var current = element.hierarchy.parent; current != null; current = current.hierarchy.parent)
            if (current.GetIl2CppType().Name == typeName) return current;
        return null;
    }

    private static bool HasAncestorNamed(VisualElement element, string name)
    {
        for (var current = element.hierarchy.parent; current != null; current = current.hierarchy.parent)
            if (string.Equals(current.name, name, StringComparison.Ordinal)) return true;
        return false;
    }

    private static VisualElement FindDirectChildNamed(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private static string FindFirstVisibleText(VisualElement root, VisualElement excluded)
    {
        if (root.Pointer == excluded.Pointer || !ElementBridge.IsVisible(root)) return null;

        try
        {
            var textElement = root.TryCast<TextElement>();
            if (textElement != null && !string.IsNullOrWhiteSpace(textElement.text))
                return Speech.TextFilters.CleanForDisplay(textElement.text);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[RecommendationsDeliveryRadio] Row label read failed: {e.GetType().Name}");
        }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindFirstVisibleText(root.hierarchy[i], excluded);
            if (!string.IsNullOrEmpty(found)) return found;
        }
        return null;
    }

    private static void CollectVisibleTexts(VisualElement root, List<string> labels)
    {
        if (!ElementBridge.IsVisible(root)) return;

        try
        {
            var textElement = root.TryCast<TextElement>();
            if (textElement != null && !string.IsNullOrWhiteSpace(textElement.text))
            {
                labels.Add(Speech.TextFilters.CleanForDisplay(textElement.text));
                return;
            }
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[RecommendationsDeliveryRadio] Column label read failed: {e.GetType().Name}");
        }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectVisibleTexts(root.hierarchy[i], labels);
    }
}

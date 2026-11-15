using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class AttributeRowAdapter : PluginAdapterBase
{
    // The game builds attribute rows from three templates: "clickable-attributes_row" (players at
    // the manager's own club — the row opens the attribute's progress-report card),
    // "non-clickable-attributes_row" (scouted / other clubs' players — same name/value cells,
    // no button, nothing to open), and the staff report's "container" (NonPlayerReport — an
    // "attribute-name" text and an "attribute-value" text in separate wrappers, never clickable).
    // Same read paths for all; only interactivity and the value cell's location differ.
    private const string ClickableRowName = "clickable-attributes_row";
    private const string NonClickableRowName = "non-clickable-attributes_row";
    private const string StaffRowName = "container";
    private const string ChangeIconName = "ui concepts-placeholder";

    public bool IsClickable { get; }
    private readonly bool _isStaffRow;

    public AttributeRowAdapter(VisualElement element) : base(element)
    {
        IsClickable = element.name == ClickableRowName;
        _isStaffRow = element.name == StaffRowName;
    }

    public override string Role => IsClickable ? "button" : "text";
    public override bool CanActivate => IsClickable;

    public override string GetLabel() => $"{GetAttributeName()} {GetAttributeValue()}";

    // A non-clickable row is static text; match TextAdapter's bare-label announcement instead
    // of the base "label, role" composition so the cursor never speaks a "text" suffix.
    public override string GetAnnouncement() => IsClickable ? base.GetAnnouncement() : GetLabel();

    // The two halves of the row, read live. The HTML surface renders them as separate table
    // cells (name button | value); the speech surface keeps the joined GetLabel.
    // Read failures throw rather than log here: every caller catches at its own boundary and
    // logs with context (table renderer emits a marker row, the summary skips, LiveValues drops
    // the tracker), so a throw-site error line would only double-log — loudest on the routine
    // case of a tracked cell read after its row was recycled by navigation.
    public string GetAttributeName()
    {
        var nameText = FindChildTextRecursive(Element.hierarchy[0]);
        if (nameText == null)
            throw new System.InvalidOperationException("Missing attribute name text");
        return nameText;
    }

    public string GetAttributeValue()
    {
        var valueRoot = ValueRoot();
        if (valueRoot == null)
            throw new System.InvalidOperationException("Missing overview frame");

        var valueText = FindChildTextRecursive(valueRoot);
        if (valueText == null)
            throw new System.InvalidOperationException("Missing attribute value text");
        return valueText;
    }

    // The row's recent-progression arrow. The game paints it inside the value side of the row and
    // collapses it (display: none) when the attribute did not move — which is every row of a
    // player we do not own, where the game shows no progression at all. Both row templates carry
    // the element, so the read is the same for each and the "nothing changed" and "not our player"
    // cases collapse into the same empty answer; the staff row has no value frame and returns
    // empty too.
    //
    // Empty means "the game is showing no arrow here". A sprite we cannot read or do not
    // recognise degrades to a marker instead, because a blank cell claims the attribute held
    // steady, and that is exactly the kind of confident falsehood the surface must never emit.
    public string GetChange()
    {
        var valueRoot = _isStaffRow ? null : ValueRoot();
        if (valueRoot == null)
            return "";

        var icon = FindVisibleDescendantNamed(valueRoot, ChangeIconName);
        if (icon == null)
            return "";

        var sprite = SpriteHelper.ReadSpriteName(icon);
        if (sprite == null)
            return Speech.Degrade.Read($"Attribute change arrow for '{Element.name}' has no readable sprite");

        return Navigation.AttributeChange.LabelFor(sprite)
               ?? Speech.Degrade.Read($"Unrecognised attribute change sprite '{sprite}'");
    }

    private static VisualElement FindVisibleDescendantNamed(VisualElement parent, string name)
    {
        if (parent.name == name)
            return parent.resolvedStyle.display == DisplayStyle.None ? null : parent;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindVisibleDescendantNamed(parent.hierarchy[i], name);
            if (found != null)
                return found;
        }
        return null;
    }

    public override string GetState() => Element.enabledInHierarchy ? "" : "disabled";

    public override void Activate()
    {
        var button = FindFirstSIButton(Element);
        if (button == null)
        {
            Speech.Log.Error("AttributeRowAdapter: no SIButton descendant found for activation");
            throw new System.InvalidOperationException("No SIButton to activate");
        }

        new ButtonAdapter(button).Activate();
    }

    private VisualElement ValueRoot() => _isStaffRow ? Element.hierarchy[1]
        : FindByName(Element, "overview frame") ?? Element.hierarchy[1];

    private static VisualElement FindFirstSIButton(VisualElement parent)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIButton")
                return child;
            var deeper = FindFirstSIButton(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private static string FindChildTextRecursive(VisualElement parent)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                    return textEl.text;
            }
            catch (System.Exception e)
            {
                Speech.Log.Warn($"TryCast<TextElement>() failed in AttributeRowAdapter: {e.GetType().Name}");
            }

            var deeper = FindChildTextRecursive(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    internal static bool IsAttributeRow(VisualElement element)
    {
        if (element.hierarchy.childCount != 2)
            return false;
        if (element.name == StaffRowName)
            return FindDescendantNamed(element.hierarchy[0], "attribute-name") != null
                && FindDescendantNamed(element.hierarchy[1], "attribute-value") != null;
        var second = element.hierarchy[1];
        // The compact in-match report uses the same two native name/value
        // columns without an activation button or the old template prefix.
        if (element.name == "attributes_row")
            return FindDescendantNamed(element.hierarchy[0], "text") != null
                && FindDescendantNamed(second, "badge-text") != null
                && FindDescendantByType(element, "SIButton") == null;
        // FM27 keeps the two-column row and its bound badge but removes the
        // value wrapper's name and its redundant SIButton. Reuse the original
        // row/table adapters for both native variants.
        if (element.name is ClickableRowName or NonClickableRowName
            && string.IsNullOrEmpty(second.name)
            && FindDescendantNamed(second, "badge-text") != null)
            return element.name == NonClickableRowName
                || element.hierarchy[0].GetIl2CppType().Name == "SIButton";
        if (second.name != "overview frame")
            return false;
        if (element.name == ClickableRowName)
        {
            if (element.hierarchy[0].GetIl2CppType().Name != "SIButton")
                return false;
            return FindDescendantByType(second, "SIButton") != null;
        }
        return element.name == NonClickableRowName;
    }

    private static VisualElement FindDescendantNamed(VisualElement parent, string name)
    {
        if (parent.name == name)
            return parent;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendantNamed(parent.hierarchy[i], name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static VisualElement FindDescendantByType(VisualElement parent, string typeName)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName)
                return child;
            var deeper = FindDescendantByType(child, typeName);
            if (deeper != null)
                return deeper;
        }
        return null;
    }
}

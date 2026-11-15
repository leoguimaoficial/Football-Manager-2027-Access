using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class StarRatingAbilityAdapter : ButtonAdapter
{
    private readonly VisualElement _starRatingElement;

    public StarRatingAbilityAdapter(VisualElement element) : base(element)
    {
        _starRatingElement = StarRatingHelper.FindDescendantByType(element, "FMStarRatingTemp");
        if (_starRatingElement == null)
        {
            Speech.Log.Error("StarRatingAbilityAdapter: no FMStarRatingTemp descendant found.");
            throw new System.InvalidOperationException("No FMStarRatingTemp descendant");
        }
    }

    // A single computed star label, not a primary/secondary split.
    protected override bool SplitsLabel => false;

    // The combined report opener names its own separate ratings. It is an
    // action containing labelled information, rather than one scalar field.
    internal bool HasCaptionedRatings
    {
        get
        {
            var stars = new System.Collections.Generic.List<VisualElement>();
            CollectVisibleStars(Element, stars);
            return stars.Count > 1 && stars.TrueForAll(star =>
                !string.IsNullOrWhiteSpace(CaptionForStar(star)));
        }
    }

    public override string GetLabel()
    {
        var stars = new System.Collections.Generic.List<VisualElement>();
        CollectVisibleStars(Element, stars);
        if (stars.Count <= 1) return StarRatingHelper.ReadStarLabel(_starRatingElement);

        // FM27 combines current and potential ability in one native button.
        // Preserve that action and read every rating with its own drawn caption,
        // rather than silently taking only the first FMStarRatingTemp.
        var parts = new System.Collections.Generic.List<string>(stars.Count);
        foreach (var star in stars)
        {
            string caption = CaptionForStar(star);
            var rating = StarRatingHelper.ReadStarLabel(star);
            parts.Add(string.IsNullOrWhiteSpace(caption) ? rating : caption + ": " + rating);
        }
        return string.Join(", ", parts);
    }

    private string CaptionForStar(VisualElement star)
    {
        for (var parent = star.parent; parent != null && parent.Pointer != Element.Pointer; parent = parent.parent)
        {
            var caption = FirstVisibleText(parent);
            if (!string.IsNullOrWhiteSpace(caption)) return caption;
        }
        return null;
    }

    private static void CollectVisibleStars(VisualElement element, System.Collections.Generic.List<VisualElement> stars)
    {
        if (!ElementBridge.IsVisible(element)) return;
        if (element.GetIl2CppType().Name == "FMStarRatingTemp") { stars.Add(element); return; }
        for (int i = 0; i < element.hierarchy.childCount; i++) CollectVisibleStars(element.hierarchy[i], stars);
    }

    private static string FirstVisibleText(VisualElement element)
    {
        if (!ElementBridge.IsVisible(element)) return null;
        var text = element.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text)) return text;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            text = FirstVisibleText(element.hierarchy[i]);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    public override string GetState() => "";

    public override string GetAnnouncement() => GetLabel() + ", " + Role;
}

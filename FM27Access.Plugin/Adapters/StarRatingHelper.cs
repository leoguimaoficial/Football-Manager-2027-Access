using System.Reflection;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal static class StarRatingHelper
{
    internal static readonly PropertyInfo MinValueProperty;
    internal static readonly PropertyInfo MaxValueProperty;

    static StarRatingHelper()
    {
        var starType = typeof(FM.UI.FMStarRatingTemp);
        MinValueProperty = starType.GetProperty("m_minValue",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        MaxValueProperty = starType.GetProperty("m_maxValue",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        if (MinValueProperty == null || MaxValueProperty == null)
        {
            Speech.Log.Error("FMStarRatingTemp.m_minValue or m_maxValue property not found.");
            throw new System.MissingMemberException("FMStarRatingTemp", "m_minValue/m_maxValue");
        }
    }

    internal static string ReadStarLabel(VisualElement starRatingElement)
    {
        var starRating = starRatingElement.TryCast<FM.UI.FMStarRatingTemp>();
        int minValue = (int)MinValueProperty.GetValue(starRating);
        int maxValue = (int)MaxValueProperty.GetValue(starRating);

        if (minValue == maxValue)
            return FormatStars(minValue);

        return $"{FormatStars(minValue)} to {FormatStars(maxValue)}";
    }

    /// <summary>
    /// Star label for a widget whose rating may not have bound yet. Returns false (with no label)
    /// when the widget is unbound — m_minValue/m_maxValue sit at -1 until their binding resolves —
    /// so a caller never emits a misleading "0 stars" for a rating that has simply not loaded.
    /// </summary>
    internal static bool TryReadStarLabel(VisualElement starRatingElement, out string label)
    {
        label = null;
        var starRating = starRatingElement.TryCast<FM.UI.FMStarRatingTemp>();
        if (starRating == null) return false;

        int minValue = (int)MinValueProperty.GetValue(starRating);
        int maxValue = (int)MaxValueProperty.GetValue(starRating);
        if (minValue < 0 || maxValue < 0) return false;

        label = minValue == maxValue
            ? FormatStars(minValue)
            : $"{FormatStars(minValue)} to {FormatStars(maxValue)}";
        return true;
    }

    internal static string FormatStars(int value)
    {
        int full = value / 2;
        int half = value % 2;

        if (full == 0 && half == 0)
            return "0 stars";
        if (full == 0 && half == 1)
            return "half a star";
        if (half == 1)
            return $"{full} and a half stars";
        if (full == 1)
            return "1 star";
        return $"{full} stars";
    }

    internal static VisualElement FindDescendantByType(VisualElement root, string typeName)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName)
                return child;
            var found = FindDescendantByType(child, typeName);
            if (found != null)
                return found;
        }
        return null;
    }
}

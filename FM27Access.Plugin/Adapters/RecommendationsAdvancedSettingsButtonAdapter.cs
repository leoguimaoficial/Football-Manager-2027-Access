using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The cog beside the recommended-player count opens the advanced settings and filters dialog,
/// but the button itself contains only an image and has no tooltip or hover binding. Resolve the
/// dialog title through the game's translation table so its accessible name follows the active
/// game language instead of baking an English profile rename into this screen.
/// </summary>
internal sealed class RecommendationsAdvancedSettingsButtonAdapter : ButtonAdapter
{
    // Runtime GameID carried by the dialog title's StaticText visual function. This differs from
    // the external KEY-683018 in the language template; TranslationID resolves runtime GameIDs.
    internal const int DialogTitleTranslationId = 18302;
    private const string FallbackLabel = "Advanced Settings & Filters";

    internal RecommendationsAdvancedSettingsButtonAdapter(VisualElement element) : base(element) { }

    protected override bool SplitsLabel => false;

    public override string GetLabel()
    {
        try
        {
            var translationId = new SI.Translation.TranslationID(DialogTitleTranslationId, string.Empty);
            string translated = translationId.TranslatedString;
            translated = Speech.TextFilters.CleanForDisplay(translated);
            if (!string.IsNullOrWhiteSpace(translated))
                return translated;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[RecommendationsAdvancedSettings] Translation lookup failed: {e.GetType().Name}: {e.Message}");
        }

        return FallbackLabel;
    }

    internal static bool IsAdvancedSettingsButton(VisualElement element)
    {
        if (element.name != "button-base-leading-icon"
            || !ElementBridge.IsInPanel(element, "RecruitmentPlayerRecommendationsCard")
            || element.hierarchy.childCount != 1)
            return false;

        return element.hierarchy[0].name == "leading-icon";
    }
}

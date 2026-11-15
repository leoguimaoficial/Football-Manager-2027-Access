using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class TacticsPlayerAdapter : PluginAdapterBase
{
    public TacticsPlayerAdapter(VisualElement element) : base(element) { }

    public override string Role => "button";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        var parts = new List<string>(4);

        var indicator = FindAncestorIndicator();

        var position = indicator != null && !string.IsNullOrEmpty(indicator.name)
            ? indicator.name : null;
        if (position != null)
            parts.Add(FM27Access.Navigation.PitchNavigation.SlotPhrase(position) ?? position);

        if (indicator != null)
        {
            var playerName = ReadPlayerName(indicator);
            if (playerName != null)
                parts.Add(playerName);

            var role = ReadRoleText(indicator);
            if (role != null)
                parts.Add(role);

            var starRating = ReadStarRating(indicator);
            if (starRating != null)
                parts.Add(starRating);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Player";
    }

    public override string GetState() => "";

    public override void Activate()
    {
        // No-op: TacticsPlayerHandler is pushed by DefaultScreenHandler.HandleEnter
    }

    internal VisualElement FindRoleButton()
    {
        var indicator = FindAncestorIndicator();
        if (indicator == null) return null;

        return FindByName(indicator, "RoleButton");
    }

    /// <summary>
    /// Clicks the game's RoleButton, opening the in-panel role options card.
    /// Throws if the button cannot be found (the pitch changed under the caller).
    /// </summary>
    internal void ActivateRoleButton()
    {
        var roleButton = FindRoleButton();
        if (roleButton == null)
            throw new System.InvalidOperationException("role button not found");
        roleButton.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
    }

    internal void ActivateShirtButton()
    {
        Element.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
    }

    internal VisualElement FindAncestorIndicator()
    {
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "FMPitchIndicator")
                return current;
            current = current.hierarchy.parent;
        }
        return null;
    }

    internal string ReadPlayerName(VisualElement indicator)
    {
        var nameText = FindByName(indicator, "PlayerNameText");
        if (nameText == null) return null;

        try
        {
            var textEl = nameText.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[TacticsPlayerAdapter] Failed to read player name: {e.GetType().Name}");
        }
        return null;
    }

    private string ReadRoleText(VisualElement indicator)
    {
        var roleButton = FindByName(indicator, "RoleButton");
        if (roleButton == null) return null;

        var roleText = FindByName(roleButton, "text");
        if (roleText == null) return null;

        try
        {
            var textEl = roleText.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[TacticsPlayerAdapter] Failed to read role text: {e.GetType().Name}");
        }
        return null;
    }

    private string ReadStarRating(VisualElement indicator)
    {
        var starElement = StarRatingHelper.FindDescendantByType(indicator, "FMStarRatingTemp");
        if (starElement == null) return null;

        try
        {
            return StarRatingHelper.ReadStarLabel(starElement);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[TacticsPlayerAdapter] Failed to read star rating: {e.GetType().Name}");
            return null;
        }
    }
}

using System.Collections.Generic;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The single, explicit place where custom surface features contribute their routes to the
/// server's route table. Wiring is deliberately greppable here rather than discovered by
/// reflection: a missing registration is a visible omission, not a silent one.
/// </summary>
internal static class FeatureRegistry
{
    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        PlayerCard.RegisterRoutes(routes);
        DateField.RegisterRoutes(routes);
        NavigatorSurface.RegisterRoutes(routes);
        TeamInstructions.RegisterRoutes(routes);
    }

    public static void RegisterRenderers(List<IElementRenderer> renderers)
    {
        renderers.Add(new FM27ShortcutCaptureRenderer());
        renderers.Add(new SetPieceTakersRenderer());
        renderers.Add(new SetPieceInstructionRenderer());
        renderers.Add(new PlayerCardRenderer());
        renderers.Add(new DateFieldRenderer());
        renderers.Add(new SpeakToRenderer());
        renderers.Add(new TabRowStatsRenderer());
        renderers.Add(new TacticsPhaseSelectRenderer());
        renderers.Add(new SoccerImportanceRenderer());
        renderers.Add(new TacticSlotChipRenderer());
        renderers.Add(new TacticSlotActiveRenderer());
        renderers.Add(new TacticSlotNumberRenderer());
        renderers.Add(new SoundCuePreviewRenderer());
        renderers.Add(new SoundSettingLevelRenderer());
        renderers.Add(new SoundSettingToggleRenderer());
        renderers.Add(new NavigatorBoxRenderer());
        renderers.Add(new MenuTreeRenderer());
        renderers.Add(new BookmarkGroupRenderer());
    }
}

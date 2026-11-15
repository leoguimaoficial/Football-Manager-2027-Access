using FM27Access.Navigation;

namespace FM27Access.Plugin;

// FM26 cursor history tracks navigable panels, not FM27 presentation documents.
// Preserve lower navigable panels while a loading screen covers them, and keep
// unknown native foregrounds eligible instead of assuming all new layers are art.
internal static class PanelObservationPolicy
{
    internal static readonly int[] NavigableLayers =
    {
        InteractivePanelSet.LayerMenu, InteractivePanelSet.LayerReport,
        InteractivePanelSet.LayerCard, InteractivePanelSet.LayerOverlay,
        6, // inherited Ribbon semantic role
        InteractivePanelSet.LayerModalDialog
    };

    internal static bool IsKnownCursorLayer(int role) => NavigableLayers.Contains(role)
        || role is InteractivePanelSet.LayerLoadingScreen or InteractivePanelSet.LayerProfanityLoadingScreen;

    internal static bool CanOwnCursor(int role, bool nativeInteractive) =>
        IsKnownCursorLayer(role) || nativeInteractive;
}

using FM27Access.Navigation;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin.Input.Handlers;

/// <summary>
/// Always-on handler that intercepts Enter on GridSelector team card buttons
/// (identified by having an "active-toggle" child). Selection itself runs through
/// <see cref="Adapters.GridSelectorSelection"/>, shared with the companion /activate path.
/// Returns false for all other elements and keys, falling through to DefaultScreenHandler.
/// </summary>
internal class TeamSelectionHandler : IInputHandler
{
    public string DisplayName => "TeamSelection";
    public bool CapturesAllInput => false;
    public IReadOnlyList<KeyboardKey> ClaimedKeys => Array.Empty<KeyboardKey>();

    public bool HandleKeyDown(KeyboardKey key, KeyModifiers modifiers)
    {
        if (key != KeyboardKey.Enter)
            return false;

        var current = NavigationManager.GetCurrentElement();
        // Only generic button cards select via the list model; specialised button subclasses
        // (presets, etc.) share the GridSelector shape but keep their own click activation, so
        // let them fall through to DefaultScreenHandler.
        if (current is not Adapters.PluginAdapterBase adapter || adapter.GetType() != typeof(Adapters.ButtonAdapter))
            return false;

        if (!Adapters.GridSelectorSelection.TrySelect(adapter.BackingElement, out bool wasSelected))
            return false;

        Speech.SpeechPipeline.SpeakInterrupt(wasSelected ? "deselected" : "selected");
        return true;
    }

    public bool Tick() => false;
    public void OnActivate() { }
    public void OnDeactivate() { }
}

namespace FM27Access.Plugin.Server;

internal enum PitchAction { Role, Instructions, Position }

// One action from a player on the editable tactics pitch. The pitch grid renderer
// (HtmlRenderer.RenderPitchGrid) registers a handle per cell and the generic /activate route runs
// it. The adapter's own Activate() is a deliberate no-op (the speech surface routes Enter through
// its handler stack), so the HTML surface clicks the underlying game buttons directly.
internal sealed class PitchActionHandle : ServerHandles.Handle
{
    public Adapters.TacticsPlayerAdapter Adapter;
    public PitchAction Action;
    internal PitchMoveSourceHandle Source;

    public override bool TryValidate(out string error)
    {
        error = null;
        var be = Adapter.BackingElement;
        if (Source != null && !Source.TryValidate(out error)) return false;
        if (be.panel == null || !ElementBridge.IsVisibleInHierarchy(be))
        {
            error = "That player is no longer on screen. Refresh.";
            return false;
        }
        return true;
    }

    public override string Activate()
    {
        switch (Action)
        {
            case PitchAction.Role:
                Adapter.ActivateRoleButton();
                return "Activated Change player role";
            case PitchAction.Instructions:
                Adapter.ActivateShirtButton();
                return "Activated Edit player instructions";
            default:
                return "Change pitch position is not built yet.";
        }
    }
}

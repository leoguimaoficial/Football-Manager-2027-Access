using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// One option of a segmented single-select control whose selected side the game marks with a
/// tick child (the set-piece screens' Scenario = Defending/Attacking and Delivery Type =
/// Inswinger/Outswinger rows, the instruction popup's category tabs, and the corner preview's
/// Left/Right Tab-Buttons pair). The template names on these buttons are static — the active/
/// inactive variants say where the row's default active side is, not the live state — so the
/// only honest tell is the tick: a "ui concepts-tick" / "Tick" descendant whose SIVisible
/// wrapper chain is actually visible. Read live every call, never cached.
/// </summary>
internal sealed class SegmentedTickOptionAdapter : ButtonAdapter
{
    public SegmentedTickOptionAdapter(VisualElement element) : base(element) { }

    public override string Role => "radio button";

    public override string GetState()
        => HasVisibleTick(Element) ? "selected" : "";

    // Visibility-pruned descent: an invisible wrapper (the SIVisible gating the tick) hides its
    // whole subtree, so a tick under one does not count. The unselected side either lacks the
    // tick element entirely or holds it under an invisible wrapper — both read unselected.
    private static bool HasVisibleTick(VisualElement el)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.name == "ui concepts-tick" || child.name == "Tick") return true;
            if (HasVisibleTick(child)) return true;
        }
        return false;
    }
}

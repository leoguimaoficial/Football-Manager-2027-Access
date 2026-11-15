using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// One option of a segmented single-select control (the Preferences "Mode" =
/// Fullscreen/Windowed and "Text Scaling" = Small/Normal/Large/Larger rows). The game builds
/// these from plain <c>SIButton</c>s in a <c>Mode-Buttons</c> container rather than a real radio
/// group, so the screen walker consolidates them into a <see cref="Server.ScreenWalker.RadioGroupItem"/>
/// and wraps each in this adapter to give it radio semantics on the HTML surface.
///
/// The selected segment is the one the game has filled with a solid background — the same
/// <c>resolvedStyle.backgroundColor.a</c> signal <see cref="ButtonAdapter.CountFilledSteps"/> already
/// trusts for stepper fill. Read live every call, never cached.
/// </summary>
internal sealed class SegmentedOptionAdapter : ButtonAdapter
{
    public SegmentedOptionAdapter(VisualElement element) : base(element) { }

    public override string Role => "radio button";

    public override string GetState()
        // FM27 can paint the active segment with its native style/gradient
        // while the button's own background remains transparent. Reuse the
        // ordinary button resolver, retaining FM26's solid-fill fallback.
        => IsSelectedBySegmentedStyle() || Element.resolvedStyle.backgroundColor.a > 0.5f ? "selected" : "";
}

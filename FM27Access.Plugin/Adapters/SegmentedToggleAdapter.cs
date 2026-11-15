using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The PositionInstructions / PersonalisedInstructions segmented button pair (the opposition
/// instructions grid's "Position/Player" column) as a single two-option control. The game draws it as
/// two icon-only buttons with the active one highlighted; the selected side is the one whose
/// background is opaque (backgroundColor.a > 0.5).
///
/// Both segments are icon-only — the game carries no text on them anywhere — so the option words are
/// mod-authored. They deliberately match the game's own column header, "Position/Player", which is the
/// only place the game names the pair, and they are listed in the same order as that header.
///
/// This never asserts a state it has not read. An earlier version spoke the label it expected the
/// toggle to land on the moment it clicked, which announced success whether or not the game agreed —
/// the player heard "Position" while the cell still showed "Player" and had no way to tell which was
/// true. Activation now drives the game and says nothing about the outcome; the surfaces re-read the
/// real selection and show that.
/// </summary>
internal class SegmentedToggleAdapter : PluginAdapterBase
{
    public SegmentedToggleAdapter(VisualElement frame) : base(frame) { }

    private const string PositionButtonName = "PositionInstructionsButton";
    private const string PositionLabel = "Position";
    private const string PlayerLabel = "Player";

    public override string Role => "toggle button";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        var (selected, _) = FindButtons();
        return LabelForButton(selected);
    }

    private static string LabelForButton(VisualElement button) =>
        button.name == PositionButtonName ? PositionLabel : PlayerLabel;

    public override string GetAnnouncement()
    {
        return $"{GetLabel()}, {Role}";
    }

    public override string GetState() => null;

    /// <summary>The two option labels in the order the game lays the segments out.</summary>
    internal string[] Options()
    {
        var (a, b) = SegmentButtons();
        return new[] { LabelForButton(a), LabelForButton(b) };
    }

    /// <summary>Index into <see cref="Options"/> of the segment the game currently has active.</summary>
    internal int SelectedIndex()
    {
        var (a, _) = SegmentButtons();
        return IsSelected(a) ? 0 : 1;
    }

    /// <summary>
    /// Drive the game to the given option. Idempotent: choosing the option already active does
    /// nothing, so re-selecting can never flip the toggle away from what the player asked for.
    /// Says nothing about the result — the caller re-reads the live selection.
    /// </summary>
    internal void SelectOption(int index)
    {
        var (a, b) = SegmentButtons();
        var target = index == 0 ? a : b;
        if (IsSelected(target)) return;
        Press(target);
    }

    public override void Activate()
    {
        var (_, unselected) = FindButtons();
        Press(unselected);
    }

    private static void Press(VisualElement button) => new ButtonAdapter(button).Activate();

    private (VisualElement a, VisualElement b) SegmentButtons() =>
        (ButtonIn(Element.hierarchy[0]), ButtonIn(Element.hierarchy[1]));

    // FM27 removes the row template name and inserts a tooltip before each
    // button. The pair's own native command names still identify the control;
    // do not take the first child and mistake its tooltip for a button.
    internal static bool IsSegmentedPair(VisualElement element)
    {
        if (element.GetIl2CppType().Name != "VisualElement" || element.hierarchy.childCount != 2) return false;
        var a = ButtonIn(element.hierarchy[0]);
        var b = ButtonIn(element.hierarchy[1]);
        return a != null && b != null && a.name != b.name;
    }

    private static VisualElement ButtonIn(VisualElement wrapper)
    {
        for (int i = 0; i < wrapper.hierarchy.childCount; i++)
        {
            var child = wrapper.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIButton"
                && child.name is "PositionInstructionsButton" or "PersonalisedInstructionsButton") return child;
        }
        return null;
    }

    private static bool IsSelected(VisualElement button) =>
        button.resolvedStyle.backgroundColor.a > 0.5f;

    private (VisualElement selected, VisualElement unselected) FindButtons()
    {
        var (a, b) = SegmentButtons();
        return IsSelected(a) ? (a, b) : (b, a);
    }
}

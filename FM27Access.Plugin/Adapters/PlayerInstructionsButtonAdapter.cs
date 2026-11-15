using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The "Player Instructions" button in a squad-table row (the Tactics and Team Selection screens).
/// It carries the same icon as the pitch's shirt-player-instruction-button, but its click is driven
/// entirely by the game's binding/input-action pipeline, not by any UI Toolkit event: SimulateButtonClick,
/// a pooled ClickEvent, NavigationSubmitEvent, and even a full synthetic pointer press-release are all
/// ignored (verified live), so the generic ButtonAdapter activation is a silent no-op. Its own Events
/// list holds only an FMX EventToPluginContext (EventFmxTogglePositionPersonalisedRole) which, bare-
/// dispatched, the game does not act on - the same dead end as the tile quick-change arrows.
///
/// Instead this reproduces what a real click leads to: it dispatches the game's
/// <see cref="FM.UI.TacticsPitchPositionEvent"/> asking for <c>OpenPlayerInstructions</c>, carrying
/// the row's team-selection index and in-possession flag. BOTH come from the row's own
/// DynamicReference (the "binding" child of its binding node): its <c>Index</c> property IS the
/// game's selection-model index for the row. It is NOT the row's display position - the Tactics
/// Planner's table is ordered per view while the selection model is not (D (R) sits at display row
/// 1 but selection index 8, probe-verified via /pibtnprobe), which is why the old display-index
/// read opened the wrong player's card there ("PI for the right defender opens Florent Sanchez")
/// while happening to work on rows where the two coincide. An unreadable reference refuses to open
/// rather than guessing: a card for the wrong player presented as the right one is exactly the lie
/// the mod exists to prevent. The game's TacticRoleHandlerModule opens the PlayerInstructionsCard.
/// Label and announcement are inherited from ButtonAdapter.
/// </summary>
internal sealed class PlayerInstructionsButtonAdapter : ButtonAdapter
{
    // The icon sprite that identifies this button (shared with the pitch shirt button, which is
    // routed to its own adapter by name before this one is ever considered).
    internal const string IconSprite = "player instructions_small_2x";

    private const string BindingChildName = "binding";
    private const string IndexProp = "Index";              // the selection-model index
    private const string InPossessionProp = "IsInPossession";

    public PlayerInstructionsButtonAdapter(VisualElement element) : base(element) { }

    public override void Activate()
    {
        if (!TryReadSelection(out int teamSelectionIndex, out bool inPossession))
        {
            throw new System.InvalidOperationException("The player instructions row is no longer available. Refresh.");
        }

        var events = SI.Core.GameSubsystems.Get<SI.Core.EventSubsystem>();
        if (events == null)
        {
            throw new System.InvalidOperationException("The native player instructions command is unavailable.");
        }

        var evt = FM.UI.TacticsPitchPositionEvent.GetPooled();
        evt.PanelToOpen = FM.UI.TacticsPitchPositionPanels.OpenPlayerInstructions;
        evt.TeamSelectionIndex = teamSelectionIndex;
        evt.InPossession = inPossession;
        events.SendEvent(evt);
    }

    /// <summary>
    /// The row's selection-model index (required) and in-possession flag (best-effort), both from
    /// the row's DynamicReference, read live at activation. False when the index can't be read.
    /// </summary>
    private bool TryReadSelection(out int teamSelectionIndex, out bool inPossession)
    {
        teamSelectionIndex = -1;
        inPossession = true;

        var root = FindBindingRoot(Element);
        if (root == null) return false;

        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return false;

        var rootKey = root.Key;
        if (!bindings.TryGetNode(ref rootKey, out var node)) return false;

        foreach (var child in node.Children)
        {
            if (child.Name != BindingChildName) continue;
            var childKey = child.Key;
            ReadReference(bindings.Get(ref childKey), ref teamSelectionIndex, ref inPossession);
        }
        return teamSelectionIndex >= 0;
    }

    private static SI.Bindable.BindingRoot FindBindingRoot(VisualElement element)
    {
        var current = element.hierarchy.parent;
        for (int depth = 0; current != null && depth < 25; depth++)
        {
            var root = current.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
            current = current.hierarchy.parent;
        }
        return null;
    }

    // The "binding" child is a ReferenceTypedValue wrapping the row's DynamicReference, whose
    // Index property is the selection-model index and whose IsInPossession mirrors the active
    // phase tab. A read failure leaves the index unset (the caller refuses to open) and the
    // possession default in place.
    private static void ReadReference(SI.Core.TypedValue typedValue, ref int index, ref bool inPossession)
    {
        try
        {
            if (typedValue == null) return;
            var reference = typedValue.Get();
            var dynRef = reference?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;

            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                string name = reg.GetInfo((uint)key).Name;
                if (name == IndexProp)
                {
                    if (TryParseTrailingInt(dynRef[key].ToString(), out int parsed))
                        index = parsed;
                }
                else if (name == InPossessionProp)
                {
                    inPossession = dynRef[key].ToString().Contains("True");
                }
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[PlayerInstructions] row reference read failed: {e.GetType().Name}");
        }
    }

    // A NumericTypedValue stringifies as "SI.Core.NumericTypedValue, 21"; take the trailing number.
    // Matches the established PresetButtonAdapter read.
    private static bool TryParseTrailingInt(string typedValueString, out int value)
    {
        value = -1;
        if (string.IsNullOrEmpty(typedValueString)) return false;
        int comma = typedValueString.LastIndexOf(',');
        var numeric = comma >= 0 ? typedValueString.Substring(comma + 1).Trim() : typedValueString.Trim();
        return int.TryParse(numeric, out value);
    }

    /// <summary>
    /// True for an SIButton whose icon is the player-instructions sprite. The sprite sits on a direct
    /// child (verified live), so this only reads the immediate children — cheap enough for the
    /// per-button adapter-selection path.
    /// </summary>
    internal static bool IsPlayerInstructionsButton(VisualElement element)
    {
        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = element.hierarchy[i];
            string sprite;
            try
            {
                var bg = child.resolvedStyle.backgroundImage;
                sprite = bg.sprite?.name ?? bg.vectorImage?.name;
            }
            catch
            {
                // resolvedStyle access can fail mid-layout in IL2CPP; treat as no match.
                continue;
            }
            // FM27 supplies the same native icon at additional raster densities.
            // Match the asset family, as ButtonAdapter does for its label, so a
            // 3x icon cannot fall through to the generic (ineffective) click.
            if (sprite != null && System.Text.RegularExpressions.Regex.Replace(sprite, @"_\d+x$", "_2x") == IconSprite)
                return true;
        }
        return false;
    }
}

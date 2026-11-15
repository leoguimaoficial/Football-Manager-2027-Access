using UnityEngine.UIElements;
namespace FM27Access.Plugin.Server;

// A move belongs to the rendered source player on one actual native phase
// pitch. Two simultaneous pitches can never be resolved by "first visible".
internal sealed class PitchMoveSourceHandle : ServerHandles.Handle
{
    internal VisualElement Pitch;
    internal Adapters.TacticsPlayerAdapter Adapter;
    internal string Slot;
    internal FM.UI.PersonReference Person;
    internal string PersonIdentity;
    internal string TacticContext;
    internal bool Possession;
    internal string Identity => PersonIdentity + "|tactic:" + TacticContext + "|phase:" + Possession;

    internal static PitchMoveSourceHandle Capture(VisualElement pitch, Adapters.TacticsPlayerAdapter adapter, string slot)
    {
        var person = Tactics.PitchNameLookup.PersonForShirt(adapter.FindAncestorIndicator());
        var phase = Tactics.PitchMoveEngine.ReadPossession(pitch);
        if (person == null || !phase.HasValue) return null;
        var identity = BindingRecords.ReferenceIdentity(person);
        if (identity == null) return null;
        return new() { Pitch = pitch, Adapter = adapter, Slot = slot, Person = person,
            PersonIdentity = identity,
            TacticContext = NativeTacticContext.Read(pitch), Possession = phase.Value };
    }

    internal bool OwnsPitch() => Pitch != null && Pitch.panel != null
        && ElementBridge.IsVisibleInHierarchy(Pitch)
        && Tactics.PitchMoveEngine.ReadPossession(Pitch) == Possession
        && (TacticContext == null || NativeTacticContext.Read(Pitch) == TacticContext);

    public override bool TryValidate(out string error)
    {
        error = "That pitch or player has changed. Refresh.";
        if (!OwnsPitch() || !ElementBridge.IsVisibleInHierarchy(Adapter.BackingElement)) return false;
        var indicator = Adapter.FindAncestorIndicator();
        if (indicator == null || indicator.name != Slot) return false;
        var current = Tactics.PitchNameLookup.PersonForShirt(indicator);
        if (current == null || BindingRecords.ReferenceIdentity(current) != PersonIdentity) return false;
        // Verify the source's actual ancestry as well as its slot and person.
        for (var parent = indicator.parent; parent != null; parent = parent.parent)
            if (parent.Pointer == Pitch.Pointer) { error = null; return true; }
        return false;
    }

    public override string Activate() => "Choose a destination on this player's pitch.";

}

namespace FM27Access.Plugin.Adapters;

// FM27 reopens saved management-style cards with item.selected=false even
// though the same event target still contains its selected-style bitmask.
// Resolve that target through the card's own typed event, never its caption,
// screen name or a parallel list of styles. Main thread only.
internal static class ManagerStyleSelection
{
    internal static bool TryRead(SI.Bindable.SICheckBox checkbox, out bool selected)
    {
        selected = false;
        var events = checkbox?.CheckedEvents;
        if (events == null) return false;
        for (int i = 0; i < events.Count; i++)
        {
            var coaching = events[i]?.TryCast<FM.UI.ToggleCoachingAttributesEventContext>();
            if (coaching != null)
            {
                selected = Read(checkbox, coaching.m_managerPersonaDataPath, coaching.m_indexSelectedDataPath, false);
                return true;
            }
            var mental = events[i]?.TryCast<FM.UI.ToggleMentalAttributesEventContext>();
            if (mental != null)
            {
                selected = Read(checkbox, mental.m_managerPersonaDataPath, mental.m_indexSelectedDataPath, true);
                return true;
            }
        }
        return false;
    }

    private static bool Read(SI.Bindable.SICheckBox checkbox, SI.Bindable.BindingPath managerPath,
        SI.Bindable.BindingPath indexPath, bool mental)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var scope = SI.Bindable.BindingUtils.GetViewKey(checkbox);
        var key = SI.Bindable.BindingUtils.FindOrBringBranchIntoScope(bindings, scope,
            Il2CppSystem.MemoryExtensions.AsSpan(managerPath.Path));
        var persona = FM.UI.ManagerPersonaModule.GetManagerPersona(bindings, key);
        if (persona == null)
            throw new System.InvalidOperationException("The management-style profile is no longer available. Refresh the screen.");
        // EventList stores prototypes: m_indexSelected is still zero until a
        // queued copy's binding callbacks run. Read its actual path in this
        // row's scope, using the same DynamicReference property reader as the
        // tactics adapters. Never prepare/send an event merely to read state.
        var indexKey = SI.Bindable.BindingUtils.FindOrBringBranchIntoScope(bindings, scope,
            Il2CppSystem.MemoryExtensions.AsSpan(indexPath.Path));
        var reference = bindings.Get(ref indexKey)?.Get()?.TryCast<SI.Bindable.DynamicReference>();
        var property = indexPath.Path.Substring(indexPath.Path.LastIndexOf('.') + 1);
        double? number = null;
        if (reference != null)
        {
            var registry = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = reference.Keys.GetEnumerator();
            while (keys.MoveNext())
                if (registry.GetInfo((uint)keys.Current).Name == property)
                {
                    number = Server.BindingRecords.NumberOf(reference[keys.Current]);
                    break;
                }
        }
        if (!number.HasValue || number.Value < 0 || number.Value >= 32 || number.Value != System.Math.Truncate(number.Value))
            throw new System.InvalidOperationException("The management-style choice is not bound. Refresh the screen.");
        int index = (int)number.Value;
        uint mask = unchecked((uint)(mental ? persona.MentalTraitsSelected : persona.CoachingStylesSelected));
        return (mask & (1u << index)) != 0;
    }
}

using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The position-picker pitch (Create a New Recruitment Focus step 1, the transfer-advert block) read
/// forward into the slot-keyed toggles the HTML grid renders.
///
/// The widget is the same FMTacticsPanel that draws a lineup, but every occupied slot here is a
/// filter toggle rather than a player. Walked flat it emits a run of bare checkboxes labelled with
/// the game's position text alone — a 3-4-2-1 gives three identical "D (C)" and two identical "DM"
/// with nothing to separate them, because the only thing that distinguishes them is where they sit
/// on the pitch. Sighted players read that off the pitch; consolidated into a grid, the row and
/// column headers carry it instead.
///
/// Extraction is live per render and holds no state beyond the adapters it hands back. A slot the
/// walk cannot read is dropped from the map and noted, never guessed at — the grid then renders that
/// cell empty, and <see cref="ScreenWalker"/> declines the whole consolidation if nothing was read,
/// so the raw checkboxes survive rather than the section vanishing.
/// </summary>
internal static class PositionPicker
{
    /// <summary>
    /// The pitch's occupied slots as slot code -> toggle adapter ("DCR" -> the "D (C)" checkbox).
    /// Empty when this pitch is not a picker or nothing readable was found.
    /// </summary>
    internal static Dictionary<string, IAccessibleElement> ReadToggles(VisualElement pitch)
    {
        var bySlot = new Dictionary<string, IAccessibleElement>();
        Collect(pitch, bySlot);
        return bySlot;
    }

    private static void Collect(VisualElement el, Dictionary<string, IAccessibleElement> bySlot)
    {
        if (el.GetIl2CppType().Name == "FMPitchIndicator")
        {
            AddSlot(el, bySlot);
            return;
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            Collect(el.hierarchy[i], bySlot);
    }

    // One slot. An unoccupied slot holds no button at all (the formation simply has nobody there),
    // which is an empty cell, not a failure — only a slot that HAS a button we cannot turn into a
    // toggle is worth a note.
    private static void AddSlot(VisualElement indicator, Dictionary<string, IAccessibleElement> bySlot)
    {
        string slot = indicator.name;
        if (string.IsNullOrEmpty(slot)) return;

        var button = FindPositionButton(indicator);
        if (button == null) return;

        var adapter = ElementBridge.CreateAdapter(button, ElementClassification.Button);
        if (adapter == null)
        {
            Degrade.Note($"position picker: slot '{slot}' has a button with no adapter");
            return;
        }
        bySlot[slot] = adapter;
    }

    private static VisualElement FindPositionButton(VisualElement el)
    {
        if (el.GetIl2CppType().Name == "SIButton"
            && Adapters.PitchPositionAdapter.IsPitchPositionButton(el))
            return el;
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var found = FindPositionButton(el.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Tactics;

/// <summary>
/// The Opposition Report's Squad Depth tab. It reuses the same <c>FMTacticsPanel</c> +
/// <c>FMPitchIndicator</c> widgets as a formation pitch, but each slot holds a DEPTH CHART, not an
/// occupant: a position header (position code, role code, and the number of players who can play
/// there), rank columns, and a streamed list of players in depth order.
///
/// <see cref="PitchGrid.ReadFormation"/> would read the first name in each slot's depth list and
/// present it as that slot's player — a starting eleven the game never claimed, with the same
/// player appearing at three different positions. This reader replaces that with what the screen
/// actually says: one row per position, the depth-ordered players in rank columns.
///
/// Detected by the depth header template, which a formation pitch never carries. Reads live on
/// every render; nothing is cached.
/// </summary>
internal static class SquadDepthPitch
{
    // The per-slot header of a depth chart: position code, role code and the squad count. Present
    // on every occupied depth slot and on no formation pitch, so it is the honest gate.
    private const string DepthHeader = "data_display-squad_planner-player_depth-header";

    /// <summary>One position's depth column.</summary>
    internal sealed class DepthSlot
    {
        internal string Slot;            // FMPitchIndicator name (GK, DCR, WBL, …) — pitch geometry
        internal string Position;        // the game's position code ("D (C)")
        internal string Role;            // the game's role code ("NCB"); null when the slot shows none
        internal string Count;           // players who can fill the position; null when unreadable
        internal List<string> Players = new();
    }

    /// <summary>
    /// True when <paramref name="pitch"/> is a depth chart rather than a formation. Bounded walk:
    /// the header sits a fixed handful of levels under the first occupied indicator.
    /// </summary>
    internal static bool IsDepthPitch(VisualElement pitch)
        => PitchGrid.FindByName(pitch, DepthHeader) != null;

    /// <summary>
    /// Every occupied depth slot under <paramref name="pitch"/>, in tree order. Empty slots (an
    /// indicator with no depth header) are absent. An empty result means the shape did not hold and
    /// the caller should leave the raw stops alone rather than render half a chart.
    /// </summary>
    internal static List<DepthSlot> Read(VisualElement pitch)
    {
        var slots = new List<DepthSlot>();
        if (pitch != null) Collect(pitch, slots);
        return slots;
    }

    private static void Collect(VisualElement el, List<DepthSlot> slots)
    {
        if (el.GetIl2CppType().Name == "FMPitchIndicator")
        {
            var header = PitchGrid.FindByName(el, DepthHeader);
            if (header != null)
            {
                var slot = ReadSlot(el, header);
                if (slot != null) slots.Add(slot);
            }
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++) Collect(el.hierarchy[i], slots);
    }

    // The count sits in its own standard cell inside the header; the position and role codes are the
    // two texts of the frame beside it. Read structurally rather than by text order, so a slot that
    // shows no role does not silently promote its count into the role column. A slot with no
    // readable position is dropped rather than rendered as an anonymous row.
    private const string CountCell = "data_display-cell-standard-small";

    private static DepthSlot ReadSlot(VisualElement indicator, VisualElement header)
    {
        var countEl = PitchGrid.FindByName(header, CountCell);
        var codes = new List<string>();
        CollectTexts(header, codes, 2, countEl != null ? countEl.Pointer : IntPtr.Zero);
        if (codes.Count == 0) return null;

        var count = new List<string>();
        if (countEl != null) CollectTexts(countEl, count, 1, IntPtr.Zero);

        var slot = new DepthSlot
        {
            Slot = indicator.name,
            Position = codes[0],
            Role = codes.Count > 1 ? codes[1] : null,
            Count = count.Count > 0 ? count[0] : null,
        };
        CollectPlayerNames(indicator, slot.Players);
        return slot;
    }

    // Texts under el, skipping the subtree rooted at skipPtr (the count cell, read separately).
    // Native-pointer identity, never wrapper reference: IL2CPP hands out a fresh wrapper per read.
    private static void CollectTexts(VisualElement el, List<string> texts, int max, IntPtr skipPtr)
    {
        if (texts.Count >= max) return;
        if (skipPtr != IntPtr.Zero && el.Pointer == skipPtr) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var t = el.TryCast<TextElement>()?.text;
            if (!string.IsNullOrEmpty(t)) texts.Add(Clean(t));
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectTexts(el.hierarchy[i], texts, max, skipPtr);
    }

    private static void CollectPlayerNames(VisualElement el, List<string> names)
    {
        if (el.name == "PlayerNameText")
        {
            var t = el.TryCast<TextElement>()?.text;
            if (!string.IsNullOrEmpty(t)) names.Add(Clean(t));
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectPlayerNames(el.hierarchy[i], names);
    }

    private static string Clean(string s) => FM27Access.Speech.TextFilters.CleanForDisplay(s);
}

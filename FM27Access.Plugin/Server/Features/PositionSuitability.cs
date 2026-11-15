using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads a player's positional familiarity off a suitability pitch — the Player Comparison card's
/// Overview tab draws one per player.
///
/// The pitch paints a dot per position and colours it by how well the player plays there (green
/// natural, amber can-fill-in, dark cannot). That colour IS the content of the widget — a sighted
/// player compares the two pitches at a glance to see who covers which positions — and it reaches
/// the HTML surface as nothing at all: the slots carry no text stops, so the walk emits an empty
/// space where the comparison should be.
///
/// The colour is painted from a style setter and cannot be read as text, but each slot's
/// <c>FMTacticsPanel.PositionInfo.Data</c> binding carries the value it is painted from:
/// <c>DisplayValue</c> (the position, "WB (L)") and <c>PropertyValue</c> (familiarity, 1-20).
/// Verified live 2026-08-07 against both the drawn pitch and the game's own words — a specialist
/// centre-back read 20 at D (C) and 1 everywhere else (one green dot), a versatile defender read
/// D (C) 20, WB (L) 20, D (L) 17, DM 12 (three green, one amber), and selecting each of those on
/// the Positions &amp; Roles tab gave Natural / Natural / Accomplished / Competent against
/// Ineffectual for 1.
///
/// The values are reported as the game's own 1-20 numbers, not as those words: only four points of
/// the scale are known, and inventing the band boundaries would eventually announce the wrong word.
/// The game's own word stays available per-position on the Positions &amp; Roles tab.
///
/// Main thread only. A failed read yields no slots, and the caller renders nothing rather than a
/// partial or guessed comparison.
/// </summary>
internal static class PositionSuitability
{
    private const System.Reflection.BindingFlags AllFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

    private static readonly System.Reflection.PropertyInfo PositionsProp =
        typeof(FM.UI.FMTacticsPanel).GetProperty("m_positions", AllFlags);

    /// <summary>The wrapper the game builds a per-player suitability pitch inside.</summary>
    internal const string SuitabilityContainer = "data_display-positions-suitability-large-default";

    private const string PositionProperty = "DisplayValue";
    private const string FamiliarityProperty = "PropertyValue";

    // The familiarity scale's bounds, and the floor that means "cannot play here". The SHARED
    // position pitch on the Positions & Roles tab puts a position BITMASK in the same property
    // (1, 4, 8, 16, 32, 64, 128, ...), so a value past the top of the scale is the tell that this
    // is not a suitability pitch at all and the whole read must be abandoned.
    private const int FamiliarityFloor = 1;
    private const int FamiliarityMax = 20;

    internal sealed class Slot
    {
        public VisualElement Indicator;
        public string SlotName;     // the pitch's own slot code ("WBL"), the key across two pitches
        public string Position;     // the game's drawn position text ("WB (L)")
        public int Familiarity;     // 1-20
    }

    /// <summary>True for a pitch built inside the per-player suitability widget.</summary>
    internal static bool IsSuitabilityPitch(VisualElement pitch)
        => string.Equals(pitch.hierarchy.parent?.name, SuitabilityContainer, StringComparison.Ordinal);

    /// <summary>
    /// The pitch's occupied slots with their familiarity, in the pitch's own order. Empty when this
    /// is not a suitability pitch, when the binding contract does not hold, or when any value falls
    /// outside the familiarity scale — all of which mean the numbers are not what this reader
    /// believes they are, so none of them are reported.
    /// </summary>
    internal static List<Slot> Read(VisualElement pitch)
    {
        var slots = new List<Slot>();
        if (!IsSuitabilityPitch(pitch)) return slots;
        if (PositionsProp == null)
        {
            Degrade.Note("position suitability: FMTacticsPanel.m_positions not found");
            return slots;
        }

        try
        {
            var typed = pitch.Cast<FM.UI.FMTacticsPanel>();
            if (PositionsProp.GetValue(typed) is not System.Collections.IEnumerable positions)
            {
                Degrade.Note("position suitability: m_positions not enumerable");
                return slots;
            }

            foreach (var entry in positions)
            {
                var slot = ReadSlot(entry);
                if (slot == null) continue;   // an unoccupied slot: the pitch simply draws nothing there
                if (slot.Familiarity < FamiliarityFloor || slot.Familiarity > FamiliarityMax)
                {
                    Degrade.Note($"position suitability: {slot.SlotName} read {slot.Familiarity}, outside the scale; no suitability reported");
                    return new List<Slot>();
                }
                slots.Add(slot);
            }
        }
        catch (Exception ex)
        {
            Degrade.Note($"position suitability: slot read failed: {ex.GetType().Name}");
            return new List<Slot>();
        }
        return slots;
    }

    // One slot, or null when it holds no bound data (an empty pitch spot) or the data does not
    // carry both properties this reader needs.
    private static Slot ReadSlot(object entry)
    {
        var indicator = entry?.GetType().GetProperty("PitchIndicatorSlot", AllFlags)?.GetValue(entry)
            as VisualElement;
        if (indicator == null) return null;

        if (entry.GetType().GetProperty("Data", AllFlags)?.GetValue(entry)
            is not SI.Bindable.DynamicReference data)
            return null;

        string position = null;
        string familiarity = null;
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var keys = data.Keys.GetEnumerator();
        while (keys.MoveNext())
        {
            var key = keys.Current;
            string name;
            try { name = reg.GetInfo((uint)key).Name; }
            catch { continue; }
            if (name == PositionProperty) position = data[key]?.AsString();
            else if (name == FamiliarityProperty) familiarity = data[key]?.AsString();
        }

        if (string.IsNullOrWhiteSpace(position) || !int.TryParse(familiarity, out int value)) return null;
        return new Slot
        {
            Indicator = indicator,
            SlotName = indicator.name ?? "",
            Position = position,
            Familiarity = value,
        };
    }

    /// <summary>Every visible suitability pitch under the panel, in document order.</summary>
    internal static List<VisualElement> FindPitches(VisualElement panel)
    {
        var found = new List<VisualElement>();
        Collect(panel, 0, found);
        return found;
    }

    private static void Collect(VisualElement el, int depth, List<VisualElement> found)
    {
        if (el == null || depth > 80) return;
        // Only the active tab's pitches: every tab's content stays in the tree, so an invisible
        // pitch here would put the Overview's table on every other tab too.
        if (!ElementBridge.IsVisible(el)) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "FMTacticsPanel")
        {
            if (IsSuitabilityPitch(el)) found.Add(el);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], depth + 1, found);
    }
}

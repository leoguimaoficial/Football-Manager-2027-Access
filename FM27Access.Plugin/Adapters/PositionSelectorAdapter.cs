using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A position toggle on a selector-mode pitch — the Player Search "Conditions" card's Position
/// block, where clicking pitch slots filters the search ("D (R)", "DM", ...). The game builds
/// these as FMPitchIndicator slots with a "positions-selector-root" binding root inside and no
/// SIButton (unlike the Recruitment Focus dialog's pitch, which PitchPositionAdapter covers via
/// its buttons). Selection lives on the owning FMTacticsPanel: m_positions maps slot index to
/// indicator, m_selection holds the selected indices, and HandleClick(index) is the game's own
/// toggle path (verified live: it commits to the search model and updates the "Position" display).
/// </summary>
internal class PositionSelectorAdapter : PluginAdapterBase
{
    private const System.Reflection.BindingFlags AllFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

    // The FMTacticsPanel reflection contract, resolved once. IL2CPP interop exposes the game's
    // private fields as properties and private methods as public, so these resolve on the proxy
    // type; a game update that renames them breaks the contract and every read degrades in place.
    private static readonly System.Reflection.PropertyInfo PositionsProp =
        typeof(FM.UI.FMTacticsPanel).GetProperty("m_positions", AllFlags);
    private static readonly System.Reflection.PropertyInfo SelectionProp =
        typeof(FM.UI.FMTacticsPanel).GetProperty("m_selection", AllFlags);
    private static readonly System.Reflection.MethodInfo HandleClickMethod =
        typeof(FM.UI.FMTacticsPanel).GetMethod("HandleClick", AllFlags);
    private static readonly System.Reflection.MethodInfo UpdateSelectionKeyMethod =
        typeof(FM.UI.FMTacticsPanel).GetMethod("UpdateSelectionKey", AllFlags);

    // The slot's own display text, when the caller already read it off the pitch. Some selector
    // templates (the Player Comparison card's) put the position text in a child this adapter's own
    // lookup does not know, leaving GetLabel on the slot CODE ("WBR") where the game draws
    // "WB (R)". Null keeps the lookup below.
    private readonly string _label;

    public PositionSelectorAdapter(VisualElement element) : this(element, null) { }

    public PositionSelectorAdapter(VisualElement element, string label) : base(element)
    {
        _label = string.IsNullOrEmpty(label) ? null : label;
    }

    public override string Role => "toggle";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        if (_label != null) return _label;

        // FM27's selector template exposes the localized caption as
        // PositionText. Keep the FM26 template as the structural fallback;
        // the internal slot code must not replace an available game label.
        foreach (var name in new[] { "PositionText", "position-text" })
        {
            var positionText = FindByName(Element, name);
            if (positionText == null || !ElementBridge.IsVisibleInHierarchy(positionText)) continue;
            var textEl = positionText.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }

        // The slot code ("DR", "DMC") is the game's own identifier — cryptic but honest.
        if (!string.IsNullOrEmpty(Element.name))
            return Element.name;

        return Speech.Degrade.Control("[PositionSelector] indicator has no position-text and no name");
    }

    public override string GetState()
    {
        if (!TryResolveSelection(out var selection, out int index, out string why))
            return Speech.Degrade.Value($"[PositionSelector] state read failed: {why}");
        return selection.Contains(index) ? "selected" : "not selected";
    }

    public override void Activate()
    {
        if (HandleClickMethod == null)
        {
            Speech.Degrade.Note("[PositionSelector] FMTacticsPanel.HandleClick not found; toggle inert");
            return;
        }
        if (!TryResolveIndex(out var pitch, out int index, out string why))
        {
            Speech.Degrade.Note($"[PositionSelector] activate failed: {why}");
            return;
        }
        HandleClickMethod.Invoke(pitch, new object[] { index });
        // HandleClick updates the pitch's own selection; the panels observing it (the Squad
        // Planner's per-position plan section) read it through the m_selectedPositionKey binding,
        // and UpdateSelectionKey is the publish step (verified live 2026-07-09: without it the
        // plan section never follows). Idempotent where HandleClick already published.
        if (UpdateSelectionKeyMethod != null)
            UpdateSelectionKeyMethod.Invoke(pitch, null);
        else
            Speech.Degrade.Note("[PositionSelector] FMTacticsPanel.UpdateSelectionKey not found; selection may not publish");
    }

    private bool TryResolveSelection(out SI.UI.Selection<int> selection, out int index, out string why)
    {
        selection = null;
        if (!TryResolveIndex(out var pitch, out index, out why))
            return false;
        if (SelectionProp == null) { why = "m_selection property not found"; return false; }
        selection = SelectionProp.GetValue(pitch) as SI.UI.Selection<int>;
        if (selection == null) { why = "m_selection null or uncastable"; return false; }
        return true;
    }

    private bool TryResolveIndex(out FM.UI.FMTacticsPanel pitch, out int index, out string why)
    {
        pitch = null;
        index = -1;
        why = null;

        var pitchEl = ElementBridge.FindAncestorByTypeName(Element, "FMTacticsPanel");
        if (pitchEl == null) { why = "no FMTacticsPanel ancestor"; return false; }
        pitch = pitchEl.Cast<FM.UI.FMTacticsPanel>();

        if (PositionsProp == null) { why = "m_positions property not found"; return false; }
        if (PositionsProp.GetValue(pitch) is not System.Collections.IEnumerable positions)
        {
            why = "m_positions not enumerable";
            return false;
        }

        var myPtr = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)Element).Pointer;
        int i = 0;
        foreach (var entry in positions)
        {
            var slotProp = entry?.GetType().GetProperty("PitchIndicatorSlot", AllFlags);
            if (slotProp?.GetValue(entry) is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase slot
                && slot.Pointer == myPtr)
            {
                index = i;
                return true;
            }
            i++;
        }
        why = "indicator not in m_positions";
        return false;
    }

    /// <summary>
    /// A pitch slot acting as a position-filter toggle: an FMPitchIndicator whose content is the
    /// position-selector widget and that carries no SIButton (a buttoned selector pitch is the
    /// Recruitment Focus dialog's, handled by PitchPositionAdapter). The empty placeholder slots
    /// of the same pitch (DCR, MCL, ...) have no selector root and stay layout wrappers.
    /// </summary>
    public static bool IsSelectorIndicator(VisualElement element)
    {
        if (FindByName(element, "positions-selector-root") == null)
            return false;
        return !HasDescendantOfType(element, "SIButton", 0);
    }

    private static bool HasDescendantOfType(VisualElement el, string typeName, int depth)
    {
        if (depth > 12) return false;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            try
            {
                if (child.GetIl2CppType().Name == typeName)
                    return true;
            }
            catch { return false; }
            if (HasDescendantOfType(child, typeName, depth + 1))
                return true;
        }
        return false;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Player Instructions support pieces. The PlayerInstructionsCard itself renders through the
/// generic walker, matching how the game presents it: a grid of tiles (instruction name, current
/// value, click opens the game's PlayerInstructionsEditCard) plus the whose-instructions side
/// panel. The editor likewise renders through the ordinary surface - a radio per option with its
/// description, refused options as real disabled radios, the committed value with staff feedback.
///
/// What lives here: the grid walk order and tile landmarks (WalkGrid), the static instruction
/// catalog (the PlayerInstructionEntries asset), and the one thing the editor genuinely lacks -
/// it never names the instruction being edited, so NameForOptions recovers that name live from
/// the on-screen options for the radio group's label and heading.
/// </summary>
internal static class PlayerInstructions
{
    private const int TileSearchDepth = 60;    // tiles sit ~30 levels below the card root
    private const string CardPanelName = "PlayerInstructionsCard";
    internal const string EditPanelName = "PlayerInstructionsEditCard";

    // Mod chrome: landmarks over the tile list. On the card the tiles are the instructions
    // themselves; in the editor the same grid is an in-place switcher over them.
    private const string CardTilesHeading = "Instructions";
    private const string EditorTilesHeading = "Switch Instruction";

    /// <summary>
    /// Called by ScreenWalker on a Player Instructions tile grid (the card's active tab, or the
    /// same grid embedded in the editor as its instruction switcher). Everything renders through
    /// the generic walk; this only fixes the reading order and drops a landmark. The grid holds
    /// more than the tiles - the whose-instructions panel (position picker, Apply to, the
    /// selected player and their role ability) is a grid cell too - so non-tile cells walk FIRST:
    /// the player should hear whose instructions these are before the instructions themselves
    /// (raw tree order splits the tiles around that panel). A heading then lands above the tiles
    /// so both surfaces can jump straight to them. False when the grid's content container is
    /// missing, so the caller falls back to the plain recursive walk.
    /// </summary>
    public static bool WalkGrid(VisualElement gridRoot, List<ScreenWalker.RenderItem> items,
        Action<VisualElement> walkOther)
    {
        var content = TeamInstructions.FindByName(gridRoot, "GridLayoutElementContent", 12);
        if (content == null) return false;

        bool editor = ElementBridge.IsInPanel(gridRoot, EditPanelName);
        if (editor) MarkEditorSeen(); else TrackCardTab(gridRoot);

        var tiles = new List<VisualElement>();
        int n = content.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var cell = content.hierarchy[i];
            if (!ElementBridge.IsVisible(cell)) continue;
            if (IsTileCell(cell)) tiles.Add(cell);
            else walkOther(cell);
        }
        if (tiles.Count > 0)
        {
            items.Add(new ScreenWalker.HeadingItem
            {
                Text = editor ? EditorTilesHeading : CardTilesHeading,
                Level = 3,
            });
            foreach (var tile in tiles) walkOther(tile);
        }
        return true;
    }

    // --- Tab restore ---
    //
    // Closing the editor rebuilds the card on its DEFAULT tab, so a player who entered from Out
    // of Possession would come back to In Possession. Walk-driven, so it needs no hook on the
    // tile click: the last card walk before an editor walk is by definition the tab the editor
    // was entered from, and the first card walk after one that shows a DIFFERENT tab can only be
    // the rebuilt card. A matching tab settles the state instead. Aged from the last walk of
    // either panel and attempt-capped, so a tab that will not switch cannot click forever.

    private static bool? _cardOop;      // tab of the last settled card walk
    private static bool _editorSeen;    // an editor walk happened since then
    private static long _lastWalkMs;
    private static int _restoreAttempts;
    // Covers the card's rebuild after Back (a few seconds) without lingering: the editor's Close
    // button tears the whole card down instead, and a request that outlived that would flip the
    // tab on the next, unrelated card open.
    private const int RestoreWindowMs = 10000;
    private const int RestoreMaxAttempts = 3;

    private const string OopGridName = "PlayerInstructionGroupsOutOfPossessionTileCompositionSet";
    private const string IpGridName = "PlayerInstructionGroupsInPossessionTileCompositionSet";

    private static void MarkEditorSeen()
    {
        if (_cardOop.HasValue) _editorSeen = true;
        _lastWalkMs = Environment.TickCount64;
    }

    private static void TrackCardTab(VisualElement gridRoot)
    {
        bool oop = gridRoot.name != null && gridRoot.name.Contains("OutOfPossession");
        long now = Environment.TickCount64;
        if (_editorSeen && _cardOop.HasValue && oop != _cardOop.Value
            && now - _lastWalkMs <= RestoreWindowMs && _restoreAttempts < RestoreMaxAttempts)
        {
            _restoreAttempts++;
            _lastWalkMs = now;
            RestoreTab(_cardOop.Value);
            return;   // keep the request until a walk lands on the right tab
        }
        _cardOop = oop;
        _lastWalkMs = now;
        _editorSeen = false;
        _restoreAttempts = 0;
    }

    private static void RestoreTab(bool wantOop)
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null || pv.Panel.name != CardPanelName) continue;
            // The first "TabButtons" group in tree order is the possession tab bar (the Apply to
            // bar shares the name but holds no grid-named wrapper, so the lookup below would miss
            // it anyway); each tab button sits in a template wrapper named after its tab's grid.
            var buttons = TeamInstructions.FindByName(pv.Panel, "TabButtons", TileSearchDepth);
            var wrapper = buttons == null ? null
                : TeamInstructions.FindByName(buttons, wantOop ? OopGridName : IpGridName, 4);
            var tab = wrapper == null ? null
                : TeamInstructions.FindByName(wrapper, "navigation-tab-default-default", 4);
            if (tab == null) break;
            tab.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
            return;
        }
        _editorSeen = false;   // give up rather than retry a click that is not landing
        Degrade.Note("player instructions: could not restore the card tab after the editor visit");
    }

    // An instruction tile is the cell built on the tile template ("TileBindingExpect", ~4 levels
    // below the cell). The cells' remapper names are NOT uniform - most are
    // "PlayerInstructionGroup<ID>" but Crossing Strategy's is "PlayerInstructionCrossingStrategy" -
    // so the template marker, not the name, is the tell.
    private static bool IsTileCell(VisualElement cell)
        // FM27 adds a BindingVariables/template pair: the same marker is six
        // levels below its cell. Preserve the original ordering and landmarks.
        => TeamInstructions.FindByName(cell, "TileBindingExpect", 8) != null;

    // --- Static catalog (PlayerInstructionEntries asset) ---

    internal sealed class Group
    {
        public string Label;                          // translated instruction name
        public List<TeamInstructions.Option> Options; // ordered, with descriptions and feedback
    }

    // GroupID string ("Tackling") -> group. Static game data, cached like the team catalog;
    // empty after a failed build so the next render retries.
    private static Dictionary<string, Group> _catalog;

    private static Dictionary<string, Group> Catalog()
    {
        if (_catalog is { Count: > 0 }) return _catalog;
        var map = new Dictionary<string, Group>(StringComparer.Ordinal);
        try
        {
            var assets = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerInstructionEntries>();
            if (assets == null || assets.Length == 0)
            {
                Degrade.Note("player instructions: PlayerInstructionEntries asset not found");
                return _catalog = map;
            }
            var groups = assets[0].m_playerInstructionGroups;
            if (groups == null)
            {
                Degrade.Note("player instructions: group list null");
                return _catalog = map;
            }
            foreach (var g in groups)
            {
                string key = g.GroupID.ToString();
                string label = ServerText.Clean(g.m_groupName.TranslatedString);
                var optionEntries = g.m_playerInstructionOptionEntries;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(label)) continue;
                if (optionEntries == null || optionEntries.Length < 2) continue;

                var options = new List<TeamInstructions.Option>(optionEntries.Length);
                foreach (var o in optionEntries)
                {
                    string name = ServerText.Clean(o.InstructionTranslation.TranslatedString);
                    if (string.IsNullOrWhiteSpace(name)) { options = null; break; }
                    string full = name;
                    var descIds = o.DescriptionTranslationIDList;
                    if (descIds != null)
                    {
                        foreach (var d in descIds)
                        {
                            string line = ServerText.Clean(d.TranslatedString);
                            if (!string.IsNullOrWhiteSpace(line)) full += ", " + line;
                        }
                    }
                    var detail = new StringBuilder();
                    AppendLine(detail, o.InstructionDescriptionTranslation.TranslatedString);
                    AppendLines(detail, o.InstructionProsTranslations);
                    AppendLines(detail, o.InstructionConsTranslations);
                    // The single-sentence main description stands in for a missing short list,
                    // like the paired team-instruction secondaries.
                    if (full == name && detail.Length > 0)
                    {
                        string main = ServerText.Clean(o.InstructionDescriptionTranslation.TranslatedString);
                        if (!string.IsNullOrWhiteSpace(main)) full += ", " + main;
                    }
                    options.Add(new TeamInstructions.Option
                    {
                        Name = name,
                        Full = full,
                        Detail = detail.ToString(),
                        Context = ServerText.Clean(o.FullContextName.TranslatedString),
                    });
                }
                if (options != null && !map.ContainsKey(key))
                    map[key] = new Group { Label = label, Options = options };
            }
        }
        catch (Exception ex)
        {
            Degrade.Note($"player instructions: catalog build failed ({ex.GetType().Name}: {ex.Message})");
        }
        return _catalog = map;
    }

    private static void AppendLine(StringBuilder sb, string raw)
    {
        string line = ServerText.Clean(raw);
        if (string.IsNullOrWhiteSpace(line)) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(line);
    }

    private static void AppendLines(StringBuilder sb, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<SI.Translation.TranslationID> ids)
    {
        if (ids == null) return;
        foreach (var id in ids) AppendLine(sb, id.TranslatedString);
    }

    /// <summary>
    /// The instruction whose options these radios are, or null when they are not a player
    /// instruction's option list. Matched against the static catalog by option name, so it is
    /// derived live from what is on screen rather than remembered from whatever was clicked - the
    /// editor can switch instruction in place, and a remembered name would then be a lie.
    /// Requires EVERY radio to match one group, so a partial or unrelated group names nothing.
    /// </summary>
    internal static string NameForOptions(List<IAccessibleElement> radios)
    {
        if (radios == null || radios.Count < 2) return null;
        var catalog = Catalog();
        if (catalog.Count == 0) return null;

        var names = new List<string>(radios.Count);
        foreach (var radio in radios)
        {
            var el = (radio as Adapters.PluginAdapterBase)?.BackingElement;
            string title = el == null ? null : TeamInstructions.FindText(el, "title", 10);
            if (string.IsNullOrWhiteSpace(title)) return null;
            names.Add(title);
        }

        foreach (var group in catalog.Values)
        {
            if (group.Options.Count != names.Count) continue;
            bool all = true;
            foreach (var n in names)
                if (group.Options.FindIndex(o => o.Name == n || o.Context == n) < 0) { all = false; break; }
            if (all) return group.Label;
        }
        return null;
    }

    /// <summary>
    /// Whether the open editor's whole-instruction lock line ("This Instruction is locked due to
    /// this player's role or position") is showing. The "locked-info-visible" wrapper itself is
    /// ALWAYS visible; the game populates its content (the lock icon and sentence) only for a
    /// locked instruction, so a visible child is the live signal - probe-verified 2026-08-26,
    /// and it tracks the editor's in-place instruction switches. When the line shows, the game
    /// refuses every option change but does NOT disable the radios: they look available and
    /// silently swallow the click, which on the HTML surface would leave a checked radio the
    /// game rejected. The renderer therefore presents the radios as disabled. An unreadable
    /// panel set reads as unlocked - the render that produced this call is already degrading in
    /// that case - and is noted.
    /// </summary>
    internal static bool EditorInstructionLocked()
    {
        var panels = InteractivePanels.CurrentOrNull();
        if (panels == null)
        {
            Degrade.Note("player instructions: lock-line read failed; radios rendered as-is");
            return false;
        }
        foreach (var pv in panels)
        {
            if (pv.Panel == null || pv.Panel.name != EditPanelName) continue;
            var lockInfo = TeamInstructions.FindByName(pv.Panel, "locked-info-visible", TileSearchDepth);
            if (lockInfo == null || !ElementBridge.IsVisible(lockInfo)) return false;
            int n = lockInfo.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                if (ElementBridge.IsVisible(lockInfo.hierarchy[i])) return true;
            return false;
        }
        return false;
    }

    // Resolved fresh from the open card on every use: the panel can rebuild its grid at will, so
    // no element reference is held across frames. Used by the PiLockProbe dev endpoint.
    internal static VisualElement FindTile(string key)
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null || pv.Panel.name != CardPanelName) continue;
            var tile = TeamInstructions.FindByName(pv.Panel, key, TileSearchDepth);
            if (tile != null && ElementBridge.IsVisible(tile)) return tile;
        }
        return null;
    }
}

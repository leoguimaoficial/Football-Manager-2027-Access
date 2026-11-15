using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>Where a menu holder sits relative to the element whose menu was resolved.</summary>
internal enum MenuHolderKind
{
    None,
    Self,       // the element itself carries the manipulator
    Descendant, // a holder inside its subtree (a wrapper button over its own name link)
    Ancestor,   // a holder above it (the element sits inside a menu-bearing container)
}

/// <summary>
/// The full outcome of <see cref="ContextMenuOpener.Resolve"/> — the chosen target plus the shape
/// evidence the choice rests on. <see cref="ContextMenuOpener.MenuTarget"/> only needs
/// <see cref="Target"/>; the extra fields exist because the descendant/ancestor rules are the ones
/// that misfire (a tile inheriting a buried entity's menu), and the census cannot judge a
/// mark without seeing WHICH holder answered and how far away it sat.
/// </summary>
internal struct MenuResolution
{
    public VisualElement Target;
    public MenuHolderKind Kind;
    public string HolderType;   // the holder's IL2CPP type name ("SIText", "PluginContextMenuBindings", ...)
    public string HolderText;   // raw text of an SIText holder (callers clean), else null
    public int Depth;           // levels between the queried element and the holder (0 = self)
    public int HolderCount;     // menu-bearing holders found in the subtree (0 when none was searched)
}

/// <summary>
/// Opens the game's per-element right-click context menus from the accessibility surface (the universal
/// recipe in docs/reference/context-menus.md). The universal trigger is a <c>ContextMenuDescriptionManipulator</c>:
/// a menu-bearing sub-element is an <c>SIText</c> leaf (a <c>&lt;link&gt;</c> hyperlink) whose
/// <c>m_contextMenuDescriptionManipulator</c> field is non-null — verified live on the squad name leaf,
/// the calendar opponent, and the calendar competition, and null on every non-menu text. (The squad also
/// has the manipulator on its <c>PluginContextMenuBindings</c> wrapper, but the leaf carries it too, so the
/// leaf is the single universal marker.) The menu opens by driving that manipulator's own keyboard entry,
/// <c>OpenOnShortcutEvent</c> (see <see cref="Send"/>) — position-free, so it works on scrolled and
/// ribbon-occluded rows where a synthetic pointer's positional pick would miss. The game's own manipulator
/// populate, contributor chain fills it and <see cref="ContextMenuSurface"/> reads the result.
///
/// A composite tile (a calendar event) has SEVERAL menu leaves (opponent → team menu; competition → its
/// own), so <see cref="MenuLeaves"/> enumerates them all for per-sub-element targeting. Sibling primitive
/// to <see cref="SITextLink"/>. Main thread only (touches live VisualElements and IL2CPP reflection).
/// </summary>
internal static class ContextMenuOpener
{
    // How far up the ancestry the menu trigger can sit above the element a player targets.
    private const int AncestryDepth = 16;

    private static readonly Il2CppSystem.Reflection.BindingFlags Flags =
        Il2CppSystem.Reflection.BindingFlags.Instance
        | Il2CppSystem.Reflection.BindingFlags.Public
        | Il2CppSystem.Reflection.BindingFlags.NonPublic
        | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly;

    // Per-type cache of the ContextMenuDescriptionManipulator field (null entry = the type has none),
    // keyed by IL2CPP type full name. Built once per type; main-thread only like the rest of this class.
    private static readonly Dictionary<string, Il2CppSystem.Reflection.FieldInfo> _manipField = new();

    /// <summary>
    /// True when <paramref name="element"/> has a right-click menu reachable from it — the element itself
    /// is a menu leaf, a menu leaf sits in its subtree (a squad NameButton wrapping its NameText), or a
    /// manipulator-bearing ancestor covers it. The standalone "Actions ▾" button is excluded (it opens on
    /// LEFT click and is already an activatable Button via <see cref="ContextMenuButtonAdapter"/>).
    /// </summary>
    public static bool HasContextMenu(VisualElement element) => MenuTarget(element) != null;

    /// <summary>
    /// The manipulator-bearing element whose menu covers <paramref name="element"/>, or null when it has
    /// none: the element itself if it is a menu leaf, else the first menu leaf in its subtree, else the
    /// nearest manipulator-bearing ancestor. The result always carries a <c>ContextMenuDescriptionManipulator</c>,
    /// so <see cref="Open"/> can drive it directly — no screen position needed.
    /// </summary>
    public static VisualElement MenuTarget(VisualElement element) => Resolve(element).Target;

    /// <summary>
    /// The scoped verdict for a resolution judged against the control's own rendered name — the single
    /// place the mark decision and the census agree on. A resolved holder is only a real menu when
    /// <see cref="Navigation.ContextMenuScope.Accepts"/> the verdict.
    /// </summary>
    public static Navigation.MenuScopeVerdict Verdict(in MenuResolution r, string ownerLabel)
    {
        if (r.Target == null) return Navigation.MenuScopeVerdict.NoHolder;
        return Navigation.ContextMenuScope.Judge(
            ownerLabel, r.HolderText, r.HolderType == "SIText", r.Depth, r.HolderCount,
            r.Kind == MenuHolderKind.Self, IsSelfContainedEntityHolder(r.HolderType));
    }

    /// <summary>
    /// A holder type that bakes its own menu and represents a SINGLE entity — a calendar event
    /// (training session, fixture tile) or a pitch indicator. The whole subtree under it is that one
    /// entity (docs/reference/context-menus.md: "a component that bakes the manipulator + contributor
    /// into itself"), so a control inside it opens that entity's menu at any depth, and the wrapper
    /// depth cap must not apply. This is NOT true of a <c>PluginContextMenuBindings</c> node, which can
    /// sit inside a multi-entity summary tile and bind an entity the tile is not named after.
    /// FMPitchIndicator is deliberately excluded for now (its menus are unverified); add it here if a
    /// pitch control's menu is confirmed wanted.
    /// </summary>
    public static bool IsSelfContainedEntityHolder(string holderType) => holderType == "FMCalendarEvent";

    /// <summary>
    /// The control's rendered accessible name, by the precedence the surfaces use (a profile rename
    /// beats the live label) — what the player HEARS the control called, which is what a menu mark
    /// must agree with. Degrades an unreadable label to the honest marker rather than an empty string
    /// (an empty owner label can never match a named holder, so a gap stays a gap).
    /// </summary>
    public static string OwnerLabel(FM27Access.Navigation.IAccessibleElement owner)
    {
        try
        {
            var rename = Server.ServerText.RenameOr(owner, null);
            if (!string.IsNullOrEmpty(rename)) return rename;
            return owner.GetPrimaryLabel() ?? owner.GetLabel() ?? "";
        }
        catch (System.Exception ex)
        {
            Speech.Log.Warn($"[context menu] owner label read failed: {ex.GetType().Name}");
            return Speech.Degrade.Unreadable;
        }
    }

    /// <summary>
    /// The menu resolution for <paramref name="element"/>: which holder answers for it, of what kind,
    /// how far away, and how many rival holders its subtree carries. <see cref="MenuTarget"/> is this
    /// minus the evidence. Never throws; a menuless element resolves to <see cref="MenuHolderKind.None"/>.
    /// </summary>
    public static MenuResolution Resolve(VisualElement element)
    {
        var r = new MenuResolution();
        if (ContextMenuButtonAdapter.IsStandaloneMenuButton(element)) return r;

        if (IsMenuBearing(element)) return Fill(element, MenuHolderKind.Self, 0, 1);

        var holders = DescendantHolders(element);
        r.HolderCount = holders.Count;
        if (holders.Count > 0)
        {
            // Prefer an actions-bearing link leaf (DescendantHolders already filters SIText to those):
            // its menu is the linked entity's own, and Send's hover-link set makes the leaf path
            // reliable. A PluginContextMenuBindings node earlier in document order can shadow the leaf
            // yet resolve no entity of its own — the fixture opposition cell, verified live: the
            // bindings-target open never surfaced, the team-leaf open works.
            int pick = 0;
            for (int i = 0; i < holders.Count; i++)
                if (holders[i].Element.GetIl2CppType().Name == "SIText") { pick = i; break; }
            return Fill(holders[pick].Element, MenuHolderKind.Descendant, holders[pick].Depth, holders.Count);
        }

        var cur = element.hierarchy.parent;
        for (int d = 1; cur != null && d <= AncestryDepth; d++, cur = cur.hierarchy.parent)
            if (IsMenuBearing(cur)) return Fill(cur, MenuHolderKind.Ancestor, d, 1);
        return r;
    }

    // Snapshot a chosen holder into a resolution. The SIText text is taken raw — every caller has its
    // own cleaning boundary, and the census needs the same raw string the label path sees.
    private static MenuResolution Fill(VisualElement holder, MenuHolderKind kind, int depth, int holderCount)
    {
        string type;
        try { type = holder.GetIl2CppType().Name; }
        catch { type = holder.name; }

        string text = null;
        if (type == "SIText")
        {
            try { text = holder.Cast<TextElement>().text; }
            catch (System.Exception ex) { Speech.Degrade.Note($"context menu: holder text read failed: {ex.GetType().Name}"); }
        }

        return new MenuResolution
        {
            Target = holder,
            Kind = kind,
            HolderType = type,
            HolderText = text,
            Depth = depth,
            HolderCount = holderCount,
        };
    }

    /// <summary>
    /// Every visible menu-bearing leaf strictly BELOW <paramref name="root"/>, in document order — the
    /// per-sub-element targets of a composite tile (a calendar event yields its competition and opponent).
    /// The root itself is never a leaf here; <see cref="Resolve"/> answers for it first.
    /// </summary>
    public static List<VisualElement> MenuLeaves(VisualElement root)
    {
        var holders = DescendantHolders(root);
        var result = new List<VisualElement>(holders.Count);
        foreach (var h in holders) result.Add(h.Element);
        return result;
    }

    /// <summary>A menu-bearing element found under a subtree, with its depth below the scan root.</summary>
    internal readonly struct Holder
    {
        public readonly VisualElement Element;
        public readonly int Depth;
        public Holder(VisualElement element, int depth) { Element = element; Depth = depth; }
    }

    // Every visible menu-bearing element under root, in document order, each with its depth below
    // root. Depth is what separates a wrapper from a composite: a name button's own binding sits a
    // level or two down, while a tile's buried sub-block binding sits many levels down and belongs to
    // an entity the tile is not named after.
    internal static List<Holder> DescendantHolders(VisualElement root)
    {
        var result = new List<Holder>();
        var stack = new Stack<Holder>();
        stack.Push(new Holder(root, 0));
        int seen = 0;
        while (stack.Count > 0 && seen < 600)
        {
            var holder = stack.Pop();
            var e = holder.Element;
            seen++;
            // The manipulator is the universal menu signal; visibility distinguishes a real leaf from a
            // hidden switch branch. Do NOT also require a laid-out worldBound — that was for the old
            // synthetic-pointer opener (don't positionally click an off-screen leaf), but the opener is now
            // position-free (OpenOnShortcutEvent + hover-link). The wb gate false-negated a leaf captured
            // before layout resolved (worldBound 0x0) — e.g. a streamed table's first row, read at the
            // earliest instant — so it rendered with no aria-haspopup despite having a menu.
            if (holder.Depth > 0 && e.visible && IsMenuBearing(e))
                result.Add(holder);
            for (int i = e.hierarchy.childCount - 1; i >= 0; i--)
                stack.Push(new Holder(e.hierarchy[i], holder.Depth + 1));
        }
        return result;
    }

    /// <summary>
    /// True when this element carries a <c>ContextMenuDescriptionManipulator</c> — either as a non-null
    /// field of its own (<c>SIText</c>, <c>PluginContextMenuBindings</c>, <c>FMCalendarEvent</c>,
    /// <c>FMPitchIndicator</c>) or via <see cref="ManipulatorRegistry"/>, which covers the shape where
    /// the game attaches a manipulator the element never stores (a <c>StreamedTable</c> column header).
    /// </summary>
    public static bool HasMenuManipulator(VisualElement e)
        => HasMenuManipulatorField(e) || ManipulatorRegistry.Get(e) != null;

    /// <summary>
    /// The field half of <see cref="HasMenuManipulator"/>, without the registry. The registry uses it
    /// to decide what NOT to record: a field-backed manipulator is already reachable.
    /// </summary>
    public static bool HasMenuManipulatorField(VisualElement e)
    {
        var field = ManipulatorField(e.GetIl2CppType());
        if (field == null) return false;
        try { return field.GetValue(e) != null; }
        catch { return false; }
    }

    /// <summary>
    /// True when this element's right-click menu would actually show something. An armed manipulator
    /// alone is not enough on an <c>SIText</c>: the game arms it on EVERY hyperlink-styled text, but at
    /// populate time the menu's content is the linked entity's actions, and links to entities with no
    /// actions (a fixture score's match link) yield zero contributors, so the menu never surfaces
    /// (verified live). Gate SIText leaves with the game's own per-link test, <c>LinkHasActions</c>.
    /// Non-SIText holders (PluginContextMenuBindings, FMCalendarEvent, FMPitchIndicator) carry their own
    /// content path and stay ungated (verified live: a friendly's competition cell has an actions-free
    /// leaf but a bindings node whose menu opens).
    /// </summary>
    public static bool IsMenuBearing(VisualElement e)
        => HasMenuManipulator(e) && (e.GetIl2CppType().Name != "SIText" || AnyLinkHasActions(e));

    // The game's own "this link's menu has content" test, per embedded-data record. Link ids are the
    // record indices (<link="0"> is record 0). No embedded records means no entity to build a menu
    // from — the friendly-competition case — so false. A failed probe degrades OPEN (marker kept):
    // the player then gets the honest "did not open" on Shift+F10, instead of a real menu silently
    // losing its mark.
    private static bool AnyLinkHasActions(VisualElement e)
    {
        try
        {
            // FM27 removes the global entity-menu providers during a match.
            // Styled SIText links can still open an in-match profile and report
            // LinkHasActions=true, but both native shortcut/right-click paths
            // leave the context unbound without any provider. Component-owned
            // menus keep their independent manipulator path above.
            var subsystem = SI.Core.GameSubsystems.Get<SI.UI.ContextMenuSubsystem>();
            if (subsystem?.m_globalContributors == null || subsystem.m_globalContributors.Count == 0)
                return false;
            var st = e.Cast<SI.Bindable.SIText>();
            if (st.DisableContextMenu) return false;
            var data = st.EmbeddedData;
            int n = data == null ? 0 : data.Count;
            for (int i = 0; i < n; i++)
                if (st.LinkHasActions(i)) return true;
            return false;
        }
        catch (System.Exception ex)
        {
            FM27Access.Speech.Degrade.Note($"context menu: LinkHasActions probe failed: {ex.GetType().Name}");
            return true;
        }
    }

    /// <summary>
    /// Open the menu for <paramref name="element"/> through the game's own keyboard/shortcut entry,
    /// <c>ContextMenuDescriptionManipulator.OpenOnShortcutEvent</c> — NOT a synthetic pointer. This is
    /// position-free: it drives the manipulator directly, so it opens menus on scrolled or ribbon-occluded
    /// rows where a synthetic right-click's positional pick lands on the wrong element (verified live: the
    /// real click path runs OnPointerUp → OpenContextMenu; OpenOnShortcutEvent reaches the same
    /// OpenContextMenu without any pointer). Returns true once the open is dispatched to a confirmed menu
    /// owner; false with <paramref name="error"/> set when there is none. Opening and CONFIRMING are
    /// separate: the game surfaces the menu a frame or two later, so the caller polls
    /// <see cref="ContextMenuSurface.TryGetMenu"/> across frames. Main thread only; never throws on the
    /// no-menu case.
    /// </summary>
    public static bool Send(VisualElement element, out string error, int? embeddedLinkIndex = null)
    {
        var target = MenuTarget(element);
        var manip = target == null ? null : Manipulator(target);
        if (manip == null)
        {
            error = "No context menu here.";
            return false;
        }

        // The menu's content is the TARGET ENTITY's actions, resolved from the leaf's hover-link-id (SIText
        // field 0x608) by the subsystem's global contributor (GetContextMenuObjects -> GetEmbeddedDataContextValues).
        // The game sets that field on FOCUS via SetHoverLinkOnTableRowFocusedStateChanged: automatic for
        // focusable table rows (squad), but NOT for non-row link leaves like calendar event tiles — whose menu
        // then resolves no entity and opens empty. So set it the game's own way before opening (verified live
        // on calendar opponent + competition leaves). No-op-safe: the method sets the hover-link to the first
        // link when the leaf has links, else clears it, so re-calling on an already-focused squad row is benign.
        // See docs/reference/context-menus.md.
        if (target.GetIl2CppType().Name == "SIText")
        {
            var text = target.Cast<SI.Bindable.SIText>();
            text.SetHoverLinkOnTableCellFocusedStateChanged(true);
            if (embeddedLinkIndex is int index)
            {
                if (!text.IsValidLink(index) || !text.IsLinkEnabled(index) || !text.LinkHasActions(index))
                { error = "That text link changed. Refresh the screen."; return false; }
                // Focus primes link zero; a separately exposed embedded link
                // must prime its own native record before the same menu opens.
                text.m_hoveredLinkID = index;
            }
        }

        // Drive the manipulator's pointer-free keyboard entry. This produces a menu that PERSISTS (no
        // pointer-capture state to be dismissed next frame, unlike injecting OnPointerDown/OnPointerUp
        // directly, which opens a menu the input system immediately closes). Verified live.
        //
        // This only BINDS the menu description; it does not necessarily surface the popup, and the
        // entries fill asynchronously after the bind. The caller polls PendingMenu()/EnsureShown():
        // surfacing here would put an entryless shell in front of the player whenever the entries had
        // not landed yet (the "menu opens but has no options" failure, observed live on the squad).
        manip.OpenOnShortcutEvent(NavigationSubmitEvent.GetPooled());
        error = null;
        return true;
    }

    /// <summary>
    /// The <c>CascadingMenu</c> bound to the subsystem's popup — the menu Send just opened — whether
    /// or not the popup has surfaced yet. Null when no menu context is bound. This is how a caller
    /// watches the asynchronous entry fill BEFORE showing anything: the game's own show
    /// (CascadingMenuPopup.ShowOrWaitForEntries) also waits for entries, but its entry-added callback
    /// does not fire when a warm cache fills the entries synchronously, so the caller must both poll
    /// this and drive <see cref="EnsureShown"/> once entries exist. Main thread only.
    /// </summary>
    public static VisualElement PendingMenu()
    {
        var popup = BoundPopup();
        return popup?.Menu;
    }

    /// <summary>
    /// Surface the popup the just-bound description belongs to. The subsystem holds one reused
    /// CascadingMenuPopup (m_cascadingMenuPopup) and the live menu context (m_popupContext); we only
    /// Show() when a context is actually bound, so a call after a failed Send never resurfaces a stale
    /// menu. Show() is idempotent on an already-shown popup, so the uncached path that surfaced on its
    /// own (the game's entry-added callback) is unharmed. Verified live: forces floating-element-root
    /// back into the tree. Main thread only. See docs/reference/context-menus.md.
    /// </summary>
    public static void EnsureShown() => BoundPopup()?.Show();

    // The subsystem's popup when a menu context is currently bound to it, else null.
    private static SI.UI.CascadingMenuPopup BoundPopup()
    {
        var popup = Popup(out bool contextBound);
        return contextBound ? popup : null;
    }

    /// <summary>
    /// The subsystem's single reused <c>CascadingMenuPopup</c> (null when the subsystem is missing),
    /// with <paramref name="contextBound"/> reporting whether a menu context is currently bound to
    /// it. Diagnostics need the popup even when unbound — a visible popup with no bound context is
    /// exactly the stale state worth seeing. Main thread only.
    /// </summary>
    public static SI.UI.CascadingMenuPopup Popup(out bool contextBound)
    {
        contextBound = false;
        var subsystem = SI.Core.GameSubsystems.Get<SI.UI.ContextMenuSubsystem>();
        if (subsystem == null) return null;

        var ssType = subsystem.GetIl2CppType();
        var ssFlags = Il2CppSystem.Reflection.BindingFlags.Instance
            | Il2CppSystem.Reflection.BindingFlags.Public
            | Il2CppSystem.Reflection.BindingFlags.NonPublic;

        contextBound = ssType.GetField("m_popupContext", ssFlags)?.GetValue(subsystem) != null;
        var popup = ssType.GetField("m_cascadingMenuPopup", ssFlags)?.GetValue(subsystem);
        return popup?.Cast<SI.UI.CascadingMenuPopup>();
    }

    /// <summary>
    /// The element's <c>ContextMenuDescriptionManipulator</c> instance, or null when it has none. The
    /// element's own field first (unchanged fast path), then <see cref="ManipulatorRegistry"/> for the
    /// manipulators the element does not store.
    /// </summary>
    public static SI.UI.ContextMenuDescriptionManipulator Manipulator(VisualElement e)
    {
        var field = ManipulatorField(e.GetIl2CppType());
        if (field != null)
        {
            try
            {
                var held = field.GetValue(e)?.Cast<SI.UI.ContextMenuDescriptionManipulator>();
                if (held != null) return held;
            }
            catch { /* fall through to the registry */ }
        }
        return ManipulatorRegistry.Get(e);
    }

    // The matching ContextMenuDescriptionManipulator field on a type (or a base type), cached.
    private static Il2CppSystem.Reflection.FieldInfo ManipulatorField(Il2CppSystem.Type type)
    {
        string key = type.FullName;
        if (_manipField.TryGetValue(key, out var cached)) return cached;
        Il2CppSystem.Reflection.FieldInfo found = null;
        var t = type;
        while (t != null && found == null)
        {
            foreach (var f in t.GetFields(Flags))
                if (f.FieldType.Name == "ContextMenuDescriptionManipulator") { found = f; break; }
            t = t.BaseType;
        }
        _manipField[key] = found;
        return found;
    }

}

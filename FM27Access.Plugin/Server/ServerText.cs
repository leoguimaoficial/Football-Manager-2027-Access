using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Stateless text/format helpers shared across the server's transport, walker, renderer
/// and handles: HTML/JS/SSE escaping, label/state/role reads off accessible elements,
/// screen titles and tokens, plus the background-thread file logger. No game state is
/// held here; every read goes live to the element passed in.
/// </summary>
internal static class ServerText
{
    internal static bool IsHeading(IAccessibleElement el)
    {
        try { return NavigationManager.IsHeading(el); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] IsHeading threw: {ex.Message}"); return false; }
    }

    // The profile heading level of a stop; 0 when none applies (generic title-text headings
    // carry no profile rule and so no level). A failed read degrades to 0, which renders at
    // the default depth — never a missing heading.
    internal static int HeadingLevel(IAccessibleElement el)
    {
        try { return NavigationManager.GetHeadingLevel(el); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] GetHeadingLevel threw: {ex.Message}"); return 0; }
    }

    // The active panel profile's rename, or the fallback when no profile renames this stop.
    // Used where the accessible name does not flow through DisplayText.Of — notably the
    // icon-only dropdown, the canonical rename case ("Advice actions").
    internal static string RenameOr(IAccessibleElement el, string fallback)
    {
        try
        {
            var r = NavigationManager.GetRenameLabel(el);
            return string.IsNullOrEmpty(r) ? fallback : r;
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] GetRenameLabel threw: {ex.Message}"); return fallback; }
    }

    internal static string Label(IAccessibleElement el)
    {
        try { var l = el.GetLabel(); if (!string.IsNullOrEmpty(l)) return l; }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] GetLabel threw: {ex.Message}"); }
        try { return el.GetAnnouncement(); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] GetAnnouncement threw: {ex.Message}"); return ""; }
    }

    internal static string State(IAccessibleElement el)
    {
        try { return el.GetState() ?? ""; }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] GetState threw: {ex.Message}"); return ""; }
    }

    /// <summary>
    /// Text the game has written under <paramref name="el"/> that <paramref name="known"/> does not
    /// already say, joined into one line. For controls whose rendered label is built from specific
    /// children and therefore misses a sibling the game put there to explain the control - the
    /// player instruction editor's "unavailable because..." line beside a disabled option is the
    /// case this exists for. Bounded, and returns null when there is nothing extra.
    /// </summary>
    internal static string ExtraTextUnder(IAccessibleElement el, string known)
    {
        var root = (el as Adapters.PluginAdapterBase)?.BackingElement;
        if (root == null) return null;
        try
        {
            var extra = new System.Text.StringBuilder();
            int budget = 80;
            CollectExtra(root, known ?? "", extra, ref budget, 0);
            return extra.Length == 0 ? null : extra.ToString();
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] extra text read threw: {ex.Message}");
            return null;
        }
    }

    private static void CollectExtra(UnityEngine.UIElements.VisualElement el, string known,
        System.Text.StringBuilder sb, ref int budget, int depth)
    {
        if (depth > 12 || budget-- <= 0) return;
        var text = el.TryCast<UnityEngine.UIElements.TextElement>();
        if (text != null)
        {
            string t = Clean(text.text);
            if (!string.IsNullOrWhiteSpace(t) && known.IndexOf(t, StringComparison.Ordinal) < 0
                && sb.ToString().IndexOf(t, StringComparison.Ordinal) < 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectExtra(el.hierarchy[i], known, sb, ref budget, depth + 1);
    }

    // The adapters report a disabled (greyed-out, unavailable) control by appending the
    // "disabled" token to their comma-joined state, each from its own enabled check. The HTML
    // surface reads that single signal rather than re-deriving disabled per adapter.
    internal static bool IsDisabled(IAccessibleElement el)
    {
        // These commands execute merged mod/native history, rather than click
        // a disabled native Forward button. Ordinary native availability stays
        // authoritative for every other control.
        if (el is Adapters.ButtonAdapter history && history.VirtualHistoryAvailable()) return false;
        // A control the game has greyed out wraps itself in a disabled SIEnabled element, which
        // propagates to enabledInHierarchy. Checking the backing element directly catches controls
        // whose state string is a value (dropdowns, comboboxes) rather than a "disabled" token.
        if (el is Adapters.PluginAdapterBase a)
        {
            try { if (!a.BackingElement.enabledInHierarchy) return true; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] enabledInHierarchy threw: {ex.Message}"); }
        }

        var state = State(el);
        if (string.IsNullOrEmpty(state)) return false;
        foreach (var part in state.Split(new[] { ", " }, StringSplitOptions.None))
            if (part == "disabled") return true;
        return false;
    }

    internal static string SafeRole(IAccessibleElement el)
    {
        try { return el.Role ?? ""; }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Role threw: {ex.Message}"); return ""; }
    }

    internal static string BackingNameOr(IAccessibleElement el, string fallback)
    {
        if (el is Adapters.PluginAdapterBase a)
        {
            var n = a.BackingElement.name;
            if (!string.IsNullOrEmpty(n)) return n;
        }
        return fallback;
    }

    internal static (float Value, float Low, float High, float Step) SliderRange(IAccessibleElement el)
    {
        if (el is Adapters.SliderAdapter s) return s.GetRange();
        return (0f, 0f, 100f, 0f);
    }

    /// <summary>The slider row's live value label ("0.75x"), or null when it has none.</summary>
    internal static string SliderValueText(IAccessibleElement el)
        => el is Adapters.SliderAdapter s ? s.GetValueText() : null;

    internal static string Num(float f) => f.ToString(CultureInfo.InvariantCulture);

    // A token that changes when the active screen changes, so the page can poll and
    // reload itself on in-game navigation. Panel identity if available, else the title.
    internal static string ScreenToken(object panelObj)
    {
        if (panelObj == null) return "none";

        string token = null;
        try
        {
            var id = NavigationManager.GetPanelIdentity(panelObj);
            if (!string.IsNullOrEmpty(id)) token = id;
        }
        catch { }
        token ??= GetTitle(panelObj);

        // FM27 reuses a native panel instance when navigating to another subject
        // of the same type (for example senior/youth squads). Its PanelID and
        // Identifier remain unchanged; the native binding root is rebound to a
        // different key. Include that context without depending on captions.
        if (panelObj is SI.Bindable.Panel nativePanel)
        {
            var root = nativePanel.m_bindingRoot?.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) token += "#binding:" + root.Key.m_key;
        }

        var tutorial = TutorialContent.Signature(panelObj);
        if (tutorial != null) token += "#tutorial:" + tutorial;

        // Some panels paginate their steps in place without loading a new panel (manager/coach
        // creation), so the identity above never changes between steps and the page would freeze
        // on the first one. Fold in the live step signature (detected structurally, no panel-name
        // list) so the SSE screen event fires per step. Empty (no change) for a panel with no
        // stepper, so the token stays the identity alone there.
        try
        {
            var step = ElementBridge.InPlaceStepSignature(panelObj);
            if (!string.IsNullOrEmpty(step)) token += "#" + step;
            var template = ElementBridge.RootTemplateSignature(panelObj);
            if (!string.IsNullOrEmpty(template)) token += "#template:" + template;
        }
        catch (Exception ex)
        {
            // Degrade to the panel identity alone (the stepper suffix is supplementary); never
            // crash the poll. Logged, not silent, per the no-silent-failures rule.
            LogBackground($"ScreenToken step signature failed: {ex.Message}");
        }

        return token;
    }

    // The aggregate screen token across the interactive set (Phase 3, plan §7.4): each panel's own
    // ScreenToken, combined in set order. Because the combine is order-sensitive and delimited, a
    // card opening/closing, a panel being added/removed, or any panel stepping in place moves the
    // value — so a set change that does not move the speech active panel still refreshes the page.
    internal static string ScreenToken(IReadOnlyList<InteractivePanels.PanelView> set)
    {
        ReplacedMessageHistory.Observe();
        var parts = new List<string>(set.Count + 1);
        foreach (var pv in set)
            parts.Add(ScreenToken(pv.Panel));
        // The modal Message view is mod-owned state outside the panel set; fold its open flag in
        // so opening and closing a message move the token. Before the menu segment, so stripping
        // "menu:" (a context menu opened over the modal) leaves the modal's base token intact —
        // app.js keys its stash/restore on this "msgview:" segment the way it keys on "menu:".
        var msgViewSig = MessageViewSurface.Signature();
        if (!string.IsNullOrEmpty(msgViewSig)) parts.Add(msgViewSig);
        // An open cascading context menu (the squad Actions menu) is a floating element outside the
        // panel set, so fold its entry signature in here — otherwise opening, drilling into, or
        // closing the menu would never move the token and the page would not refresh to show it.
        try
        {
            var menuSig = ContextMenuSurface.Signature();
            if (!string.IsNullOrEmpty(menuSig)) parts.Add(menuSig);
        }
        catch (Exception ex) { LogBackground($"Context-menu signature failed: {ex.Message}"); }
        // The bookmark editor popup floats outside the panel set too; folding its row signature
        // in refreshes the page on open, close, and every toggle (rows swap lists).
        try
        {
            var editorSig = BookmarkEditorSurface.Signature();
            if (!string.IsNullOrEmpty(editorSig)) parts.Add(editorSig);
        }
        catch (Exception ex) { LogBackground($"Bookmark-editor signature failed: {ex.Message}"); }
        // The mod's Sound Cues Settings page has no panel behind it at all (mod-owned state), so
        // fold its open flag in the same way: open and close each move the token and re-render.
        var soundCuesSig = SoundCuesSurface.Signature();
        if (!string.IsNullOrEmpty(soundCuesSig)) parts.Add(soundCuesSig);
        // The Navigator page is mod-owned state the same way.
        // Player names arriving from the game (a Data Hub player scatter) re-render the page.
        var namesSig = PlayerNameRequests.Signature();
        if (!string.IsNullOrEmpty(namesSig)) parts.Add(namesSig);
        var navigatorSig = NavigatorSurface.Signature();
        if (!string.IsNullOrEmpty(navigatorSig)) parts.Add(navigatorSig);
        // A generic filter popup floats outside the panel set the same way; folding its checkbox
        // signature in refreshes the page on open, close, and every toggle.
        try
        {
            var popupSig = FilterPopupSurface.Signature();
            if (!string.IsNullOrEmpty(popupSig)) parts.Add(popupSig);
        }
        catch (Exception ex) { LogBackground($"Filter-popup signature failed: {ex.Message}"); }
        return AggregateSignature.Combine(parts);
    }

    internal static string JsEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Escape '<' so a token containing "</script>" can't break out of the inline script.
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("<", "\\x3C").Replace("\r", "").Replace("\n", "\\n");
    }

    internal static string GetTitle(object panelObj) => ReadTitle(panelObj, allowInternalName: true);

    // Native captions can bind after PanelOpened. Keep internal identifiers for
    // document/bookkeeping fallback, but never speak one as an automatic arrival
    // title. No deferred whole-page/first-control announcement is needed.
    internal static string GetAnnouncementTitle(object panelObj) => ReadTitle(panelObj, allowInternalName: false);

    private static string ReadTitle(object panelObj, bool allowInternalName)
    {
        try
        {
            if (panelObj is SI.Bindable.Panel p)
            {
                // Must beat the Title-region branch below: the match transport strip sits in its
                // layer's "Title" region, so it would otherwise take the generic chrome heading.
                var chrome = ChromePanelTitle(p.PanelID.name);
                if (chrome != null) return chrome;

                var radar = RadarCardObservation.CardTitle(p);
                if (radar != null) return radar;

                // The title bar (the panel in its layer's "Title" region, the same signal
                // InteractivePanels uses for IsTitleBar) is the navigation chrome; its PanelID carries
                // no real title and would fall back to the raw "OverviewTitleBar" name. Label the
                // section for what the player uses it for instead.
                if (FM27PanelCatalog.IsTitleBar(p)) return TitleBarHeading;

                var portal = PortalDashboardTitle(p);
                if (portal != null) return portal;

                var squad = SquadReportTitle(p);
                if (squad != null) return squad;
                var site = SiteHeaderTitle(p);
                if (site != null) return site;

                var t = p.PanelID.Title.TranslatedString;
                if (!string.IsNullOrEmpty(t))
                {
                    // A native PanelID can carry an unexpanded gender token;
                    // the live navigation model already has the bound caption.
                    if (t.Contains("male_") || t.Contains("female_"))
                    {
                        var caption = NavigatorIndex.Build()?.FirstOrDefault(entry => entry.PanelUid == p.PanelID.ID
                            && entry.Depth > 0 && entry.ConditionMet && !entry.Disabled)?.Label;
                        if (!string.IsNullOrWhiteSpace(caption)) return caption;
                    }
                    t = StripUnresolvedEntityTokens(t);
                    if (!string.IsNullOrEmpty(t)) return t;
                }

                // A few game-creation panels bind no PanelID.Title, so the fallback below would
                // surface their raw internal panel name as the page heading — developer jargon a
                // sighted player never sees. Reuse the game's own on-screen title instead (the "Game
                // Setup" summary header; the delete-manager modal's "Are you sure you want to
                // delete?" prompt). Scoped to these panels by name — each has a profile hiding that
                // same title text from the body so it is not also read as a stray line — so every
                // other panel keeps the raw-name fallback untouched.
                var nativeDialog = NativeDialogTitle(p);
                if (!string.IsNullOrEmpty(nativeDialog)) return nativeDialog;
                if (TitleFromScreen.Contains(p.PanelID.name) && panelObj is VisualElement ve)
                {
                    var onScreen = OnScreenTitle(ve);
                    if (!string.IsNullOrEmpty(onScreen)) return onScreen;
                }
                var toolTitle = SharedMatchToolTitle(p);
                if (toolTitle != null) return toolTitle;
                var chartTitle = SharedChartCardTitle(p);
                if (chartTitle != null) return chartTitle;
                return allowInternalName ? p.PanelID.name ?? "Screen" : null;
            }
        }
        catch { }
        return allowInternalName ? "Screen" : null;
    }

    // The title bar's section heading: it is the navigation chrome (see GetTitle). Mod-authored
    // chrome label, paired with the secondary-nav "Sections" heading. Swap to rename.
    private const string TitleBarHeading = "Navigation";

    // The in-match chrome panels bind no PanelID.Title and the sighted screen titles them with
    // nothing at all, so both would fall back to developer jargon: the squad overview body heads
    // its section with the raw panel name ("OverviewPrototype" — titled "Your Squad", which is
    // what the section holds), and the playback transport strip sits in a Title region, taking a
    // second "Navigation" heading that is not navigation. "Match Controller" matches the header
    // checkbox that shows/hides the strip (OverviewHeader.json), so the player can connect the
    // toggle to the section it reveals. Mod-authored chrome labels (same precedent as
    // TitleBarHeading), shared with the speech surface's panel-ready announcement so both
    // surfaces title a panel identically.
    internal static string ChromePanelTitle(string panelName) => panelName switch
    {
        "FMpediaScreen" => "FMPedia",
        "OverviewPrototype" => "Your Squad",
        "MatchController" => "Match Controller",
        // These observed FM27 loading panels bind progress/content but no
        // written title. Keep a stable mod heading rather than their UXML ids;
        // the actual native percentages and messages remain in the body.
        "LoadingScreen" or "LoadGameLoadingScreen" or "MatchLoadingScreen" or "ApplicationProgressPanel" => "Loading",
        // The career-start loading/news screen binds no PanelID.Title, so it would head the page with
        // its raw internal name ("GameCreationLoadingScreen"). It presents the new club's profile and
        // news with the Begin Career button; "Career Start" names it for what the player is doing.
        "GameCreationLoadingScreen" => "Career Start",
        // The Data Hub's scatter cards bind an unresolved LocString ("string#1") as their title, so
        // the page headed itself with that token. The card is reused for every scatter analysis
        // (Crossing, Goal Output, Defensive Efficiency, the per-position player ones), so no one
        // name fits the subject — but the summary and the axis labels below name it, and a plain
        // description of the card beats either a broken token or the raw panel name.
        "ScatterGraphCard" => "Scatter Analysis",
        _ => null,
    };

    // The Portal dashboard's bound title is "Overview", but the mod hides its shortcut tiles
    // (every destination is already a Portal sections button), leaving the Messages widget as the
    // panel's entire content — so "Overview" would mislabel the page while a second "Messages"
    // heading sat right under it. Title the section with the widget's own on-screen heading
    // instead (game text, read live); the PortalScreen profile hides that heading from the body
    // so it is not also read as a stray line. Null (caller keeps the bound title) for any other
    // panel or when the widget heading is absent. Shared with the speech surface's panel-ready
    // announcement so both surfaces title the panel identically.
    internal static string PortalDashboardTitle(SI.Bindable.Panel p)
    {
        try
        {
            if (p.PanelID.name != "PortalScreen") return null;
            // FM27 embeds news, fixtures, standings and data tiles alongside the
            // inbox. Keep the native overview title and head each widget below.
            if (FindDescendantNamed(p, "PortalAdaptiveContent") != null) return null;
            var widget = FindDescendantNamed(p, "Messages");
            var heading = widget != null ? FindDescendantNamed(widget, "Heading") : null;
            string text = heading != null ? FindFirstTextElement(heading) : null;
            return string.IsNullOrEmpty(text) ? null : Clean(text);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Portal dashboard title read failed, keeping bound title: {ex.Message}");
            return null;
        }
    }

    // FM27 reuses TeamSquadReport (whose asset title is always First Team) for
    // senior and youth squads. Its bound card header identifies the actual team.
    // Read that header before the view dropdown/table, never a saved caption or
    // the first matching navigation UID: those destinations share the same UID.
    internal static string SquadReportTitle(SI.Bindable.Panel p)
    {
        if (p.PanelID.name != "TeamSquadReport") return null;
        var card = FindDescendantNamed(p, "FirstTeam");
        var text = card != null ? FindFirstTextElement(card) : null;
        return string.IsNullOrWhiteSpace(text) ? null : Clean(text);
    }

    private static string SiteHeaderTitle(SI.Bindable.Panel panel)
    {
        // FM27 site assets can name a reused subpage: ClubSite says "Club
        // Facilities" and NationSite says "National Team Venue" even while
        // displaying the country overview. Reuse the actual bound primary
        // header for this native site family, never a neighbouring panel's
        // entity or a translation inferred from the internal panel name.
        if (panel.PanelID.name?.EndsWith("Site", StringComparison.Ordinal) != true) return null;
        var header = FindDescendantNamed(panel, "SiteHeaderDynamicItem");
        for (var owner = header; owner != null; owner = owner.hierarchy.parent)
        {
            if (owner.GetIl2CppType().Name != "Panel") continue;
            return owner.Pointer == panel.Pointer ? FirstVisibleText(header) : null;
        }
        return null;
    }

    private static string FirstVisibleText(VisualElement element)
    {
        if (!ElementBridge.IsVisible(element)) return null;
        var text = element.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text)) return Clean(text);
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var found = FirstVisibleText(element.hierarchy[i]);
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }
        return null;
    }

    // FM renders an unresolved entity reference as a raw "<kind>#<index>" token. It leaks into
    // some titles whose PanelID.Title is bound without the subject resolved in context: "male#1
    // Profile" (leading, space-separated) or "Player Databaseteam#1" (trailing, run together with
    // no separator at all) — the on-screen header resolves fine, so this is a title-binding gap on
    // the game's side, not ours. The kinds are listed explicitly rather than matching any word:
    // "Databaseteam#1" shows the boundary before the token can't be found generically without
    // eating real title text. A title that was only the token strips to "", so GetTitle falls back
    // to the panel name. Shared with the speech surface's panel-ready announcement (and the
    // continue-button locator) so both surfaces title a panel identically. Lives here rather than
    // TextFilters because Speech is a permanent assembly — this must hot-reload with the core.
    // A "- " separator joined to the token comes off with it ("Competition - comp#1" would
    // otherwise strip to the dangling "Competition -"). "club" joined the list for the Youth
    // Setup screens, which title themselves "Youth Performanceclub#1" / "Youth Candidatesclub#1".
    private static readonly Regex LeadingEntityToken = new(@"^(?:male|female|team|comp|club|string)#\d+\s*(?:-\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingEntityToken = new(@"(?:\s*-)?\s*(?:male|female|team|comp|club|string)#\d+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string StripUnresolvedEntityTokens(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        title = LeadingEntityToken.Replace(title, "");
        title = TrailingEntityToken.Replace(title, "");
        return title.Trim();
    }

    // The game embeds entity links inside translated strings as
    // \x01<serialized payload>\x02<display text>\x01 — the payload is the machine half (binding +
    // tooltip, base64), the segment after \x02 is exactly what the game renders on screen.
    // Replacing the whole token with its display segment is a faithful decode of the game's own
    // format, not a heuristic. Strings without \x01 pass through untouched; token shapes that
    // don't match the three-delimiter grammar fall through to the generic control-character strip.
    // Lives here rather than TextFilters for the same reason as the strips above: Speech is a
    // permanent assembly, and this must hot-reload with the core.
    private static readonly Regex InlineEntityLink = new(
        "[^]*([^]*)", RegexOptions.Compiled);

    internal static string DecodeInlineEntityLinks(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('') < 0) return text;
        return InlineEntityLink.Replace(text, "$1");
    }

    // Panels whose PanelID.Title is unbound and whose real, sighted title lives in an on-screen
    // element instead (see GetTitle). Each pairs with a panel profile that hides that same title
    // text from the body, so it heads the section without also reading as a stray line.
    private static readonly HashSet<string> TitleFromScreen = new()
    {
        "GameCreationGameSummaryScreen",
        "GameCreationDeleteManagerWindow",
        // The budget-adjustment modal ("Budget Adjustment", an SIText named Title) and the
        // Debts And Loans transfer-debt popover ("Transfer Debt", inside a Title container).
        "ClubBudgetAdjustmentDialog",
        "TransferDebtTooltipCard",
        // The in-match shootout card, whose own title element is the lowercase "title" region.
        "PenaltyShootoutPanel",
        // The Recruitment Focus scout/analyst picker ("Assigned Scout" / "Assigned Analyst",
        // an SIText named "title") — the modal the Assign Scouts / Assign Analysts Edit
        // buttons open over the create/edit focus card.
        "RecruitmentFocusAssignScoutAnalystDialog",
        // The game's shared modal dialog — the confirm/prompt behind Rename View, Delete View and
        // many others. It binds no PanelID.Title at all, so every one of them headed the page with
        // the literal "GenericModalDialog". Its own "dialog-title" element carries the real prompt
        // ("Rename View 'Copy of Pass view'"), which is what a sighted player reads.
        "GenericModalDialog",
        "AddShortcutDialog",
        "WhatsNewDialog",
        "FMFCJoinSquadCard",
        "DatabaseSelectionCard",
        "AddNationsModal",
        "GameCreationCustomLeagueSetup",
        "ManagerCreationCloseConfirmWindow",
        "ManagerCreationScreen",
        "ManagerPersonaDetailsScreenContainer",
        "ManagerPersonaDetailsScreen_EditMode",
        "ManagerPersonaBackgroundScreenContainer",
        "ManagerPersonaManagementStyleScreenContainer",
          "ManagerPersonaManagementStyleScreen_EditMode",
          "GlobalSearchCard",
          "TutorialDialog",
          "FMpediaSectionScreen",
    };

    // The on-screen title of a panel whose PanelID.Title is unbound: the first text inside a
    // "Header" region, or failing that a title label (an SIText named "Title", e.g. a confirmation
    // modal's prompt). Read live. Null when neither exists, so the caller falls back to the raw
    // name rather than an empty or wrong title.
    // The panel-ready announcement's view of the TitleFromScreen branch in GetTitle, so the
    // speech surface titles these panels identically to the HTML section heading instead of
    // announcing the raw panel name. Null (caller keeps its own title) for unlisted panels or
    // when the on-screen title is absent — the raw-name fallback stays the honest degrade.
    internal static string TitleFromScreenOverride(SI.Bindable.Panel p)
    {
        try
        {
            var squad = SquadReportTitle(p);
            if (squad != null) return squad;
            var site = SiteHeaderTitle(p);
            if (site != null) return site;
            var title = p.PanelID.Title.TranslatedString;
            if (title != null && (title.Contains("male_") || title.Contains("female_"))) return GetTitle(p);
            if (!TitleFromScreen.Contains(p.PanelID.name))
                return string.IsNullOrEmpty(p.PanelID.Title.TranslatedString)
                    ? NativeDialogTitle(p) ?? SharedMatchToolTitle(p) ?? SharedChartCardTitle(p) : null;
            return OnScreenTitle(p);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] On-screen title read failed, keeping panel-name title: {ex.Message}");
            return null;
        }
    }

    // FM27 chart cards can leave the outer title bar unbound and put their
    // actual heading in the shared fixture-details component beside the teams
    // and date. Exclude that contextual branch rather than using its first club
    // as a title. No raw panel names or remembered opening-button captions.
    private static string SharedChartCardTitle(SI.Bindable.Panel panel)
    {
        var controller = FindDescendantNamed(panel, "CardController");
        if (controller?.GetIl2CppType().Name != "BaseCardTemplateImpl") return null;
        var details = FindDescendantNamed(controller, "fixture-details-frame");
        if (details == null || !ElementBridge.IsVisibleInHierarchy(details)
            || FindDescendantNamed(details, "teams-and-dates-frame") == null) return null;
        var captions = new List<string>();
        ReadChartHeading(details, captions, 0);
        return captions.Count == 1 ? captions[0] : null;
    }

    private static void ReadChartHeading(VisualElement element, List<string> captions, int depth)
    {
        if (depth > 8 || !ElementBridge.IsVisible(element)
            || element.name == "teams-and-dates-frame"
            || element.GetIl2CppType().Name == "SIButton") return;
        if (element.TryCast<TextElement>() is { } text)
        {
            var caption = Clean(text.text);
            if (!string.IsNullOrWhiteSpace(caption)) captions.Add(caption);
            return;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
            ReadChartHeading(element.hierarchy[i], captions, depth + 1);
    }

    // FM27's in-match tools have unbound PanelID titles. Their shared, visible
    // title-bar tab strip owns the actual tool caption. Read its selected tab
    // for the foreground report, retaining the same title on HTML and speech.
    // Cards/overlays and background reports cannot borrow this toolbar's title.
    private static string SharedMatchToolTitle(SI.Bindable.Panel panel)
    {
        if (panel.parent?.name != "ReportBody"
            || FM27PanelCatalog.GetForegroundPanel()?.Pointer != panel.Pointer) return null;
        foreach (var layer in FM27PanelCatalog.GetNativeLayers(SI.Bindable.PanelManager.Instance))
        foreach (var header in layer.Open)
        {
            if (!header.IsVisible || !header.IsActive || !FM27PanelCatalog.IsTitleBar(header)) continue;
            var tabs = FindDescendantNamed(header, "TopBarTacticsButtonGroup");
            if (tabs == null || !ElementBridge.IsVisibleInHierarchy(tabs)) continue;
            var selected = new List<string>();
            ReadSelectedToolTabs(tabs, selected, 0);
            if (selected.Count == 1) return selected[0];
        }
        return null;
    }

    private static void ReadSelectedToolTabs(VisualElement element, List<string> selected, int depth)
    {
        if (depth > 16 || !ElementBridge.IsVisible(element)) return;
        if (element.GetIl2CppType().Name == "SIButton")
        {
            var button = new Adapters.ButtonAdapter(element);
            if (button.GetState().Split(new[] { ", " }, StringSplitOptions.None).Contains("selected"))
            {
                var label = Clean(button.GetLabel());
                if (!string.IsNullOrWhiteSpace(label)) selected.Add(label);
            }
            return;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
            ReadSelectedToolTabs(element.hierarchy[i], selected, depth + 1);
    }

    private static string OnScreenTitle(VisualElement panel)
    {
        if (panel is SI.Bindable.Panel promo && promo.PanelID.name == "FMFCJoinSquadCard")
        {
            // This native promo card has an image/close-only TitleHeader;
            // its written heading starts the content frame beneath the image.
            var content = FindDescendantNamed(panel, "CardContent");
            var frame = content == null ? null : FindDescendantNamed(content, "Frame");
            return frame == null ? null : FirstVisibleText(frame);
        }
        if (panel is SI.Bindable.Panel article && article.PanelID.name == "FMpediaSectionScreen")
        {
            var title = FindDescendantNamed(panel, "CurrentTitle");
            return title != null ? Clean(title.TryCast<TextElement>()?.text) : null;
        }
        if (panel is SI.Bindable.Panel tutorial && tutorial.PanelID.name == "TutorialDialog")
        {
            var floating = TutorialContent.TitleFor(tutorial);
            if (floating != null) return floating;
            var texts = FindDescendantNamed(panel, "FrameTexts");
            return texts != null ? Clean(FindFirstTextElement(texts)) : null;
        }
        if (panel is SI.Bindable.Panel persona && persona.PanelID.name.StartsWith("ManagerPersona"))
            return Clean(FindFirstTextElement(panel));
        if(panel is SI.Bindable.Panel custom && custom.PanelID.name=="GameCreationCustomLeagueSetup")
            return Clean(FindFirstTextElement(panel));
        var header = FindDescendantNamed(panel, "Header");
        string text = header != null ? FindFirstTextElement(header) : null;
        if (string.IsNullOrEmpty(text))
        {
            var title = FindDescendantNamed(panel, "Title") ?? FindDescendantNamed(panel, "title");
            if (title != null) text = FindFirstTextElement(title);
        }
        // The shared modal dialog names its title element outright rather than wrapping it in a
        // Header/Title region, so neither lookup above reaches it.
        if (string.IsNullOrEmpty(text))
        {
            var dialogTitle = FindDescendantNamed(panel, "dialog-title");
            if (dialogTitle != null) text = FindFirstTextElement(dialogTitle);
        }
        if(string.IsNullOrEmpty(text))
        {
            var titleText=FindDescendantNamed(panel,"title-text");
            if(titleText!=null)text=FindFirstTextElement(titleText);
        }
        // These FM27 cards have no header/title slot. Their first visible native
        // caption is the heading shown on screen (database / add nations).
        if(string.IsNullOrEmpty(text) && panel is SI.Bindable.Panel p
            && p.PanelID.name is "DatabaseSelectionCard" or "AddNationsModal")
            text=FindFirstTextElement(panel);
        return string.IsNullOrEmpty(text) ? null : Clean(text);
    }

    private static string NativeDialogTitle(VisualElement panel)
    {
        // Shared FM27 notification template: an unnamed caption immediately before
        // dialog-description. Read that native slot, never the prompt's wording or
        // the panel's internal identifier. Do not adopt text inside action buttons.
        var description = FindDescendantNamed(panel, "dialog-description");
        if (description == null)
        {
            var formTitle = NativeInputDialogTitleElement(panel)?.TryCast<TextElement>();
            return string.IsNullOrWhiteSpace(formTitle?.text) ? null : Clean(formTitle.text);
        }
        var parent = description?.parent;
        if (parent == null) return null;
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var sibling = parent.hierarchy[i];
            if (sibling.Pointer == description.Pointer) break;
            if (!ElementBridge.IsVisible(sibling)) continue;
            var text = sibling.TryCast<TextElement>();
            if (text != null && !string.IsNullOrWhiteSpace(text.text)) return Clean(text.text);
        }
        return null;
    }

    internal static VisualElement DialogTitleElement(VisualElement panel)
    {
        if (panel == null) return null;
        if (FindDescendantNamed(panel, "dialog-description") == null)
            return NativeInputDialogTitleElement(panel);
        return FindDescendantNamed(panel, "dialog-title");
    }

    internal static bool HasNativeDialogActions(VisualElement panel)
        => panel != null && (FindDescendantNamed(panel, "dialog-description") != null
                || NativeInputDialogTitleElement(panel) != null)
            && FindDescendantNamed(panel, "notifications-modal-dialog-actions-default") != null;

    private static VisualElement NativeInputDialogTitleElement(VisualElement panel)
    {
        // The shared notification FORM template has no dialog-description slot.
        // Its frame/body/header holds the native caption before the edit control.
        // Stay in that header: values, passwords and action labels are not titles.
        var template = FindDescendantNamed(panel, "notifications-modal-dialog-default-default");
        if (template == null || FindDescendantNamed(template, "notifications-modal-dialog-actions-default") == null)
            return null;
        var frame = FindDescendantNamed(template, "frame");
        var body = frame == null ? null : FindDescendantNamed(frame, "Frame");
        var header = body == null ? null : FindDescendantNamed(body, "Frame");
        return header == null ? null : FindVisibleHeaderCaption(header);
    }

    private static VisualElement FindVisibleHeaderCaption(VisualElement element)
    {
        if (!ElementBridge.IsVisibleInHierarchy(element)) return null;
        if (element.GetIl2CppType().Name == "SIText"
            && !string.IsNullOrWhiteSpace(element.TryCast<TextElement>()?.text)) return element;
        if (TreeWalker.NavigationStops.Contains(ElementBridge.ClassifyElement(element, null))) return null;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var caption = FindVisibleHeaderCaption(element.hierarchy[i]);
            if (caption != null) return caption;
        }
        return null;
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if ((child.name ?? "") == name) return child;
            var deeper = FindDescendantNamed(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string FindFirstTextElement(VisualElement root)
    {
        // Named title slots can themselves look visible beneath a hidden
        // native branch. Check their ancestry, then reuse the visibility-aware
        // recursive reader so inactive header chrome never becomes a title.
        return root != null && ElementBridge.IsVisibleInHierarchy(root)
            ? FirstVisibleText(root) : null;
    }

    // SSE data must be newline-free per data: line; tokens are single-line in practice
    // but a stray newline would silently corrupt the frame.
    internal static string SseEscape(string s) => s.Replace("\r", "").Replace("\n", " ");

    private static readonly char[] HtmlEscapeChars = { '&', '<', '>', '"' };

    internal static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // The common case is a clean string; four chained Replace calls each allocate even when
        // nothing matches, and this runs once per rendered cell/label.
        if (s.IndexOfAny(HtmlEscapeChars) < 0) return s;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // Game-derived text carries Unity rich-text markup (sprites, links, color/size/etc.)
    // that the speech surface strips in its filter chain. Every surface that emits an
    // accessible name or announcement must strip it too, or the raw tags surface literally
    // (the browser renders them, a plain-text dev view prints them). Plain text uses Clean;
    // HTML uses EscapeText, which cleans first then makes the cleaned text HTML-safe.
    internal static string Clean(string s) => TextFilters.CleanForDisplay(s);

    internal static string EscapeText(string s) => Escape(Clean(s));

    /// <summary>
    /// The reply for a surface action that threw. <paramref name="spoken"/> is what the player hears —
    /// a short fact about what did not happen — while the exception's own detail goes to the log for
    /// whoever is diagnosing it. Replaces the "Error: " + ex.Message pattern, which read the mod's
    /// internals out loud (a dispatch timeout used to announce itself as "Main-thread dispatch timed
    /// out (is the game updating?)", which is neither actionable nor even about the player's action).
    ///
    /// A dispatch timeout is the exception: MainThreadDispatcher already phrases those as the state
    /// the surface is in ("The game is not responding.", "The accessibility mod has stopped."), which
    /// is more truthful than a per-action message, so that text is passed through.
    /// </summary>
    internal static string Failure(string spoken, Exception ex)
    {
        Degrade.Note($"surface action failed ({spoken}): {ex.GetType().Name}: {ex.Message}");
        return ex is TimeoutException ? ex.Message : spoken;
    }

    private static string _logPath;

    // No IL2CPP calls or held file handles on request threads. Rotate on every append,
    // including sessions that cross the limit without reloading the core.
    internal static void LogBackground(string msg)
    {
        try
        {
            _logPath ??= System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", "server.log");
            FM27Access.Speech.BoundedLogFile.Append(_logPath, "[AccessibilityServer] " + msg);
        }
        catch (Exception ex)
        {
            FM27Access.Speech.Log.Warn("[AccessibilityServer] Could not write server.log: " + ex.Message);
        }
    }

}

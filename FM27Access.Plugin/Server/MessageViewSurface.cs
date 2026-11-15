using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The modal Message view: an opened email rendered as the whole page — sender, subject, time,
/// body and attachments (the reading pane's live subtree) — instead of an inline pane under the
/// inbox. While it is open, ComposeBody renders ONLY this section (the context-menu / bookmark
/// editor precedent); the base Messages page never shows the reading pane at all (ScreenWalker
/// skips it), so a message is either open modally or not shown. Escape (FM.msgview in app.js)
/// closes it via /message/close and the companion restores the stashed list page, focus landing
/// back on the row the player opened.
///
/// The game has no modal state behind this: clicking a message updates the reading pane in place,
/// with the content arriving asynchronously. So the open is mod-owned and verified — OpenForRow
/// activates the row, then polls until the pane's subject matches the clicked row's subject
/// before declaring the view open. A pane that never matches within the budget means the click
/// did not land (or navigated elsewhere): no modal, an honest reply, never a pane presented as
/// the message the player asked for. Content is walked live at every render, never cached.
///
/// The view self-closes whenever the top panel changes (an attachment opening its article, ribbon
/// or bookmark navigation, the continue flow), so it can never trap the player over a screen it
/// no longer describes. A hot reload resets it closed — harmless, the next poll renders the base
/// screen.
///
/// Opening and dismissing the view also maintain its entry in <see cref="MessageViewHistory"/>, so
/// the message is a real back/forward destination rather than state the history forgets. See
/// docs/message-view-history-plan.md.
/// </summary>
internal static class MessageViewSurface
{
    private const string PanelName = "PortalMessagesTool";
    private const string ContentRootName = "MessagesToolBlockMessageContent";
    private const string SubjectElementName = "MessageTitle";
    private const string CloseLabel = "Close message";

    // FM26 message prose, titles and metadata are readonly text. Keep useful
    // person/entity-list and table links under their own native components.
    // Ownership is structural, independent of the caption and game language.
    internal static bool IsNarrativeText(VisualElement element)
    {
        if (element == null || element.GetIl2CppType().Name != "SIText"
            || Adapters.TableRowTextAdapter.FindTableRow(element) != null) return false;
        for (var ancestor = element.parent; ancestor != null; ancestor = ancestor.parent)
        {
            if (ancestor.name == ContentRootName) return true;
            if (ancestor.GetIl2CppType().Name is "StreamedObjectList" or "NavigableLink") return false;
            if (ancestor.TryCast<SI.Bindable.Panel>() != null) break;
        }
        return false;
    }

    private const int OpenBudgetMs = 2500;   // pane content routinely lands within a few hundred ms
    private const int OpenStepMs = 100;
    // The grace given to a pane that is already standing and merely rebinding, before the restore
    // stops waiting for it and re-selects the row instead. Everything else about a restore's timing is
    // bounded by the one deadline its caller passes in, so a press can never spend the sum of several
    // independent budgets.
    private const int PaneGraceMs = 600;

    // Spoken only when a recorded message could not be brought back: the player is left on the inbox
    // rather than shown a message the mod cannot confirm.
    private const string RestoreFailed = "Message unavailable.";

    // Mod-owned open state. _panelIdentity is the top panel's identity at open time: the
    // self-close anchor, compared (never dereferenced) on every build.
    private static bool _open;
    private static string _panelIdentity;
    private static MessageSearchReturn.Origin _searchOrigin;

    internal static bool IsOpen() => _open;

    /// <summary>
    /// True when the view is open but no longer describes what is on screen — its panel is gone, or
    /// another panel opened on top (an attachment's card, a ribbon jump).
    /// </summary>
    private static bool IsStale(out VisualElement panel)
    {
        panel = FindMessagesPanel();
        var top = TopPanel();
        return panel == null || top == null || PanelIdentity(top) != _panelIdentity;
    }

    /// <summary>
    /// Close the view if it has been left behind by a screen change. Nothing watches the panel stack,
    /// so the flag only self-corrects when the page is next rendered — which means a keypress arriving
    /// between the screen change and that render would otherwise act on a modal the player is no
    /// longer looking at, and be spent closing it. Call this before ACTING on the open flag, not just
    /// before rendering it. Main thread.
    /// </summary>
    internal static void CloseIfStale()
    {
        ReplacedMessageHistory.Observe();
        if (_open && IsStale(out _)) CloseIfOpen(discardPending: false);
    }

    /// <summary>
    /// Show the live game history to <see cref="MessageViewHistory"/> so entries that no longer
    /// describe the stack are pruned. A history that cannot be read prunes nothing and leaves the
    /// feature dormant. Main thread.
    ///
    /// Deliberately lazy — this runs when a press acts on the history or a message opens or closes,
    /// NOT on every panel load. Pruning compares the live stack against the snapshot from the last
    /// look, so a push that happens between two looks is caught by the later one; observing per panel
    /// load would put game reflection on a hot path for no extra correctness.
    /// </summary>
    internal static void ObserveHistory()
    {
        ReplacedMessageHistory.Observe();
        var live = GameHistory.TryRead();
        if (live != null)
            MessageViewHistory.Current.Observe(live.Value.Index, live.Value.Count, live.Value.Shape);
    }

    /// <summary>Folded into the screen token so the SSE poll re-renders on open and close. The
    /// "msgview:" segment is also what app.js keys its stash/restore and Escape handling on.</summary>
    internal static string Signature() => _open ? "msgview:open" : "";

    /// <summary>
    /// The player dismissed the view — Escape, or the "Close message" stop. Distinct from
    /// <see cref="CloseIfOpen"/>: a dismiss also retires the view's history entry, so a later
    /// Forward does not resurrect a message the player deliberately put away. Main thread (it reads
    /// the game's history to know which stack position is being dismissed).
    /// </summary>
    internal static void Close()
    {
        var search = _open && !IsStale(out _) ? _searchOrigin : null;
        var messageId = search != null ? CurrentMessageId : null;
        _searchOrigin = null;
        if (_open)
        {
            var live = GameHistory.TryRead();
            if (live != null)
            {
                ReplacedMessageHistory.Dismiss(live.Value.Index);
                MessageViewHistory.Current.NoteDismissed(live.Value.Index);
            }
        }
        _open = false;
        MessageSearchReturn.Return(search, messageId);
    }

    /// <summary>Close if open; a no-op otherwise. Navigation hygiene (nav/bookmark/history/
    /// navigator), so navigating away never leaves a stale modal over the new screen. Deliberately
    /// leaves the view's history entry standing — the player was still inside the message when they
    /// navigated, which is exactly what makes a later Back bring it back.</summary>
    internal static void CloseIfOpen(bool discardPending = true)
    {
        _open = false;
        if (discardPending) ReplacedMessageHistory.CancelPending();
        // A linked profile can return through native history to this exact
        // message. Retain search provenance until a new explicit message or
        // dismissal; Return revalidates its actual source visit/query/NewsItem.
    }

    internal static void BeforeNativeContentAction(IAccessibleElement element)
    {
        if (!_open || IsStale(out var panel) || element is not Adapters.PluginAdapterBase adapter) return;
        var content = FindByName(panel, ContentRootName);
        if (content == null) return;
        for (var parent = adapter.BackingElement; parent != null; parent = parent.hierarchy.parent)
            if (parent.Pointer == content.Pointer)
            {
                ReplacedMessageHistory.BeforeContentAction(
                    MessageVerificationKey.Create(ReadPaneId(panel), ReadPaneSubject(panel)));
                return;
            }
    }

    /// <summary>
    /// Open the message behind a table row's handle: activate the row, then poll until the
    /// reading pane shows that row's subject. Works from both inboxes — on the standalone
    /// Messages panel the pane updates in place; on the Portal dashboard the click first
    /// navigates to the Messages panel and the pane fills there, so a missing panel is a
    /// wait state, not a failure. Request thread (sleeps between per-frame Invokes, the
    /// context-menu open precedent). Empty reply on success — the token change re-renders
    /// the page — and an honest, speakable message on failure.
    /// </summary>
    internal static string OpenForRow(string id, string expectedScreen = null)
    {
#if FM27_DEVELOPER_TOOLS
        var openTiming = System.Diagnostics.Stopwatch.StartNew();
        long dispatchTiming = 0;
        int openPolls = 0;
#endif
        try
        {
            string initialTop = null;
            string expected = null;
            MessageSearchReturn.Origin searchOrigin = null;
            var actErr = ServerHandles.Run(id, h =>
            {
#if FM27_DEVELOPER_TOOLS
                Log.Debug($"[MessageTiming] actionEntered={openTiming.ElapsedMilliseconds}ms");
#endif
                var row = (h as ServerHandles.StopHandle)?.El as Adapters.MessageButtonAdapter;
                if (row == null) return "Not a message row.";
                if (!row.CanActivate) return "This message preview is read-only.";
                var subject = row.GetSubject();
                expected = MessageVerificationKey.Create(row.MessageId,
                    subject != null ? TextFilters.CleanForDisplay(subject).Trim() : "");
                // Capture the origin and activate the validated native row in
                // one Unity dispatch. A separate origin read gave a filter one
                // extra frame to retire that command before activation.
                var top = TopPanel();
                initialTop = top != null ? PanelIdentity(top) : "";
                searchOrigin = MessageSearchReturn.Capture(top?.TryCast<SI.Bindable.Panel>(), row.MessageId);
                _searchOrigin = null;
#if FM27_DEVELOPER_TOOLS
                Log.Debug($"[MessageTiming] captured={openTiming.ElapsedMilliseconds}ms");
#endif
                row.Activate();
#if FM27_DEVELOPER_TOOLS
                Log.Debug($"[MessageTiming] nativeActivated={openTiming.ElapsedMilliseconds}ms");
#endif
                return null;
            }, expectedScreen);
            if (actErr != null) return actErr;
#if FM27_DEVELOPER_TOOLS
            dispatchTiming = openTiming.ElapsedMilliseconds;
#endif

            if (string.IsNullOrEmpty(expected))
            {
                return "The message could not be identified. Refresh the inbox before opening it.";
            }

            long start = Environment.TickCount64;
            while (true)
            {
#if FM27_DEVELOPER_TOOLS
                openPolls++;
#endif
                var state = MainThreadDispatcher.Invoke(() =>
                {
                    var panel = FindMessagesPanel();
                    if (panel != null)
                    {
                        var current = ReadPaneSubject(panel);
                        if (MessageVerificationKey.Matches(expected, ReadPaneId(panel), current)) return "match";
                        return "wait";
                    }
                    // No Messages panel on screen: either the dashboard click's navigation to it
                    // is still in flight (wait) or the click went somewhere else entirely (a
                    // press-conference row) — then the screen change speaks for itself.
                    var top = TopPanel();
                    if (top != null && top.name != PanelName && PanelIdentity(top) != initialTop)
                        return "navigated";
                    return "wait";
                });
                if (state == "navigated")
                    return "";
                if (state == "match")
                    return MainThreadDispatcher.Invoke(() =>
                    {
                        if (!OpenNow()) return "Message did not open.";
                        _searchOrigin = searchOrigin;
#if FM27_DEVELOPER_TOOLS
                        Log.Debug($"[MessageTiming] dispatch={dispatchTiming}ms total={openTiming.ElapsedMilliseconds}ms polls={openPolls}");
#endif
                        return "";
                    });
                if (Environment.TickCount64 - start >= OpenBudgetMs)
                {
                    Degrade.Note($"message view: pane never showed \"{expected}\" within budget");
                    return "Message did not open.";
                }
                System.Threading.Thread.Sleep(OpenStepMs);
            }
        }
        catch (Exception ex)
        {
            Degrade.Note($"message view open failed: {ex.GetType().Name}: {ex.Message}");
            return "Message did not open.";
        }
    }

    /// <summary>
    /// Reopen the message recorded at a history position — the back/forward restore. Request thread
    /// (it sleeps between per-frame Invokes, the OpenForRow pattern).
    ///
    /// The reading pane keeps the last-opened message across a back navigation (verified live), so the
    /// normal path is: wait for the Messages panel, confirm the pane still shows
    /// <paramref name="expectedSubject"/>, open. When the pane holds something else the row is
    /// re-selected and the open verified again. When neither works the entry is retired and the
    /// player is told — the inbox stays on screen and nothing is presented as a message the mod
    /// cannot confirm it is showing.
    ///
    /// Empty reply on success (the screen change speaks for itself) and a short speakable string
    /// only when the message could not be brought back.
    /// </summary>
    internal static string Restore(int index, string expectedSubject, long deadline, Func<bool> superseded)
    {
        try
        {
            if (!WaitForMessagesPanel(deadline, superseded))
            {
                // The landing is not the screen this entry belongs to (or a newer press took over).
                // Keep the entry — a later press can still use it — and stay quiet: the screen the
                // player is actually on speaks for itself.
                Degrade.Note($"message view: restore did not reach the Messages panel in time (index {index})");
                return "";
            }

            // Whether the pane still holds the message depends on what the player came back from: a
            // card opened over the inbox leaves it standing, a full screen rebuilds the panel with a
            // fresh pane. So the retained-pane path gets a short grace for a late bind, not the whole
            // budget, and a rebuilt pane falls through to re-selecting the row.
            var search = MainThreadDispatcher.Invoke(() => _searchOrigin);
            MessageSearchReturn.RestoreInboxSearch(search, MessageVerificationKey.Id(expectedSubject), deadline, superseded);
            if (superseded()) return "";
            long graceUntil = Min(Environment.TickCount64 + PaneGraceMs, deadline);
            if (WaitForPaneSubject(expectedSubject, graceUntil, superseded)) return "";
            if (superseded()) return "";
            return RestoreByRow(index, expectedSubject, deadline, superseded);
        }
        catch (Exception ex)
        {
            // Anything unexpected — a dispatch timeout because the game is busy, an element destroyed
            // mid-read — lands here. Retire the entry so the next press is a plain, fast history move
            // rather than a repeat of this wait, and tell the player the message is not coming back.
            Degrade.Note($"message view restore failed: {ex.GetType().Name}: {ex.Message}");
            MessageViewHistory.Current.Drop(index);
            return RestoreFailed;
        }
    }

    // The reading pane does not hold the message: re-select its row and verify the open, exactly as a
    // fresh click would. The inbox list is virtualised and streams in after the panel, so the row is
    // polled for rather than demanded at once — but a row that never realises (the message has paged
    // out of the window, or is gone) retires the entry and reports the gap instead of guessing.
    private static string RestoreByRow(int index, string expectedSubject, long deadline, Func<bool> superseded)
    {
        while (true)
        {
            bool activated = MainThreadDispatcher.Invoke(() =>
            {
                var panel = FindMessagesPanel();
                var row = panel == null ? null : ScreenWalker.FindMessageRowBySubject(panel,
                    MessageVerificationKey.Subject(expectedSubject), MessageVerificationKey.Id(expectedSubject));
                if (row == null) return false;
                row.Activate();
                return true;
            });
            if (activated) break;

            if (superseded()) return "";
            if (Environment.TickCount64 >= deadline)
            {
                Degrade.Note($"message view: no realised inbox row carries \"{expectedSubject}\", restore abandoned");
                MessageViewHistory.Current.Drop(index);
                return RestoreFailed;
            }
            System.Threading.Thread.Sleep(OpenStepMs);
        }

        if (WaitForPaneSubject(expectedSubject, deadline, superseded)) return "";
        if (superseded()) return "";

        Degrade.Note($"message view: pane never showed \"{expectedSubject}\" after re-selecting its row");
        MessageViewHistory.Current.Drop(index);
        return RestoreFailed;
    }

    // Wait for the Messages panel to be the top panel — the screen an entry belongs to, and what
    // OpenNow requires before it will flip open. Request thread.
    private static bool WaitForMessagesPanel(long deadline, Func<bool> superseded)
    {
        while (true)
        {
            bool ready = MainThreadDispatcher.Invoke(() =>
            {
                var top = TopPanel();
                return top != null && top.name == PanelName && FindMessagesPanel() != null;
            });
            if (ready) return true;
            if (superseded() || Environment.TickCount64 >= deadline) return false;
            System.Threading.Thread.Sleep(OpenStepMs);
        }
    }

    // Poll until the reading pane shows this subject, then open. Request thread. OpenForRow keeps its
    // own richer loop: a row click there may navigate the whole screen (the Portal dashboard case),
    // which this path cannot do — the panel is already the one the entry belongs to.
    private static bool WaitForPaneSubject(string expected, long deadline, Func<bool> superseded)
    {
        while (true)
        {
            bool opened = MainThreadDispatcher.Invoke(() =>
            {
                var panel = FindMessagesPanel();
                if (panel == null) return false;
                var current = ReadPaneSubject(panel);
                if (!MessageVerificationKey.Id(expected).HasValue
                    && ScreenWalker.FindMessageRowBySubject(panel, expected) == null) return false;
                return MessageVerificationKey.Matches(expected, ReadPaneId(panel), current) && OpenNow(restoring: true);
            });
            if (opened) return true;
            if (superseded() || Environment.TickCount64 >= deadline) return false;
            System.Threading.Thread.Sleep(OpenStepMs);
        }
    }

    private static long Min(long a, long b) => a < b ? a : b;

    // Flip open, anchored to the current top panel, and record the view's history entry so a later
    // Back can bring the player back to this message. Main thread.
    private static bool OpenNow(bool restoring = false)
    {
        var top = TopPanel();
        if (top == null || top.name != PanelName) return false;
        _panelIdentity = PanelIdentity(top);
        _open = true;

        var live = GameHistory.TryRead();
        var key = MessageVerificationKey.Create(ReadPaneId(top), ReadPaneSubject(top));
        if (live != null && !ReplacedMessageHistory.NoteOpened(key, restoring))
            MessageViewHistory.Current.NoteOpened(live.Value.Index, live.Value.Shape,
                key);
        return true;
    }

    /// <summary>
    /// The modal section for ComposeBody, or null when closed (or self-closed: panel gone or the
    /// top panel changed since open). Main thread, inside ComposeBody's Invoke; the caller
    /// activates the Messages panel's profile before this so heading and hide rules apply to the
    /// walked content.
    /// </summary>
    internal static (string Title, List<ScreenWalker.RenderItem> Items)? TryBuildSection()
    {
        if (!_open) return null;

        if (IsStale(out var panel))
        {
            ReplacedMessageHistory.Observe();
            CloseIfOpen(discardPending: false);
            return null;
        }

        var contentRoot = FindByName(panel, ContentRootName);
        if (contentRoot == null)
        {
            Degrade.Note("message view: reading pane missing, closing");
            CloseIfOpen();
            return null;
        }

        var items = new List<ScreenWalker.RenderItem>
        {
            new ScreenWalker.StopItem { Element = new CloseAdapter() },
        };
        items.AddRange(ScreenWalker.WalkMessageContent(contentRoot));

        string title = ReadPaneSubject(panel);
        var subject = FindByName(contentRoot, SubjectElementName);
        if (!string.IsNullOrEmpty(title) && subject != null)
            foreach (var item in items)
                if (item is ScreenWalker.StopItem stop
                    && stop.Element is Adapters.PluginAdapterBase native
                    && native.BackingElement.Pointer == subject.Pointer)
                {
                    stop.IsSectionTitle = true;
                    break;
                }
        return (string.IsNullOrEmpty(title) ? "Message" : title, items);
    }

    /// <summary>The Messages panel object for profile activation, or null. Main thread.</summary>
    internal static object PanelForProfile()
    {
        return FindMessagesPanel();
    }

    // --- live lookups (main thread, never cached) -----------------------------------------------

    private static VisualElement FindMessagesPanel()
    {
        foreach (var pv in InteractivePanels.Current())
        {
            VisualElement el = pv.Panel;
            if (el != null && el.name == PanelName) return el;
        }
        return null;
    }

    private static VisualElement TopPanel()
    {
        var set = InteractivePanels.Current();
        return set.Count > 0 ? set[set.Count - 1].Panel : null;
    }

    private static string PanelIdentity(VisualElement panel)
    {
        try
        {
            var id = NavigationManager.GetPanelIdentity(panel);
            if (!string.IsNullOrEmpty(id)) return id;
        }
        catch (Exception ex) { Log.Warn($"[MessageViewSurface] panel identity read failed: {ex.Message}"); }
        return panel.name ?? "";
    }

    // The reading pane's current subject, cleaned and trimmed — the open-verification anchor and
    // the modal's title. Null when unreadable.
    internal static int? ReadPaneId(VisualElement panel)
        => NativeMessageIdentity.Read(panel == null ? null : FindByName(panel, ContentRootName));

    internal static int? CurrentMessageId => ReadPaneId(FindMessagesPanel());

    private static string ReadPaneSubject(VisualElement panel)
    {
        var content = FindByName(panel, ContentRootName);
        if (content == null) return null;
        var titleEl = FindByName(content, SubjectElementName);
        var text = titleEl?.TryCast<TextElement>()?.text;
        if (string.IsNullOrEmpty(text)) return null;
        var cleaned = TextFilters.CleanForDisplay(text).Trim();
        return cleaned.Length > 0 ? cleaned : null;
    }

    private static VisualElement FindByName(VisualElement root, string name)
    {
        if (root.name == name) return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var r = FindByName(root.hierarchy[i], name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>The view's dismiss stop, first in the section (the modal-page precedent).</summary>
    internal sealed class CloseAdapter : IAccessibleElement
    {
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => CloseLabel;
        public string GetPrimaryLabel() => CloseLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => CloseLabel + ", " + Role;
        public void Activate() => Close();
    }
}

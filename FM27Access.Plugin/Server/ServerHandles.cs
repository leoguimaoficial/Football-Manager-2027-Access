using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Owns the id -> interactive handle map and everything that acts through it: handle
/// registration during render, action dispatch (/activate, /settext, ...) and ribbon
/// navigation. Ids are derived from element identity (tree path + classification), not a
/// sequence, so the same element keeps the same id across renders and the page can
/// restore screen-reader focus to "the element that was focused" after a DOM patch.
///
/// Written on the main thread during render, read on the main thread during action
/// requests. The dispatcher serialises both, so no locking.
/// </summary>
internal static class ServerHandles
{
    private static readonly Dictionary<string, Handle> _handles = new();
    // Section arrival headings have stable DOM ids, separate from their native
    // text's live-value id. Keep their readonly description in this rendered
    // snapshot, without registering an additional game command.
    private static readonly Dictionary<string, (string Caption, string Screen, IAccessibleElement Source)> _headingDescriptions = new();

    internal static void RegisterHeadingDescription(string id, string caption, IAccessibleElement source)
        => _headingDescriptions[id] = (ServerText.Clean(caption),
            ServerText.ScreenToken(InteractivePanels.Current()), source);

    // A rebuild invalidates every handle the session could act through; the renderer
    // clears the map at the start of each body build.
    internal static void Clear()
    {
        _handles.Clear();
        _headingDescriptions.Clear();
        SpeakToRenderer.ResetQuestions();
    }

    // Whether this id's handle re-realises virtualized content at action time (a recycled
    // table/list row whose controls re-bind across frames). Callers that gate on a resolved
    // control's state use this to retry a transient miss instead of trusting the first read.
    // Main thread only. Unknown ids are not retryable.
    internal static bool DispatchesAcrossFrames(string id)
        => _handles.TryGetValue(id, out var h) && h.DispatchesAcrossFrames;

    internal static PitchMoveSourceHandle ResolvePitchMoveSource(string id)
        => !string.IsNullOrEmpty(id) && _handles.TryGetValue(id, out var handle) ? handle as PitchMoveSourceHandle : null;

    // Keep the exact opening rule even if a later body replaces the handle map.
    // Called/read only on the Unity thread; ordinary dropdowns retain their path.
    internal static Func<string> CaptureConditionValidation(string id)
        => _handles.TryGetValue(id, out var h) && h is ConditionRowHandle condition
            ? condition.ValidateSelectionContext : null;

    internal static Func<string> CaptureConditionValueValidation(string id)
        => _handles.TryGetValue(id, out var h) && h is ConditionRowHandle condition
            ? condition.CaptureValueValidation() : null;

    // Main thread only (called inside Invoke). Resolves a rendered dropdown's handle
    // back to its adapter for DropdownSessionManager.
    internal static Adapters.DropdownAdapter ResolveDropdownHandle(string id, out string error, string expectedContext = null, bool scroll = true)
    {
        if (!_handles.TryGetValue(id, out var h))
        {
            error = "Unknown element. Refresh the screen.";
            return null;
        }
        if (h is ConditionRowHandle condition && condition.ClientContext != expectedContext)
        {
            error = "That search condition has changed. Refresh.";
            return null;
        }
        if (!h.TryValidate(out error))
            return null;
        NativeDestinationTab.RememberHistorySelection();
        Adapters.DropdownAdapter dd;
        bool found = h is CellHandle cell
            ? cell.TryGetDropdownAdapter(out dd, out error, scroll)
            : h.TryGetDropdownAdapter(out dd, out error);
        return found ? dd : null;
    }

    // Main thread only (called inside Invoke). Resolves a rendered date field's handle back
    // to its adapter for DateSelectorSession, mirroring ResolveDropdownHandle.
    internal static Adapters.DateSelectorAdapter ResolveDateHandle(string id, out string error, string expectedContext = null)
    {
        if (!_handles.TryGetValue(id, out var h))
        {
            error = "Unknown element. Refresh the screen.";
            return null;
        }
        if (h is ConditionRowHandle condition && condition.ClientContext != expectedContext)
        {
            error = "That search condition has changed. Refresh.";
            return null;
        }
        if (!h.TryValidate(out error))
            return null;
        return h.TryGetDateAdapter(out var da, out error) ? da : null;
    }

    // Main thread only (called inside Invoke). Resolves a rendered searchable dropdown's handle
    // back to its adapter for SearchDropdownSession, mirroring ResolveDropdownHandle.
    internal static Adapters.SearchableDropdownAdapter ResolveSearchDropdownHandle(string id, out string error)
    {
        if (!_handles.TryGetValue(id, out var h))
        {
            error = "Unknown element. Refresh the screen.";
            return null;
        }
        if (!h.TryValidate(out error))
            return null;
        return h.TryGetSearchDropdownAdapter(out var sd, out error) ? sd : null;
    }

    // Main thread only (called inside Invoke). Resolves a team-instruction select's handle for
    // TeamInstructions.Set, mirroring ResolveDropdownHandle.
    internal static TeamInstructions.TeamInstructionHandle ResolveTeamInstruction(string id, out string error)
    {
        if (!_handles.TryGetValue(id, out var h))
        {
            error = "Unknown element. Refresh the screen.";
            return null;
        }
        if (!h.TryValidate(out error))
            return null;
        if (h is TeamInstructions.TeamInstructionHandle ti) return ti;
        error = "That element is not a team instruction. Refresh the screen.";
        return null;
    }


    // Read-only description query (the Alt+I command). Resolves and validates on the main
    // thread but, unlike Run, does NOT close an open dropdown session: querying the focused
    // element's description must never disturb an interaction in flight.
    internal static string Describe(string id)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                if (!_handles.TryGetValue(id, out var h))
                {
                    if (_headingDescriptions.TryGetValue(id, out var heading))
                    {
                        var changed = ValidateScreen(heading.Screen);
                        if (changed != null) return changed;
                        if (heading.Source == null) return heading.Caption;
                        h = new StopHandle { El = heading.Source };
                    }
                    else if (LiveValues.TryGetSource(id, out var source))
                        h = new StopHandle { El = source };
                    else
                        return "Unknown element. Refresh the screen.";
                }
                if (!h.TryValidate(out var err))
                    return err;
                return h.Describe();
            });
        }
        catch (Exception ex) { return ServerText.Failure("Description unavailable.", ex); }
    }

    // Open the right-click context menu for a rendered handle, then confirm it surfaced. Runs on the
    // request's background thread (like a cross-frame activation) so it can sleep between per-frame
    // Invokes: the send and the confirm must straddle frames, because the game opens the menu a frame or
    // two after the pointer pair — checking in the same frame always sees "not open yet". Phase 1 resolves
    // the handle, realises a cold (scrolled-away) row until its control is laid out, and sends the
    // right-click. Phase 2 polls ContextMenuSurface until the menu appears. "Opened menu" on success;
    // an honest gap ("No context menu here.", "did not open", "Refresh") otherwise.
    private const int CtxRealizeBudgetMs = 1000;  // wait for the handle to resolve and its cold row to
                                                  // lay out: after a menu close the page snapshot that
                                                  // re-registers the page's handles races the player's
                                                  // next open, so an unknown id is retried too
    private const int CtxConfirmBudgetMs = 3000;  // wait for the bound menu to FILL: entries arrive
                                                  // asynchronously (binding callbacks), routinely several
                                                  // hundred ms and past 1.5s on a cold entity
    private const int CtxStepMs = 16;             // ~one frame between attempts

    internal static string OpenContextMenu(string id, string expectedScreen = null)
    {
        return OpenContextMenuLoop(() =>
        {
            var screenError = ValidateScreen(expectedScreen);
            if (screenError != null) return (null, screenError, null);
            // An unknown id is usually transient right after a menu close (the re-register races the
            // next gesture), so it is a RETRY, not a hard error — matched to the realize budget.
            if (!_handles.TryGetValue(id, out var h)) return (null, null, "Unknown element. Refresh the screen.");
            if (!h.TryValidate(out var err)) return (null, err, null);
            return (h.ContextMenuElement(), null, null);
        },
        // Only consulted when the resolved holder has no text of its own.
        () => _handles.TryGetValue(id, out var h) ? h.MenuOwnerLabel() : null,
        () => _handles.TryGetValue(id, out var h) ? h.ContextMenuLinkIndex() : null);
    }

    // Open the game menu on a specific live leaf (a promoted sender cell). Same confirm machinery as
    // the id path, but the element is supplied directly rather than looked up.
    internal static string OpenContextMenuOn(VisualElement leaf) =>
        OpenContextMenuLoop(() => leaf == null
            ? (null, "No context menu here.", null)
            : (leaf, null, null));

    // Standalone Actions buttons use their own native left-click controller.
    // Like right-click menus, their asynchronous result must be observed before
    // reporting success. Empty native action groups must not say "Activated".
    private static string OpenStandaloneMenu(StopHandle handle)
    {
        var result = OpenContextMenuLoop(() =>
        {
            var screenError = ValidateScreen(handle.ActionOwner);
            if (screenError != null) return (null, screenError, null);
            if (!handle.TryValidate(out var error)) return (null, error, null);
            if (handle.El is not Adapters.ContextMenuButtonAdapter menu)
                return (null, "That menu changed. Refresh the screen.", null);
            if (ServerText.IsDisabled(menu)) return (null, "That control is disabled.", null);
            return (menu.BackingElement, null, null);
        }, send: _ =>
        {
            MessageViewSurface.BeforeNativeContentAction(handle.El);
            handle.El.Activate();
        });
        return result == "Context menu did not open."
            ? "That menu is unavailable in the current context."
            : result;
    }

    // The shared open+confirm sequence. `resolve` runs on the main thread each attempt and returns
    // (element, hardError, retryError): a hard error answers immediately, a retry error is kept and
    // returned only if the realize budget expires, and a null element with neither is treated as a
    // transient not-yet-realized row and retried. `ownerFallback` names the menu when the resolved
    // holder carries no text (null for callers with nothing better to offer).
    private static string OpenContextMenuLoop(Func<(VisualElement, string, string)> resolve,
        Func<string> ownerFallback = null, Func<int?> embeddedLinkIndex = null,
        Action<VisualElement> send = null)
    {
        try
        {
            long start = Environment.TickCount64;
            string retryErr = null;   // the honest message if a retryable state never resolves
            while (true)
            {
                var outcome = MainThreadDispatcher.Invoke(() =>
                {
                    // An unknown id is usually transient right after a menu close: the page snapshot
                    // that re-registers the page's handles (same deterministic ids) races the player's
                    // next Shift+F10. Retry within the budget instead of failing the gesture.
                    var (el0, hardErr, retry) = resolve();
                    if (hardErr != null) return (hardErr, false);
                    if (retry != null) { retryErr = retry; return ((string)null, false); }
                    // Null is usually transient on a virtualised row: RealizeControl's ScrollTo lands a
                    // frame or more before the recycled row re-binds its controls, so the first read
                    // after a scroll comes back empty. Retry within the realize budget like the
                    // not-yet-laid-out case, instead of answering "no menu" for a row that has one.
                    var el = el0;
                    if (el == null || el.worldBound.width <= 0f)
                    {
                        retryErr = "That row is no longer available. Refresh.";
                        return ((string)null, false);   // not realized yet — retry
                    }
                    // Engage the loader guard only NOW, at bind time — the menu only becomes
                    // vulnerable to a table sweep once it exists. Guarding the realize/lookup phase
                    // too was a livelock: an open waiting for its id aborted the very page render
                    // that would re-register it. Set inside this main-thread lambda so no sweep op
                    // can interleave between the flag and the bind; cleared in the finally.
                    ContextMenuGuard.OpenInFlight = true;
                    var linkIndex = embeddedLinkIndex?.Invoke();
                    if (send != null) send(el);
                    else if (!Adapters.ContextMenuOpener.Send(el, out var e2, linkIndex)) return (e2, false);
                    // The menu's content is the entity's actions, and the entity's own name is the
                    // holder's link text (the name cell's name, the sender's name) — so title the
                    // menu view by it: the player hears WHOSE menu opened, not a bare "Actions".
                    // Null for a textless holder (a nation flag), which falls back to plain "Actions".
                    // A textless holder (a column header's TemplateContainer) has no entity name to
                    // give, so the handle names the menu instead — the column, for a header menu.
                    var owner = linkIndex.HasValue ? ownerFallback?.Invoke()
                        : ServerText.Clean(Adapters.ContextMenuOpener.Resolve(el).HolderText);
                    if (string.IsNullOrEmpty(owner) && ownerFallback != null) owner = ownerFallback();
                    ContextMenuSurface.OpenedOwnerLabel = owner;
                    return ((string)null, true);
                });
                if (outcome.Item2) break;                                           // sent
                if (outcome.Item1 != null) return outcome.Item1;                    // hard error or "no menu"
                if (Environment.TickCount64 - start >= CtxRealizeBudgetMs)
                    return retryErr ?? "That row is no longer available. Refresh.";
                System.Threading.Thread.Sleep(CtxStepMs);
            }

            long confirm = Environment.TickCount64;
            while (Environment.TickCount64 - confirm < CtxConfirmBudgetMs)
            {
                // Fill first, surface second. Send only binds the menu description; the entries land
                // asynchronously, and showing the popup before they exist puts an entryless shell in
                // front of the player (the "menu opens but has no options" failure, observed live on
                // the squad). So watch the BOUND menu (shown or not) for entries, surface it only
                // then, and confirm success off the shown, filled menu — never off mere presence.
                if (MainThreadDispatcher.Invoke(() =>
                    {
                        var pending = Adapters.ContextMenuOpener.PendingMenu();
                        if (pending == null || !ContextMenuSurface.HasEntries(pending)) return false;
                        Adapters.ContextMenuOpener.EnsureShown();
                        var menu = ContextMenuSurface.TryGetMenu();
                        return menu != null && ContextMenuSurface.HasEntries(menu);
                    }))
                {
                    // The menu actually surfaced (not merely dispatched) — this is the confirmed-open
                    // point. Announce it through Prism so the player hears that the Applications-key /
                    // Shift+F10 gesture worked; the menu appearing does not otherwise produce speech.
                    MainThreadDispatcher.Invoke(() => { SpeechPipeline.SpeakInterrupt("context menu opened"); return true; });
                    return "Opened menu";
                }
                System.Threading.Thread.Sleep(CtxStepMs);
            }
            // The open failed or filled with nothing (nothing was ever shown in that case). Dismiss
            // whatever half-state is bound (Close no-ops when nothing is open) so the next open
            // starts clean and the player is never stranded on an unreadable empty modal.
            MainThreadDispatcher.Invoke(() => { ContextMenuSurface.Close(); return true; });
            return "Context menu did not open.";
        }
        catch (Exception ex) { return ServerText.Failure("Context menu did not open.", ex); }
        finally { ContextMenuGuard.OpenInFlight = false; }
    }

    // Diagnostic: run a table cell handle's realise-and-settle exactly as Activate does, but report
    // what it found instead of acting — the element's rect frame by frame, the list viewport it has
    // to sit inside, and whether a name link's <link> markup has bound. Reports one line per frame
    // so a settle that never converges is visible as movement rather than as a silent no-op.
#if FM27_DEVELOPER_TOOLS
    internal static string ProbeCell(string id, int fireAtFrame)
    {
        try
        {
            if (!_handles.TryGetValue(id, out var h)) return "Unknown element. Refresh the screen.\n";
            if (h is not CellHandle cell) return "That element is not a table cell.\n";
            var sb = new StringBuilder();
            bool scrolled = false;
            int frames = fireAtFrame >= 0 ? fireAtFrame + 1 : 12;
            for (int frame = 0; frame < frames; frame++)
            {
                sb.Append(MainThreadDispatcher.Invoke(() =>
                {
                    var ctx = new Contexts.TableContext(cell.Table, new ClassificationCache());
                    var el = ctx.RealizeCellElement(cell.Row, cell.Col, cell.ElementIndex, scroll: !scrolled);
                    scrolled = true;
                    if (el is not Adapters.PluginAdapterBase a) return $"frame {frame}: no element\n";
                    var r = a.BackingElement.worldBound;
                    var v = ctx.ViewportRect();
                    string clip = r.y < v.y ? " CLIPPED-TOP" : r.yMax > v.yMax ? " CLIPPED-BOTTOM" : "";
                    string link = el is Adapters.TableRowTextAdapter t
                        ? (t.LinkReady ? " link=ready" : " link=NOT-BOUND") : "";
                    string line = $"frame {frame}: \"{ServerText.Clean(el.GetAnnouncement())}\" "
                         + $"rect=({r.x:F0},{r.y:F0},{r.width:F0},{r.height:F0}) "
                         + $"view=({v.x:F0},{v.y:F0},{v.width:F0},{v.height:F0}){clip}{link} "
                         + $"pending={ctx.PendingCellsAt(cell.Row)}";
                    if (frame == fireAtFrame) { el.Activate(); line += " FIRED"; }
                    return line + "\n";
                }));
                System.Threading.Thread.Sleep(16);
            }
            return sb.ToString();
        }
        // Dev diagnostic: the raw exception detail IS the answer here, and no player hears it.
        catch (Exception ex) { return "Error: " + ex + "\n"; }
    }

    // Diagnostic: scroll a fixture-schedule row in by data index and dump its raw realized subtree
    // plus the backing item model's type — for identifying rows whose cells harvest empty. Takes
    // any FixtureRowHandle id from the rendered schedule (its List anchors the probe); ?idx=
    // overrides the target row (defaults to the handle's own row).
    internal static string ProbeFixtureRow(string id, int idx)
    {
        try
        {
            if (!_handles.TryGetValue(id, out var h)) return "Unknown element. Refresh the screen.\n";
            if (h is not FixtureRowHandle fx) return "That element is not a fixture row.\n";
            int target = idx >= 0 ? idx : fx.Row;
            for (int frame = 0; frame < 20; frame++)
            {
                string got = MainThreadDispatcher.Invoke(() =>
                {
                    var ctx = new Contexts.FixtureScheduleContext(fx.List, new List<string>());
                    return ctx.DumpRow(target);
                });
                if (got != null) return $"frame {frame}, row {target}:\n{got}";
                System.Threading.Thread.Sleep(16);
            }
            return $"row {target} never realized\n";
        }
        // Dev diagnostic: the raw exception detail IS the answer here, and no player hears it.
        catch (Exception ex) { return "Error: " + ex + "\n"; }
    }

    // Diagnostic: the context-menu system's live state — whether a menu context is bound to the
    // subsystem's popup, the popup's show state, the bound (possibly hidden) menu's description
    // entries, and the in-tree menu's entries. For chasing partial fills, stale contexts, and
    // unclean closes: poll it around an open/close sequence.
    internal static string DescribeMenuState(int treeDepth = 0)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                var sb = new StringBuilder("[ctxstate]");
                var popup = Adapters.ContextMenuOpener.Popup(out bool contextBound);
                sb.Append("\ncontext: ").Append(contextBound ? "bound" : "unbound");
                if (popup != null)
                {
                    sb.Append("\npopup: wantsToShow=").Append(popup.WantsToShow)
                      .Append(" isVisible=").Append(popup.IsVisible);
                    var pending = popup.Menu;
                    sb.Append("\npending: ").Append(pending == null
                        ? "no menu element"
                        : ContextMenuSurface.DescribeEntries(pending));
                }
                var inTree = ContextMenuSurface.TryGetMenu();
                sb.Append("\ninTree: ").Append(inTree == null
                    ? "none"
                    : ContextMenuSurface.DescribeEntries(inTree));
                if (treeDepth > 0 && inTree != null)
                    sb.Append("\ntree:\n").Append(ContextMenuSurface.DescribeTree(inTree, treeDepth));
                return sb.ToString();
            });
        }
        // Dev diagnostic: the raw exception detail IS the answer here, and no player hears it.
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    // Diagnostic: walk a rendered handle's ancestry and report any ancestor carrying a (non-null)
    // ContextMenuDescriptionManipulator field — the universal right-click TRIGGER, held by
    // PluginContextMenuBindings, FMCalendarEvent, FMPitchIndicator, and SIText alike. Tells us whether
    // manipulator-presence is a clean menu marker (set only when a menu is armed) or too noisy (set on
    // every SIText). Reflects each ancestor's own + inherited private fields.
    // What the resolver actually picked for this element — the one holder out of the subtree listing
    // below that answers for it. Without this the dump shows candidates with no indication of which
    // one a Shift+F10 would reach. The owner's rendered label (and so the scope verdict) is not
    // available from a raw handle; /ctxcensus pairs the two.
    private static void AppendResolution(StringBuilder sb, VisualElement el)
    {
        var r = Adapters.ContextMenuOpener.Resolve(el);
        if (r.Target == null) { sb.Append("resolved=none"); return; }
        sb.Append("resolved=").Append(r.Kind).Append(' ').Append(r.HolderType)
          .Append(" depth=").Append(r.Depth).Append(" holders=").Append(r.HolderCount);
        if (!string.IsNullOrEmpty(r.HolderText))
            sb.Append(" text=\"").Append(Speech.TextFilters.CleanForDisplay(r.HolderText)).Append('"');
    }

    internal static string DescribeMenuTrigger(string id)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                if (!_handles.TryGetValue(id, out var h)) return "Unknown element. Refresh the screen.";
                var el = h.ContextMenuElement();
                if (el == null) return "Handle has no backing VisualElement.";

                var flags = Il2CppSystem.Reflection.BindingFlags.Instance
                    | Il2CppSystem.Reflection.BindingFlags.Public
                    | Il2CppSystem.Reflection.BindingFlags.NonPublic
                    | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly;

                // Enumerate EVERY element in the tile subtree that carries a ContextMenuDescriptionManipulator
                // field (SIText, PluginContextMenuBindings, FMCalendarEvent, ...), reporting whether it is
                // armed (SET) vs present-but-null, plus the SIText's text. This shows which sub-element
                // (opponent / competition / venue) actually has a menu and confirms the field is a clean
                // per-leaf marker (null on non-menu texts).
                //
                // Each holder also reports its DEPTH below the queried element, which is what separates a
                // control's own binding (a level or two in) from a composite tile's buried sub-block
                // binding belonging to another entity. The header line reports what the resolver actually
                // picked and how ContextMenuScope judges that pairing.
                var sb = new StringBuilder("[menu-trigger] ");
                AppendResolution(sb, el);
                sb.Append("\nsubtree:");
                var stack = new Stack<(VisualElement El, int Depth)>();
                stack.Push((el, 0));
                int seen = 0;
                while (stack.Count > 0 && seen < 800)
                {
                    var (e, depth) = stack.Pop();
                    seen++;
                    string found = null;
                    var t = e.GetIl2CppType();
                    while (t != null && found == null)
                    {
                        foreach (var f in t.GetFields(flags))
                        {
                            if (f.FieldType.Name != "ContextMenuDescriptionManipulator") continue;
                            try { found = f.GetValue(e) != null ? "SET" : "null"; }
                            catch (Exception ex) { found = "err:" + ex.GetType().Name; }
                            break;
                        }
                        t = t.BaseType;
                    }
                    if (found != null)
                    {
                        string typeName = e.GetIl2CppType().Name;
                        sb.Append('\n').Append("d").Append(depth).Append(' ').Append(typeName)
                          .Append(string.IsNullOrEmpty(e.name) ? "" : "'" + e.name + "'")
                          .Append(" manip=").Append(found);
                        if (typeName == "SIText")
                        {
                            var tx = e.Cast<TextElement>().text;
                            if (!string.IsNullOrEmpty(tx)) sb.Append(" text=\"").Append(tx.Length > 30 ? tx.Substring(0, 30) : tx).Append('"');
                            ProbeLinkActions(sb, e);
                        }
                    }
                    for (int i = e.hierarchy.childCount - 1; i >= 0; i--) stack.Push((e.hierarchy[i], depth + 1));
                }
                return sb.ToString();
            });
        }
        // Dev diagnostic: the raw exception detail IS the answer here, and no player hears it.
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    // Diagnostic-only: the game's own per-link "would this menu have content" signals on an SIText
    // menu leaf — EnableEmbeddedLinks / DisableContextMenu, the embedded-data record count, and
    // LinkHasActions(id) per link id (private on the game type; the IL2CPP proxy exposes it).
    // Answers whether LinkHasActions cleanly separates real menus (team, competition) from
    // armed-manipulator-but-empty-menu false positives (fixture scores).
    private static void ProbeLinkActions(StringBuilder sb, VisualElement e)
    {
        try
        {
            var st = e.Cast<SI.Bindable.SIText>();
            sb.Append(" links=").Append(st.EnableEmbeddedLinks ? "on" : "off");
            if (st.DisableContextMenu) sb.Append(" ctxDisabled");
            var data = st.EmbeddedData;
            int n = data == null ? 0 : data.Count;
            sb.Append(" embedded=").Append(n);
            for (int i = 0; i < n && i < 4; i++)
                sb.Append(" hasActions[").Append(i).Append("]=").Append(st.LinkHasActions(i));
        }
        catch (Exception ex) { sb.Append(" probeErr=").Append(ex.GetType().Name).Append(':').Append(ex.Message); }
    }

    // Diagnostic: walk the FULL ancestry (to the panel root) of a rendered handle's backing element,
    // marking any PluginContextMenuBindings and its depth. Answers "does the context-menu walk-up just
    // need a deeper bound, or is there no binding above this element at all" without scanning the panel.
    internal static string DescribeAncestry(string id)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                if (!_handles.TryGetValue(id, out var h))
                    return "Unknown element. Refresh the screen.";
                var el = h.ContextMenuElement();
                if (el == null) return "Handle has no backing VisualElement.";

                var sb = new StringBuilder("[ancestry] ");
                var cur = el;
                int depth = 0;
                while (cur != null)
                {
                    string type = cur.GetIl2CppType().Name;
                    sb.Append('\n').Append(depth).Append(": ").Append(type);
                    if (!string.IsNullOrEmpty(cur.name)) sb.Append('\'').Append(cur.name).Append('\'');
                    if (type == "PluginContextMenuBindings") sb.Append("   <-- BINDINGS at depth ").Append(depth);
                    cur = cur.hierarchy.parent;
                    depth++;
                }

                // Subtree (descendants): where a binding nested between the element and its leaves sits,
                // and which SIText leaves carry text — to see whether the menu-bearing leaf is the one
                // ClickTarget (deepest SIText) would pick.
                sb.Append("\n[subtree]");
                var stack = new System.Collections.Generic.Stack<(VisualElement E, int D)>();
                stack.Push((el, 0));
                int seen = 0;
                while (stack.Count > 0 && seen < 400)
                {
                    var (e, d) = stack.Pop();
                    seen++;
                    string type = e.GetIl2CppType().Name;
                    bool isBindings = type == "PluginContextMenuBindings";
                    bool isText = type == "SIText";
                    if (isBindings || isText)
                    {
                        sb.Append('\n').Append(new string(' ', d)).Append(d).Append(": ").Append(type);
                        if (!string.IsNullOrEmpty(e.name)) sb.Append('\'').Append(e.name).Append('\'');
                        if (isText) { var t = e.Cast<TextElement>().text; if (!string.IsNullOrEmpty(t)) sb.Append(" = \"").Append(t).Append('"'); }
                        if (isBindings) sb.Append("   <-- BINDINGS");
                    }
                    for (int i = e.hierarchy.childCount - 1; i >= 0; i--)
                        stack.Push((e.hierarchy[i], d + 1));
                }
                return sb.ToString();
            });
        }
        // Dev diagnostic: the raw exception detail IS the answer here, and no player hears it.
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    // Speak arbitrary text through the in-game Prism TTS. This is the companion app's announcement
    // channel (see app.js FM.core.say): on-demand reads (Alt+I, search counts, calendar feedback)
    // and errors are spoken here instead of through an aria-live region. Marshaled to the main
    // thread to reach the speech backend the way the rest of the mod does. The empty return is
    // ignored by the client.
#endif

    internal static string Speak(string text)
    {
        MainThreadDispatcher.Invoke(() => { SpeechPipeline.SpeakInterrupt(text); return true; });
        return "";
    }

    // The native money/value command updates its caption on a following frame.
    // Preserve FM26's ComboboxHandler availability checks and value observation,
    // rather than speaking the old caption immediately after SimulateButtonClick.
    internal static string Step(string id, int direction, string expectedScreen = null, string expectedContext = null)
    {
        if (direction != -1 && direction != 1) return "Invalid step direction.";
        try
        {
            StopHandle target = null;
            Adapters.ComboboxAdapter combo = null;
            string before = null, owner = null;
            bool sent = false;
            var result = Run(id, h =>
            {
                if (h is ConditionRowHandle) return h.Step(direction);
                if (h is not StopHandle stop || stop.El is not Adapters.ComboboxAdapter value) return "Not a stepper.";
                if (ServerText.IsDisabled(value)) return "That control is disabled.";
                if (direction > 0 ? !value.IsIncrementEnabled() : !value.IsDecrementEnabled())
                    return value.GetCurrentValue() + (direction > 0 ? ", maximum" : ", minimum");
                before = value.GetCurrentValue();
                if (string.IsNullOrEmpty(before)) return "That value could not be read. Refresh before trying again.";
                target = stop;
                combo = value;
                owner = expectedScreen ?? ServerText.ScreenToken(InteractivePanels.Current());
                if (direction > 0) value.Increment(); else value.Decrement();
                sent = true;
                return null;
            }, expectedScreen, expectedContext);
            if (!sent) return result;

            long until = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < until)
            {
                result = MainThreadDispatcher.Invoke(() =>
                {
                    var error = ValidateScreen(owner);
                    if (error != null) return error;
                    if (!target.TryValidate(out error)) return error;
                    string current = combo.GetCurrentValue();
                    return !string.IsNullOrEmpty(current) && current != before ? current : null;
                });
                if (result != null) return result;
                System.Threading.Thread.Sleep(16);
            }
            return "Adjustment sent, but the updated value could not be confirmed. Refresh before trying again.";
        }
        catch (Exception ex) { return ServerText.Failure("The adjusted value could not be confirmed. Refresh before trying again.", ex); }
    }

    internal static string Run(string id, Func<Handle, string> action, string expectedScreen = null, string expectedContext = null)
    {
#if FM27_DEVELOPER_TOOLS
        var handleTiming = System.Diagnostics.Stopwatch.StartNew();
#endif
        try
        {
            // Resolve, close any open dropdown, and validate on the main thread. A cross-frame
            // handle stops here and runs its action on this (background) thread instead, so it can
            // sleep between the per-frame Invokes its re-bind wait needs; everything else acts in
            // this one Invoke as before.
            Handle deferred = null;
            var result = MainThreadDispatcher.Invoke(() =>
            {
                var screenError = ValidateScreen(expectedScreen);
                if (screenError != null) return screenError;
                if (!_handles.TryGetValue(id, out var h))
                    return "Unknown element. Refresh the screen.";
                if (h is ConditionRowHandle condition && condition.ClientContext != expectedContext)
                    return "That search condition has changed. Refresh.";
                if (!h.TryValidate(out var err))
                    return err;
#if FM27_DEVELOPER_TOOLS
                bool messageTiming = h is StopHandle messageHandle && messageHandle.El is Adapters.MessageButtonAdapter;
                if (messageTiming) Log.Debug($"[MessageTiming] handleValidated={handleTiming.ElapsedMilliseconds}ms");
#endif
                NativeDestinationTab.RememberHistorySelection();
#if FM27_DEVELOPER_TOOLS
                if (messageTiming) Log.Debug($"[MessageTiming] historyRemembered={handleTiming.ElapsedMilliseconds}ms");
#endif
                DropdownSessionManager.CloseCurrentOnMainThread();
                if (h.DispatchesAcrossFrames) { h.ActionOwner = expectedScreen; deferred = h; return null; }
                return action(h);
            });
            return deferred != null ? action(deferred) : result;
        }
        catch (Exception ex) { return ServerText.Failure("That action failed.", ex); }
    }

    // Main thread, immediately before resolving/acting on a rendered handle.
    internal static string ValidateScreen(string expected)
        => !string.IsNullOrEmpty(expected)
            && !ActionScreen.Matches(expected, ServerText.ScreenToken(InteractivePanels.Current()))
                ? "This screen changed. Refresh before trying again." : null;

    // --- Navigation (main thread) ---

    // Friendly /nav target -> the ribbon button's TemplateContainer name in OverviewTitleBar.
    // The category SIButtons all share the generic name "buttons-button-navigation-default",
    // so the unique handle is the parent container name.
    private static readonly Dictionary<string, string> RibbonTargets = new()
    {
        ["portal"] = "PortalButton",
        ["squad"] = "SquadButton",
        ["recruitment"] = "RecruitmentButton",
        ["matchday"] = "ClubMatchDayButton",
        ["club"] = "ClubButton",
        ["career"] = "CareerButton",
        ["tactics"] = "ClubTacticsButton",
        ["training"] = "TrainingButton",
        ["calendar"] = "CurrentDayWidget",
        ["pausemenu"] = "FMButton",
    };

    // Ribbon-category jump. HTML panel cycling (the old dir=next|prev branch) is retired in Phase 4:
    // every interactive panel is on the page, reached by heading. Speech-side cycling keeps its own
    // path (DefaultScreenHandler → NavigationManager.CyclePanelForward/Backward), so those methods stay.
    internal static string Nav(string target)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                DropdownSessionManager.CloseCurrentOnMainThread();
                BookmarkEditorSurface.CloseIfOpen();
                SoundCuesSurface.CloseIfOpen();
                NavigatorSurface.CloseIfOpen();
                FilterPopupSurface.CloseIfOpen();
                MessageViewSurface.CloseIfOpen();
                if (!string.IsNullOrEmpty(target))
                    return ActivateRibbonButton(target);
                return "Nothing to navigate.";
            });
        }
        catch (Exception ex) { return ServerText.Failure("Navigation failed.", ex); }
    }

    // Screen-history back/forward. The game's own stack is walked by clicking its Back/Forward button
    // through the same delegates the speech surface uses, but an open full-screen Message view is a
    // mod-owned entry sitting above its inbox in that stack, so a press can also step into or out of a
    // message without moving the game (see MessageViewHistory and docs/message-view-history-plan.md).
    //
    // The reply is empty on success — the screen change speaks for itself — and a short speakable
    // string when the press could not do what it means ("back not available", "Message unavailable.").
    // The companion speaks a non-empty reply; it is not routed through Prism TTS as
    // NavigationManager.NavigateBack/Forward would be.
    internal static string History(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir))
                return MainThreadDispatcher.Invoke(HistoryState);
            if (dir != "back" && dir != "forward")
                return "Unknown direction.";

            // One deadline for the whole press, and one generation. Together they bound the two ways a
            // press could otherwise misbehave: waiting out several stacked budgets (a press must never
            // hang for the sum of them), and an older press finishing its restore after a newer press
            // has moved the player somewhere else.
            long deadline = Environment.TickCount64 + PressBudgetMs;
            int generation = System.Threading.Interlocked.Increment(ref _historyGeneration);
            Func<bool> superseded = () => System.Threading.Volatile.Read(ref _historyGeneration) != generation;

            var plan = MainThreadDispatcher.Invoke(() => PlanHistoryMove(dir));
            if (plan.Replaced != null) return plan.Replaced.Wait(deadline, superseded);
            switch (plan.Outcome)
            {
                case HistoryOutcome.RestoreNow:
                    return MessageViewSurface.Restore(plan.Index, plan.Subject, deadline, superseded);
                case HistoryOutcome.AwaitBack:
                    return AwaitHistoryLanding(plan.Index, deadline, superseded, restoreMessage: true);
                case HistoryOutcome.AwaitForward:
                    return AwaitHistoryLanding(plan.Index, deadline, superseded, restoreMessage: false);
                case HistoryOutcome.AwaitForegroundClose:
                    return AwaitForegroundClose(plan.ClosingPanel, deadline, superseded);
                default:
                    return plan.Reply;
            }
        }
        catch (Exception ex)
        {
            // The press did not do what it means and the reason is not something to read out (a
            // dispatch timeout while the game is busy, a destroyed element). Log the detail, speak a
            // short truth. Never silence: the player cannot see that nothing happened.
            Speech.Degrade.Note($"history {dir} failed: {ex.GetType().Name}: {ex.Message}");
            return "Navigation failed.";
        }
    }

    // How long one Back or Forward press may spend before it gives up, however many phases it passes
    // through. Generous enough for the game's own transition plus a panel rebuild and a streaming list,
    // short enough that a press which cannot finish still answers while the player is waiting on it.
    private const int PressBudgetMs = 5000;

    // Bumped by every press; a restore still running when it changes abandons itself.
    private static int _historyGeneration;

    private enum HistoryOutcome
    {
        /// <summary>Nothing left to do; Reply is the answer.</summary>
        Done,
        /// <summary>Step up into the message recorded at Index. No game move happened.</summary>
        RestoreNow,
        /// <summary>The game's back is in flight; wait for the landing and restore if it has an entry.</summary>
        AwaitBack,
        /// <summary>The game's forward is in flight; restore its observed native tab only.</summary>
        AwaitForward,
        /// <summary>A native overlay is closing without traversing menu history.</summary>
        AwaitForegroundClose,
    }

    private readonly struct HistoryPlan
    {
        public HistoryPlan(HistoryOutcome outcome, string reply, int index, string subject,
            ReplacedMessageHistory.MoveRequest replaced = null, SI.Bindable.Panel closingPanel = null)
        {
            Outcome = outcome;
            Reply = reply;
            Index = index;
            Subject = subject;
            Replaced = replaced;
            ClosingPanel = closingPanel;
        }

        public HistoryOutcome Outcome { get; }
        public string Reply { get; }
        public int Index { get; }
        public string Subject { get; }
        public ReplacedMessageHistory.MoveRequest Replaced { get; }
        public SI.Bindable.Panel ClosingPanel { get; }

        public static HistoryPlan Done(string reply) => new HistoryPlan(HistoryOutcome.Done, reply ?? "", 0, null);
    }

    /// <summary>
    /// Decide what this Back or Forward press means and take every step that belongs on the main
    /// thread. An open Message view is an entry in the merged history (see MessageViewHistory), so a
    /// press can step between the message and its inbox without moving the game at all. Anything that
    /// has to wait — the game's transition, or a reading pane that has to be re-read — is handed back
    /// to the request thread rather than blocking a frame. Main thread.
    /// </summary>
    private static HistoryPlan PlanHistoryMove(string dir)
    {
        NativeDestinationTab.RememberHistorySelection();
        DropdownSessionManager.CloseCurrentOnMainThread();
        BookmarkEditorSurface.CloseIfOpen();
        SoundCuesSurface.CloseIfOpen();
        NavigatorSurface.CloseIfOpen();
        FilterPopupSurface.CloseIfOpen();

        // Toggle-owned overlays do not move the game's menu-history index.
        // Close through their actual owner and finish here, rather than waiting
        // for an unrelated history landing after the native panel has closed.
        if (dir == "back" && SI.Bindable.PanelManager.Exists()
            && FM27PanelCatalog.TryCloseToggleOwnedForeground(SI.Bindable.PanelManager.Instance))
            return HistoryPlan.Done("");

        // The open flag self-corrects only when the page is next rendered, so a press arriving right
        // after an attachment opened its card would otherwise be spent closing a message the player
        // has already left. Settle it against the live screen first, then read it.
        MessageViewSurface.CloseIfStale();
        bool modalOpen = MessageViewSurface.IsOpen();
        var live = GameHistory.TryRead();
        if (live == null)
        {
            // No anchor to pin an entry to: behave exactly as before the feature existed.
            MessageViewSurface.CloseIfOpen();
            return HistoryPlan.Done(GameMove(dir));
        }

        var h = live.Value;
        var replacement = ReplacedMessageHistory.Begin(dir == "back");
        if (replacement != null)
            return new HistoryPlan(HistoryOutcome.Done, null, h.Index, null, replacement);
        var history = MessageViewHistory.Current;
        history.Observe(h.Index, h.Count, h.Shape);

        var step = dir == "back"
            ? history.Back(h.Index, modalOpen)
            : history.Forward(h.Index, modalOpen);

        if (step == MessageViewHistory.Step.CloseModal)
        {
            MessageViewSurface.CloseIfOpen();
            history.NoteSteppedToInbox(h.Index);
            return HistoryPlan.Done("");
        }

        if (step == MessageViewHistory.Step.RestoreModal)
        {
            var slot = history.SlotAt(h.Index);
            if (slot != null)
                return new HistoryPlan(HistoryOutcome.RestoreNow, null, h.Index, slot.Value.Subject);
        }

        MessageViewSurface.CloseIfOpen();
        ReplacedMessageHistory.LeaveForNativeMove(dir);
        // The native Back delegate dismisses covering cards before touching
        // the covered title bar. Such a close (global search is one example)
        // does not create/traverse a menu-history entry. Capture its exact
        // foreground owner in this same dispatch, before sending Back once.
        var foreground = dir == "back" ? FM27PanelCatalog.GetForegroundPanel() : null;
        var overlay = foreground != null && InteractivePanels.CurrentOrNull()?.Any(view =>
            view.IsOverlay && view.Panel.Pointer == foreground.Pointer) == true ? foreground : null;
        var error = GameMove(dir);
        if (error != null) return HistoryPlan.Done(error);
        if (overlay != null)
            return new HistoryPlan(HistoryOutcome.AwaitForegroundClose, "", h.Index, null, closingPanel: overlay);
        // Only a back can land on a position whose message entry sits above it; a forward enters from
        // below, onto the inbox.
        return new HistoryPlan(dir == "back" ? HistoryOutcome.AwaitBack : HistoryOutcome.AwaitForward,
            "", h.Index, null);
    }

    // Null on success, or the game's short reason ("back not available").
    private static string GameMove(string dir)
        => dir == "back" ? NavigationManager.RequestNavigateBack() : NavigationManager.RequestNavigateForward();

    // The game's back runs through its own delay and panel transition, so the landing position is
    // polled rather than assumed — it is also what says whether the screen we arrived at has a message
    // entry to step into. A landing that never happens leaves the player wherever they are, silently:
    // the screen speaks for itself. The restore inherits what is left of the press's deadline, so
    // waiting for the landing can never spend time the restore then spends again.
    private static string AwaitHistoryLanding(int fromIndex, long deadline, Func<bool> superseded, bool restoreMessage)
    {
        while (true)
        {
            var landing = MainThreadDispatcher.Invoke(() => ReadHistoryLanding(fromIndex, restoreMessage));
            if (landing.Landed)
            {
                var replacement = MainThreadDispatcher.Invoke(() => ReplacedMessageHistory.Land(landing.Index, restoreMessage));
                if (replacement != null) return replacement.Wait(deadline, superseded);
                NativeDestinationTab.RestoreHistorySelection(landing.Index, deadline, superseded);
                return landing.Subject == null
                    ? ""
                    : MessageViewSurface.Restore(landing.Index, landing.Subject, deadline, superseded);
            }
            if (superseded()) return "";
            if (Environment.TickCount64 >= deadline)
            {
                Speech.Degrade.Note($"history move never left index {fromIndex} within budget");
                return "Navigation did not reach another history entry in time.";
            }
            System.Threading.Thread.Sleep(LandingStepMs);
        }
    }

    private const int LandingStepMs = 60;

    private static string AwaitForegroundClose(SI.Bindable.Panel panel, long deadline, Func<bool> superseded)
    {
        while (true)
        {
            // Hold the actual native owner for this operation. A newly opened
            // card must never be dismissed or mistaken for the closing one.
            bool closed = MainThreadDispatcher.Invoke(() => !FM27PanelCatalog.GetNativeLayers(SI.Bindable.PanelManager.Instance)
                .Any(layer => layer.Open.Any(current => current.Pointer == panel.Pointer)
                    || layer.Closing.Any(current => current.Pointer == panel.Pointer)));
            if (closed || superseded()) return "";
            if (Environment.TickCount64 >= deadline)
                return "The panel did not close in time.";
            System.Threading.Thread.Sleep(LandingStepMs);
        }
    }

    private readonly struct BackLanding
    {
        public BackLanding(bool landed, int index, string subject)
        {
            Landed = landed;
            Index = index;
            Subject = subject;
        }

        public bool Landed { get; }
        public int Index { get; }

        /// <summary>The message to step into at the landing position, or null for none.</summary>
        public string Subject { get; }
    }

    private static BackLanding ReadHistoryLanding(int fromIndex, bool restoreMessage)
    {
        var live = GameHistory.TryRead();
        if (live == null) return new BackLanding(false, 0, null);
        var h = live.Value;
        if (h.Index == fromIndex) return new BackLanding(false, 0, null);
        // The native index changes before its asynchronous panel transition.
        // Do not restore an Inbox over the preceding native navigation command.
        if (!GameHistory.IsAtForeground(h.Index)) return new BackLanding(false, 0, null);
        var slot = restoreMessage ? MessageViewHistory.Current.RestoreAfterBack(h.Index, h.Count, h.Shape) : null;
        return new BackLanding(true, h.Index, slot?.Subject);
    }

    // GET /history with no dir: the dev-driver state dump for the virtual-history work — where the
    // game's own stack stands and whether the mod's modal Message view is open over it. Read-only,
    // and it deliberately does none of History's modal-closing hygiene, so polling it never changes
    // what the player is looking at. Main thread (the caller marshals).
    private static string HistoryState()
    {
        var live = GameHistory.TryRead();
        if (live == null)
            return "history unreadable";
        MessageViewSurface.ObserveHistory();
        var h = live.Value;
        // Slots print as index="subject", with * marking one the player was still inside when they
        // left that stack position.
        return $"index={h.Index} count={h.Count} canBack={h.CanBack} canForward={h.CanForward} "
             + $"msgview={(MessageViewSurface.IsOpen() ? "open" : "closed")} shape=[{h.Shape}] "
             + $"slots={MessageViewHistory.Current.Describe()}";
    }

    // The companion's Ctrl-chord bookmark open: slot N (1-based, up to 12; 0/minus/equals are
    // slots 10-12 client-side) from the MOD bookmark store, navigated through the Navigator's
    // live-resolved dispatch — not the game's title-bar strip, whose system the mod no longer
    // remaps. The reply is feedback only on failure — "Opening X" is suppressed client-side
    // because the screen change speaks for itself (matching /nav). Go marshals to the main
    // thread itself and does the modal-surface hygiene closes, so no Invoke wrapper here.
    internal static string Bookmark(string slotStr)
    {
        try
        {
            if (!int.TryParse(slotStr, out int slot) || slot < 1 || slot > ModBookmarks.MaxSlots)
                return "Unknown bookmark slot.";

            var (entry, count) = ModBookmarks.Slot(slot);
            if (entry == null)
                return count == 0
                    ? "No bookmarks set. Use the Edit bookmarks button."
                    : $"No bookmark {slot}, only {count} set.";

            return NavigatorIndex.Go(entry.Uid.ToString(), entry.Obj.ToString());
        }
        catch (Exception ex) { return ServerText.Failure("Bookmark failed.", ex); }
    }

    // The live ribbon button's own label for a /nav target ("Portal", "Match Day", ...), or null
    // when the button isn't on screen. The Navigator titles its category entries with this — the
    // game's PanelID.Title is not the ribbon caption (PortalScreen's Title is "Overview") — so the
    // caption stays the game's localized text, never mod-authored. Main thread only.
    internal static string RibbonButtonLabel(string target)
    {
        if (!RibbonTargets.TryGetValue(target, out var containerName)) return null;
        var container = FindRibbonContainer(containerName);
        if (container == null) return null;
        var button = FindFirstByTypeName(container, "SIButton");
        if (button == null) return null;
        return ElementBridge.CreateAdapter(button, ElementClassification.Button).GetPrimaryLabel();
    }

    // The ribbon target's container, resolved unambiguously. "ClubButton" names three different
    // things on a busy screen: the ribbon entry (a TemplateContainer in the title bar's MenuBar),
    // the title bar's club crest (an SIButton), and every fixture row's club crest on the portal
    // Matches screen (SIButtons, in a panel iterated before the title bar). So: only inside the
    // OverviewTitleBar panel, and never an element that is itself an SIButton — the ribbon entry
    // is the container around the button, not a button. Null when the title bar isn't on screen
    // (main menu, match HUD), where ribbon targets are legitimately unavailable.
    private static VisualElement FindRibbonContainer(string containerName)
    {
        if (NavigationManager.GetAllOpenPanels() is not { } openPanels) return null;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            if (panel.name != "OverviewTitleBar") continue;
            return FindContainerByName(panel, containerName)
                ?? (containerName == "FMButton" ? FindContainerByName(panel, "FMMenuDropdown") : null);
        }
        return null;
    }

    private static VisualElement FindContainerByName(VisualElement root, string name)
    {
        if (root.name == name && root.GetIl2CppType().Name != "SIButton") return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindContainerByName(root.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    // Also the category tier of NavigatorIndex.Go: main tabs navigate via this proven click path,
    // never via OpenSubtabEvent (dispatching a main-tab PanelID as a subtab hard-crashed the game).
    internal static string ActivateRibbonButton(string target)
    {
        NativeDestinationTab.RememberHistorySelection();
        if (!RibbonTargets.TryGetValue(target.ToLowerInvariant(), out var containerName))
            return "Unknown navigation target.";

        // Same scoped resolve as RibbonButtonLabel: the old any-panel name scan could land on a
        // fixture row's club crest (also named ClubButton) and click the wrong thing entirely.
        var container = FindRibbonContainer(containerName);
        if (container == null) return $"{target} not available on this screen.";

        var button = FindFirstByTypeName(container, "SIButton");
        if (button == null)
        {
            // FM27's FM menu is now a native SIDropdown. Hand its existing HTML
            // control back to the original client/session machinery, not a second menu.
            var dropdown = FindFirstByTypeName(container, "SIDropdown");
            if (dropdown == null) return $"Found {containerName} but no button inside.";
            var dd = new Adapters.DropdownAdapter(dropdown);
            if (dd.IsDisabled()) return "This control is not available right now.";
            // Reuse the id already rendered in the page. Allocating the same identity
            // twice intentionally produces a suffixed id, which has no DOM control.
            foreach (var pair in _handles)
                if (pair.Value is StopHandle stop && stop.El is Adapters.DropdownAdapter rendered
                    && rendered.BackingElement.Pointer == dropdown.Pointer)
                    return "dropdown:" + pair.Key;
            return "The menu is not on the current page. Refresh the screen.";
        }

        var adapter = ElementBridge.CreateAdapter(button, ElementClassification.Button);
        adapter.Activate();
        return "Opening " + target;
    }

    private static VisualElement FindFirstByTypeName(VisualElement root, string typeName)
    {
        if (root.GetIl2CppType().Name == typeName) return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindFirstByTypeName(root.hierarchy[i], typeName);
            if (found != null) return found;
        }
        return null;
    }

    // --- Handle registration ---

    // Read-only correlation for native disclosure metadata. Allocating another
    // id for an already-rendered control would create a collision suffix, not
    // that control's actual DOM id. Multiple aliases are deliberately refused.
    internal static string RenderedNativeId(VisualElement element)
    {
        string found = null;
        foreach (var pair in _handles)
            if (pair.Value is StopHandle stop && stop.El is Adapters.PluginAdapterBase adapter
                && adapter.BackingElement.Pointer == element.Pointer)
            {
                if (found != null) return null;
                found = pair.Key;
            }
        return found;
    }

    internal static string Register(Handle h, IAccessibleElement el)
    {
        if (h is StopHandle calendarHandle)
            calendarHandle.CalendarIdentity = CalendarCommandIdentity.ReadWithReference(NativeElement(el), out calendarHandle.CalendarWeekdayReference);
        if (h is StopHandle messageHandle && el is Adapters.MessageButtonAdapter message)
            messageHandle.MessageId = message.MessageId;
        if(h is StopHandle answerHandle&&el is Adapters.ButtonAdapter answer)
            answerHandle.ConversationContext=SpeakToRenderer.AnswerContext(answer.BackingElement);
        // The title bar's native buttons must use the same merged message
        // history as Alt+[ / Alt+]. A direct SIButton click returns to the inbox
        // but skips restoration of the message above that native history slot.
        // Capture direction while rendering on Unity's thread; activation then
        // runs the existing cross-frame History path on the request thread.
        if (h is StopHandle stop && el is Adapters.ButtonAdapter button
            && ElementBridge.IsInPanel(button.BackingElement, "OverviewTitleBar"))
            stop.HistoryDirection = button.BackingElement.name switch
            {
                "BackButton" => "back",
                "ForwardButton" => "forward",
                _ => null,
            };
        return RegisterKey(h, KeyFor(el));
    }

    // The stable id key for an element: its identity path for game-backed adapters,
    // otherwise role + label. Also used by LiveValues for read-only elements so both id
    // sources agree. A synthetic adapter with its own identity (IStableKeyElement — the
    // bookmark editor's rows) overrides this so its DOM id follows the bookmark, not the
    // label (which carries the mutable slot number): an edit then changes exactly that
    // node rather than text-shifting every surviving row past NVDA's browse buffer.
    internal static string KeyFor(IAccessibleElement el)
    {
        if (el is IStableKeyElement s && s.StableKey is { } k) return WithCalendarIdentity(k, el);
        if (el is Adapters.MessageButtonAdapter message && message.MessageId is int messageId)
            return IdentityPath(NativeMessageIdentity.ListOwner(message.BackingElement)) + "|native-message:" + messageId;
        if(el is Adapters.PluginAdapterBase a)
        {
            var key=IdentityPath(a.BackingElement);
            var context=el is Adapters.ButtonAdapter?SpeakToRenderer.AnswerContext(a.BackingElement):null;
            return WithCalendarIdentity(context==null?key:key+"|conversation-answer:"+context, el);
        }
        return ServerText.SafeRole(el) + "|" + ServerText.Label(el);
    }

    private static VisualElement NativeElement(IAccessibleElement el)
        => el is Adapters.PluginAdapterBase adapter ? adapter.BackingElement
            : el is FilterPopupSurface.TriggerAdapter trigger ? trigger.Widget : null;

    private static string WithCalendarIdentity(string key, IAccessibleElement el)
    {
        var identity = CalendarCommandIdentity.Read(NativeElement(el));
        return identity == null ? key : key + "|" + identity;
    }

    internal static string RegisterKey(Handle h, string key)
    {
        // The id comes from the screen's shared pool (LiveValues) so handle ids and
        // read-only value ids can never collide.
        var id = LiveValues.AllocateId(key);
        _handles[id] = h;
        return id;
    }

    // Element identity as a root-to-element path of names (or type names) with sibling
    // disambiguation, so the same element yields the same id on every render. The label
    // is deliberately excluded: a value change must not change the element's identity,
    // or focus restore would lose the element the user is sitting on.
    internal static string IdentityPath(VisualElement el)
    {
        // Collected leaf-to-root and appended in reverse: sb.Insert(0, ...) per level is O(len)
        // character moves each, which made deep paths quadratic in depth.
        var parts = new List<string>();
        var node = el;
        while (node != null)
        {
            var parent = node.hierarchy.parent;
            string name = node.name;
            if (string.IsNullOrEmpty(name)) name = node.GetIl2CppType().Name;
            int index = 0;
            if (parent != null)
            {
                int n = parent.hierarchy.childCount;
                for (int i = 0; i < n; i++)
                {
                    var sib = parent.hierarchy[i];
                    // Pointer, not ReferenceEquals: two IL2CPP wrappers around the SAME native
                    // element are different managed objects whenever the interop cache has dropped
                    // one, so a reference compare intermittently failed to recognise the node among
                    // its own siblings, counted past it, and produced a different path — the same
                    // element then got a different id on that render, breaking focus restore and
                    // anything else keyed by identity.
                    if (sib.Pointer == node.Pointer) break;
                    var sibName = sib.name;
                    if (string.IsNullOrEmpty(sibName)) sibName = sib.GetIl2CppType().Name;
                    if (sibName == name) index++;
                }
            }
            parts.Add(name + ":" + index);
            node = parent;
        }
        var sb = new StringBuilder();
        for (int i = parts.Count - 1; i >= 0; i--)
            sb.Append('/').Append(parts[i]);
        return sb.ToString();
    }

    // --- Interactive handles ---

    internal abstract class Handle
    {
        internal string ActionOwner;
        protected bool ValidateActionOwner(out string error)
        {
            error = ValidateScreen(ActionOwner);
            return error == null;
        }
        public abstract bool TryValidate(out string error);

        // Most handles act in a single main-thread call. A handle backed by a virtualised table
        // cell must instead scroll a recycled row back into view and wait across frames for its
        // controls to re-bind before acting, so it opts in here: Run resolves and validates it on
        // the main thread, then runs the action on the request's background thread, where it can
        // sleep between per-frame Invokes. Running such an action inside Run's one Invoke would
        // block the very frames the re-bind needs.
        public virtual bool DispatchesAcrossFrames => false;

        public virtual string Activate() => "Not activatable.";

        // The live VisualElement this handle acts on, used to open its right-click context menu and to
        // dump its ancestry. A virtualised cell realises (scrolls in) its element here. Null when the
        // handle is not element-backed (a menu data entry).
        public virtual VisualElement ContextMenuElement() => null;

        // What to title this handle's menu by when the menu's holder carries no text of its own. The
        // usual holder IS the entity ("Mohamed Wright"), so the title comes free; a textless holder
        // (a column header's TemplateContainer, a nation flag) leaves the menu a bare "Actions" unless
        // the handle names it. Null keeps that existing fallback.
        public virtual string MenuOwnerLabel() => null;
        public virtual int? ContextMenuLinkIndex() => null;

        public virtual string SetText(string v) => "Not a text field.";
        public virtual string SetValue(string v) => "Not adjustable.";
        // Choose option <opt> of a two-option segmented control. Returns "ok" on success — the
        // literal FM.ti.commit expects — or a message to speak. Selecting is by option, not a flip,
        // so committing the option already active is a no-op rather than a toggle.
        public virtual string SetSegment(int opt) => "Not a segmented control.";
        public virtual string Step(int dir) => "Not a stepper.";

        // The element's secondary text, read on demand. Default: nothing extra to report.
        public virtual string Describe() => "";

        // A handle that can open a dropdown popup yields its live adapter; the default is "no".
        public virtual bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
        {
            adapter = null;
            error = "That element is not a dropdown. Refresh the screen.";
            return false;
        }

        // A handle backing a date field yields its live adapter; the default is "no".
        public virtual bool TryGetDateAdapter(out Adapters.DateSelectorAdapter adapter, out string error)
        {
            adapter = null;
            error = "That element is not a date field. Refresh the screen.";
            return false;
        }

        // A handle backing a searchable dropdown yields its live adapter; the default is "no".
        public virtual bool TryGetSearchDropdownAdapter(out Adapters.SearchableDropdownAdapter adapter, out string error)
        {
            adapter = null;
            error = "That element is not a searchable dropdown. Refresh the screen.";
            return false;
        }
    }

    // A promoted menu leaf: a bare VisualElement (a message's sender-name link) surfaced as its own
    // stop so the entity's context menu stays reachable after the container it sat inside lost its
    // mark to the scope rule. It is menu-only — its whole reason to exist is the right-click menu — so
    // Activate opens that menu, the same gesture the mark advertises. See ContextMenuScope.
    internal sealed class MenuLeafHandle : Handle
    {
        public VisualElement Leaf;

        public override bool TryValidate(out string error)
        {
            error = null;
            if (Leaf.panel == null || !ElementBridge.IsVisible(Leaf))
            {
                error = "That element is no longer on screen. Refresh.";
                return false;
            }
            return true;
        }

        public override VisualElement ContextMenuElement() => Leaf;

        // Activate runs the context-menu open loop, which drives its own per-frame Invokes and
        // sleeps — run inside Run's one main-thread dispatch it deadlocks against the drain it is
        // itself holding (a measured 2s freeze ending in "The accessibility mod is busy.").
        public override bool DispatchesAcrossFrames => true;

        // Left-click and Shift+F10 both land here (the mark routes the gesture to /contextmenu via
        // app.js), but a direct /activate is honoured too by opening the same menu.
        public override string Activate() => OpenContextMenuOn(Leaf);
    }

    internal sealed class StopHandle : Handle
    {
        public IAccessibleElement El;
        internal string HistoryDirection;
        internal string ConversationContext;
        internal int? MessageId;
        internal string CalendarIdentity;
        internal SI.Core.Record CalendarWeekdayReference;
        public override bool DispatchesAcrossFrames => HistoryDirection != null
            || El is Adapters.ContextMenuButtonAdapter;

        public override bool TryValidate(out string error)
        {
            error = null;
            if (El is NativeBookmarkSurface.RecordButton bookmark && !bookmark.IsCurrent(out error))
                return false;
            if (El is ListWindowSurface.PageAdapter pager && !pager.IsVisible())
            {
                error = "That list is no longer on screen. Refresh.";
                return false;
            }
            if (CalendarIdentity != null)
            {
                var current = CalendarCommandIdentity.ReadWithReference(NativeElement(El), out var weekdayReference);
                if (CalendarIdentity == CalendarCommandIdentity.Pending || current != CalendarIdentity
                    || CalendarWeekdayReference != null && weekdayReference?.Pointer != CalendarWeekdayReference.Pointer)
                {
                    error = CalendarWeekdayReference != null
                        ? "That weekday changed or left this editor. Refresh before trying again."
                        : "That calendar date changed or is still loading. Refresh before trying again.";
                    return false;
                }
            }
            if (El is FilterPopupSurface.TriggerAdapter trigger
                && (trigger.Widget.panel == null || !ElementBridge.IsVisibleInHierarchy(trigger.Widget)))
            {
                error = "That element is no longer on screen. Refresh.";
                return false;
            }
            if (El is Adapters.MessageButtonAdapter && !MessageId.HasValue)
            {
                error = "That message identity is still loading. Wait for the inbox to update.";
                return false;
            }
            if (El is Adapters.PluginAdapterBase a)
            {
                var be = a.BackingElement;
                if (be.panel == null || !ElementBridge.IsVisibleInHierarchy(be))
                {
                    error = "That element is no longer on screen. Refresh.";
                    return false;
                }
                if (MessageId.HasValue && El is Adapters.MessageButtonAdapter message && message.MessageId != MessageId)
                {
                    error = "That message changed. Refresh the inbox before opening it.";
                    return false;
                }
                if(ConversationContext!=null&&ConversationContext!=SpeakToRenderer.AnswerContext(be,fresh:true))
                {
                    error="That conversation answer changed. Read the current question before answering.";
                    return false;
                }
            }
            if (El is Adapters.SITextLink.Part part && !part.IsCurrent)
            { error = "That text link changed. Refresh the screen."; return false; }
            return true;
        }

        public override string Activate()
        {
            if (HistoryDirection != null) return History(HistoryDirection);
            if (El is Adapters.ContextMenuButtonAdapter) return OpenStandaloneMenu(this);
            if (ServerText.IsDisabled(El)) return "That control is disabled.";
            if (!El.CanActivate) return ServerText.Clean(El.GetAnnouncement()) + " is not activatable.";
            var ann = ServerText.Clean(El.GetAnnouncement());
            MessageViewSurface.BeforeNativeContentAction(El);
            El.Activate();
            return "Activated " + ann;
        }

        public override VisualElement ContextMenuElement()
            => El is Adapters.PluginAdapterBase a ? a.BackingElement : null;
        public override string MenuOwnerLabel() => El is Adapters.SITextLink.Part ? El.GetLabel() : null;
        public override int? ContextMenuLinkIndex() => El is Adapters.SITextLink.Part part ? part.LinkIndex : null;

        // The button's descriptive remainder. A long tail reads as the tooltip; a short tail
        // has already been folded into the visible label, so the command reports that there is
        // nothing more; a button that never had a description re-announces its label so the
        // command always produces audible feedback.
        public override string Describe()
        {
            if (El is Adapters.TextFieldAdapter field && field.IsPassword)
                return (field.FieldLabel() ?? "Password") + ", password field";
            var desc = DisplayText.DescriptionOf(El);
            if (!string.IsNullOrEmpty(desc)) return desc;
            if (DisplayText.HasInlinedDescription(El)) return "No additional information.";
            return DisplayText.Of(El);
        }

        public override string SetText(string v)
        {
            if(El is Adapters.FM27ShortcutCaptureAdapter capture)
            {
                capture.SetText(v);
                return v=="Escape"?"Shortcut capture cancelled.":"Set to "+capture.GetCurrentText();
            }
            if (El is Adapters.TextFieldAdapter tf)
                return tf.SetText(v) ? (tf.IsPassword ? "Password updated." : "Set to " + v) : "Could not set this field.";
            return "Not a text field.";
        }

        public override string SetValue(string v)
        {
            if (El is Adapters.IntFieldAdapter n
                && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                // A box the game has greyed out (the vacation day count while the return-on-date
                // option is picked) takes no value; saying so beats a silent no-op that reads back
                // as a successful "Set to 1".
                if (!n.BackingElement.enabledInHierarchy) return "That field is disabled.";
                return "Set to " + n.SetValue(i).ToString(CultureInfo.InvariantCulture);
            }

            if (El is Adapters.SliderAdapter s
                && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                // A slider the game has disabled (Camera Height under a fixed camera) takes no
                // value; saying so beats a silent no-op "Set to 0".
                if (!s.BackingElement.enabledInHierarchy) return "That slider is disabled.";
                if(s.IsTimeline)
                {
                    s.SetValue(f);
                    return "Playback position selected.";
                }
                // Report the value the slider actually took (it clamps/snaps), not the request —
                // and by its row's value label ("Set to 1x") where one exists: the label updates in
                // the same frame as the write (verified live), and it is the slider's real meaning
                // where the raw number is just a preset index.
                var actual = s.SetValue(f);
                return "Set to " + (s.GetValueText() ?? actual.ToString(CultureInfo.InvariantCulture));
            }
            return "Not adjustable.";
        }

        public override string Step(int dir)
        {
            if (El is Adapters.ComboboxAdapter c)
            {
                if (dir > 0) c.Increment(); else c.Decrement();
                return c.GetCurrentValue() ?? "";
            }
            return "Not a stepper.";
        }

        public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
        {
            if (El is Adapters.DropdownAdapter dd) { adapter = dd; error = null; return true; }

            // A combobox opens its INTERNAL dropdown's choice list — the same commit path the
            // game's own click uses, and picking a value outright beats stepping to it (the
            // conditions table established this route; see ConditionRowHandle).
            if (El is Adapters.ComboboxAdapter c)
            {
                try
                {
                    if (c.HasDropdownChoices())
                    {
                        adapter = c.GetInternalDropdownAdapter();
                        error = null;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Speech.Degrade.Note($"combobox internal dropdown unavailable ({e.GetType().Name})");
                    adapter = null;
                    error = "That value list could not be opened. Refresh.";
                    return false;
                }
            }
            return base.TryGetDropdownAdapter(out adapter, out error);
        }

        public override bool TryGetSearchDropdownAdapter(out Adapters.SearchableDropdownAdapter adapter, out string error)
        {
            if (El is Adapters.SearchableDropdownAdapter sd) { adapter = sd; error = null; return true; }
            return base.TryGetSearchDropdownAdapter(out adapter, out error);
        }
    }

    internal sealed class CellHandle : Handle
    {
        public VisualElement Table;
        public int Row, Col;
        public string NativeRowIdentity;
        // Which interactive element within the cell this handle targets; cells can hold several.
        public int ElementIndex;
        // A single-select disclosure row-selector (staff-meeting advice). Activation selects the row by
        // its data index through the list's own selection API — index-native, no ScrollTo, no element
        // re-realisation — so a stale virtualised worldBound or shared-scroll shift cannot misfire it.
        public bool IsSelectionDisclosure;
        public bool IsManySelectRowSelector;

        // A streamed table only keeps a handful of rows realised; the rest are recycled once
        // scrolled away. Re-realising a recycled row scrolls it back in, but its controls re-bind
        // asynchronously over the next frames (same async bind the row read waits out). The first
        // re-realise after a scroll therefore finds no control yet, so this drives its own wait.
        public override bool DispatchesAcrossFrames => true;

        private const int RealizeBudgetMs = 1000; // total wait for a recycled row to re-bind, land,
                                                  // and clear the viewport edge (each stage costs
                                                  // frames, and a nudge restarts the settle)
        private const int ConfirmBudgetMs = 1000; // and then, for a multi-select row selector, the
                                                  // wait for the game to own the new checked value.
                                                  // Its own budget rather than a bigger shared one:
                                                  // a row that is genuinely gone should still say so
                                                  // in a second, not sit through a wait meant for a
                                                  // click that has already landed.
        private const int RealizeStepMs = 16;     // ~one frame between re-realise attempts
        private const int MaxNudges = 2;          // overshoot twice at most, then act where it is
        private const int StableFrames = 4;       // agreeing reads before the row counts as landed

        public override bool TryValidate(out string error)
        {
            if (!ValidateActionOwner(out error)) return false;
            error = null;
            if (Table.panel == null || !ElementBridge.IsVisibleInHierarchy(Table))
            {
                error = "That table is no longer on screen. Refresh.";
                return false;
            }
            return true;
        }

        // Runs on the request's background thread (Run defers cross-frame handles), so it may sleep
        // between attempts to let frames advance. Each attempt scrolls the row in and realises the
        // control in one Invoke, acting in the same Invoke once it binds so the live element cannot
        // recycle between realise and click. The element must be non-null AND activatable: a cell
        // handle is only ever made for an activatable control (a button, toggle, or dropdown), so a
        // partial bind that transiently surfaces a non-activatable stop (the age text before the
        // name link binds, which also shifts which stop sits at this index) just means "not ready
        // yet" — keep waiting. A row that never yields its activatable control within the budget is
        // genuinely gone (deleted/filtered out), the only case that reports unavailable.
        public override string Activate()
            => IsManySelectRowSelector ? StreamedTableLoader.WithExclusiveRead(ActivateCore) : ActivateCore();

        private string ActivateCore()
        {
            // A single-select disclosure row-selector selects by data index through the game's own
            // selection API — no ScrollTo, no element re-realisation, so there is nothing whose stale
            // worldBound could misfire. One main-thread call; the body refresh re-reads the disclosure
            // the selection opened. Distinct from the pointer/link cells below, which must re-realise a
            // live element and therefore wait across frames for a scrolled-away row to re-bind.
            if (IsSelectionDisclosure && NativeRowIdentity == null)
            {
                return MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.TableContext(Table, new ClassificationCache());
                    if (!ctx.SelectRow(Row, out var label))
                        return "That row is no longer available. Refresh.";
                    return string.IsNullOrEmpty(label) ? "Selected" : "Selected " + label;
                });
            }

            if (IsManySelectRowSelector && NativeRowIdentity == null)
            {
                return MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.TableContext(Table, new ClassificationCache());
                    return ToggleManyRow(ctx);
                });
            }

            long start = Environment.TickCount64;
            bool scrolled = false;                    // scroll once, then let the row settle
            UnityEngine.Rect settled = default;       // the rect seen on the previous attempt
            int stable = 0;                           // consecutive attempts that agreed on it
            int nudges = 0;                           // overshoot attempts spent un-clipping the row
            bool confirmingToggle = false;            // click fired; waiting on the game's own state
            bool toggleBefore = false;                // the bound value it must move away from
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.TableContext(Table, new ClassificationCache());
                    // Re-scrolling on every attempt restarts the very settle this loop waits for,
                    // so the scroll happens once and later attempts only re-read the row.
                    var el = ctx.RealizeCellElement(Row, Col, ElementIndex, scroll: !scrolled);
                    scrolled = true;
                    if (!ctx.MatchesRowIdentity(Row, NativeRowIdentity))
                        return confirmingToggle ? "The selection could not be confirmed because this row changed. Refresh." : null;
                    if (el == null) return null; // control not re-bound yet — retry
                    if (!el.CanActivate && !ServerText.IsDisabled(el)) return null;
                    if (IsSelectionDisclosure)
                    {
                        if (ctx.PendingCellsAt(Row) > 0 || ++stable < StableFrames) return null;
                        if (ServerText.IsDisabled(el)) return "That control is disabled.";
                        if (!ctx.SelectRow(Row, out var label)) return "That row is no longer available. Refresh.";
                        return string.IsNullOrEmpty(label) ? "Selected" : "Selected " + label;
                    }
                    if (IsManySelectRowSelector)
                    {
                        // FM27 record-backed rows keep their original binding and availability
                        // barriers before taking the upstream index-addressed selection entry.
                        if (ctx.PendingCellsAt(Row) > 0 || ++stable < StableFrames) return null;
                        if (ServerText.IsDisabled(el)) return "That control is disabled.";
                        return ToggleManyRow(ctx);
                    }
                    // The click has already gone in; the only thing left is to see the game own the
                    // new value. Answered before the geometry below because none of it applies any
                    // more — nothing further will be clicked, and the row re-binding as the
                    // selection lands would restart a settle this no longer needs. Re-read the live
                    // control each attempt (never the pre-click one): the row is being rebound
                    // underneath us, and the bound value is the only proof the game accepted it.
                    if (confirmingToggle)
                        return el is Adapters.ToggleAdapter confirming && confirming.IsChecked != toggleBefore
                            ? "Activated " + ServerText.Clean(el.GetAnnouncement())
                            : null;
                    if (el is not Adapters.PluginAdapterBase a) { el.Activate(); return "Activated " + ServerText.Clean(el.GetAnnouncement()); }
                    // A just-scrolled-in row's control exists before it is laid out, and an SIText
                    // link only fires its action once rendered (its link table is parsed at layout).
                    // Wait for a non-zero worldBound so cold-row clicks navigate, not just no-op.
                    var rect = a.BackingElement.worldBound;
                    if (rect.width <= 0f) return null;
                    // ...and then for the rect to STOP MOVING. A scrolled-in row reports the
                    // geometry of whoever the recycled element showed a moment ago, and the game
                    // resolves a cell click against the pointer position it is handed — so acting
                    // on the first non-zero rect dispatched to the row above or below the one the
                    // player picked, or nowhere at all. Agreement across StableFrames attempts is
                    // the row having landed; measured live, a click one or two frames earlier than
                    // that is still swallowed even though every reading already looks settled.
                    if (rect != settled) { settled = rect; stable = 1; return null; }
                    if (++stable < StableFrames) return null;
                    // ScrollTo scrolls the minimum that brings the row to an edge, so the row the
                    // player picked routinely lands half outside the viewport. The game swallows a
                    // click whose position falls in the clipped part, so overshoot one row further
                    // and come back, which leaves it fully clear of that edge.
                    var view = ctx.ViewportRect();
                    if (view.height > 0f && nudges < MaxNudges)
                    {
                        int step = rect.yMax > view.yMax ? 1 : rect.y < view.y ? -1 : 0;
                        if (step != 0)
                        {
                            nudges++;
                            ctx.NudgeIntoView(Row, step);
                            stable = 0;   // re-settle at the new position
                            return null;
                        }
                    }
                    // Recycled rows temporarily retain the previous occupant's
                    // disabled flag. Check native availability only after the
                    // same binding/layout settlement required before clicking.
                    if (ServerText.IsDisabled(el)) return "That control is disabled.";
                    // A name link's text lands before the styling that carries its <link> markup;
                    // without markup there is no id to fire but a guess, so keep waiting.
                    if (el is Adapters.TableRowTextAdapter link && !link.LinkReady) return null;
                    // A multi-select squad picker keeps the ExpandedToggle's real pointer path, and
                    // that pointer is validated against a position — firing it is not proof the
                    // game took it. So arm the confirmation above rather than answering here: the
                    // reply, and with it the companion's body refresh, waits until the bound
                    // Toggle.value has actually moved. Without that the checkbox showed checked for
                    // as long as it took the optimistic refresh to read the unchanged game state
                    // back and snap it off again.
                    if (el is Adapters.ToggleAdapter toggle && toggle.IsMultiSelectRowSelector)
                    {
                        toggleBefore = toggle.IsChecked;
                        confirmingToggle = true;
                        start = Environment.TickCount64;   // the confirm phase gets its own budget
                        el.Activate();
                        return null;
                    }

                    el.Activate();
                    return "Activated " + ServerText.Clean(el.GetAnnouncement());
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start < (confirmingToggle ? ConfirmBudgetMs : RealizeBudgetMs))
                {
                    System.Threading.Thread.Sleep(RealizeStepMs);
                    continue;
                }
                if (!confirmingToggle) return "That row is no longer available. Refresh.";
                // Request thread: Log.* would hard-crash off the main thread, use the background log.
                ServerText.LogBackground(
                    $"[Cell] row {Row} col {Col} selector still reads {toggleBefore} after {ConfirmBudgetMs}ms; reporting it unchanged.");
                return "The selection did not change. Try again.";
            }
        }

        private string ToggleManyRow(Contexts.TableContext ctx)
        {
            if (!ctx.ToggleRowSelection(Row, out var selected, out var label))
                return "That row is no longer available. Refresh.";
            if (selected == null)
            {
                Speech.Degrade.Note($"[Cell] row {Row} selection toggle left the selected count unchanged or unreadable");
                return "The selection did not change. Try again.";
            }
            return "Activated " + (string.IsNullOrEmpty(label) ? "" : label + ", ")
                + "checkbox, " + (selected.Value ? "checked" : "unchecked");
        }

        // Choose an option of a two-option segmented cell control (the opposition grid's
        // Position/Player column). Same re-realise-across-frames protocol as Activate — a recycled
        // row's control re-binds asynchronously, so the first attempt on a scrolled-away row finds
        // nothing. Says nothing about the resulting state: SelectOption drives the game and the
        // page refresh that follows re-reads the real selection, so a choice the game refused shows
        // as the game's value rather than as ours.
        public override string SetSegment(int opt)
        {
            long start = Environment.TickCount64;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.TableContext(Table, new ClassificationCache());
                    var el = ctx.RealizeCellElement(Row, Col, ElementIndex);
                    if (!ctx.MatchesRowIdentity(Row, NativeRowIdentity)) return null;
                    if (el is not Adapters.SegmentedToggleAdapter seg) return null; // not re-bound yet
                    if (seg.BackingElement.worldBound.width <= 0f) return null;     // not laid out yet
                    if (opt < 0 || opt >= seg.Options().Length)
                        return "That option is no longer available. Refresh.";
                    seg.SelectOption(opt);
                    return "ok";
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start >= RealizeBudgetMs)
                    return "That row is no longer available. Refresh.";
                System.Threading.Thread.Sleep(RealizeStepMs);
            }
        }

        // Re-realises (scrolls in) the virtualised cell so the right-click aims at a live, laid-out
        // element. A row whose control has not re-bound or laid out yet returns a zero-rect element;
        // OpenContextMenu(id) retries until it settles or the realize budget elapses.
        public override VisualElement ContextMenuElement()
        {
            if (!TryValidate(out _)) return null;
            var ctx = new Contexts.TableContext(Table, new ClassificationCache());
            var el = ctx.RealizeCellElement(Row, Col, ElementIndex);
            if (!ctx.MatchesRowIdentity(Row, NativeRowIdentity)) return null;
            return el is Adapters.PluginAdapterBase a ? a.BackingElement : null;
        }

        public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
            => TryGetDropdownAdapter(out adapter, out error, true);

        internal bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error, bool scroll)
        {
            if (!TryValidate(out error)) { adapter = null; return false; }
            // Re-realise the virtualized cell element (scrolling its row into view) so the popup
            // anchors to a live element, not a recycled one.
            var ctx = new Contexts.TableContext(Table, new ClassificationCache());
            // By kind, not by ElementIndex: a split-button cell's indices shift with the active
            // profile's hide rules (see RealizeCellDropdown), and the caller wants THE dropdown.
            var cellDd = ctx.RealizeCellDropdown(Row, Col, scroll);
            if (!ctx.MatchesRowIdentity(Row, NativeRowIdentity))
            { adapter = null; error = "That row is no longer available. Refresh."; return false; }
            if (cellDd != null) { adapter = cellDd; error = null; return true; }
            // The walk rendered this cell as a dropdown, so no dropdown stop here means the row is
            // gone or its controls have not re-bound — log what the cell holds now, or the failure
            // is undiagnosable from the player's "not a dropdown" alone.
            var el = ctx.RealizeCellElement(Row, Col, ElementIndex, scroll: false);
            if (el == null) { adapter = null; error = "That row is no longer available. Refresh."; return false; }
            ServerText.LogBackground(
                $"[CellHandle] dropdown open resolved row={Row} col={Col} idx={ElementIndex} to "
                + $"{el.GetType().Name} role={el.Role ?? "?"} \"{ServerText.Clean(el.GetAnnouncement())}\"");
            return base.TryGetDropdownAdapter(out adapter, out error);
        }
    }

    // A fixture-schedule cell control: the row-selection checkbox or a cell's button (opposition,
    // result, competition). The schedule list virtualises, so the handle holds no cell element — it
    // re-realises the control by data index and column key at action time, waiting across frames
    // for a recycled row's controls to re-bind. Same protocol as CellHandle, driven through
    // FixtureScheduleContext instead of TableContext (the schedule is a StreamedObjectList, not a
    // StreamedTable).
    internal sealed class FixtureRowHandle : Handle
    {
        public VisualElement List; // the StreamedObjectList("FixturesList")
        public int Row;            // the fixture's data index in the virtualised list
        public string ColumnKey;   // "Selected", "Opposition", "Result", "Competition", ...
        public string NativeIdentity;

        public override bool DispatchesAcrossFrames => true;

        private const int RealizeBudgetMs = 1000;
        private const int RealizeStepMs = 16;
        private const int StableFrames = 4;
        private const int SelectionConfirmBudgetMs = 1000;

        public override bool TryValidate(out string error)
        {
            if (!ValidateActionOwner(out error)) return false;
            error = null;
            if (List == null || List.panel == null || !ElementBridge.IsVisibleInHierarchy(List))
            {
                error = "That fixture list is no longer on screen. Refresh.";
                return false;
            }
            if (string.IsNullOrEmpty(NativeIdentity))
            {
                error = "This fixture could not be identified. Refresh.";
                return false;
            }
            return true;
        }

        // Runs on the request's background thread (Run defers cross-frame handles). Each attempt
        // scrolls the row in and resolves the control in one Invoke, acting in the same Invoke once
        // it binds so the live element cannot recycle between resolve and click.
        public override string Activate()
        {
            // Harvests and commands drive this same native scroll position.
            // Wait off the Unity thread, before starting the realization budget,
            // and keep the existing owner/reference checks inside each dispatch.
            if (!System.Threading.Monitor.TryEnter(FixtureScheduleLoader.ReadLock, 10000))
                return "The fixture list is still updating. Refresh.";
            try { return ActivateAfterHarvest(); }
            finally { System.Threading.Monitor.Exit(FixtureScheduleLoader.ReadLock); }
        }

        private string ActivateAfterHarvest()
        {
            // The Sel. checkbox is bound to the list's row selection; a synthetic checkbox click is
            // reverted by that binding next frame. Realize/revalidate its actual
            // fixture first, then use the native controller once and observe the
            // bound selection; never replay an uncertain selection.
            long start = Environment.TickCount64;
            int stable = 0;
            UnityEngine.Rect settled = default;
            bool selectionIssued = false;
            int selectionStable = 0;
            long selectionStarted = 0;
            string selectionLabel = null;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.FixtureScheduleContext(List,
                        ColumnKey == "Selected" ? new[] { "Selected" } : System.Array.Empty<string>());
                    if (selectionIssued)
                    {
                        if (!ctx.MatchesRowIdentity(Row, NativeIdentity))
                            return "The fixture selection could not be confirmed because this row changed. Refresh.";
                        var selected = ctx.ReadRow(Row);
                        if (selected?.Cells.Count > 0 && selected.Cells[0].On
                            && FixtureHistorySelection.Read(List)?.Reference == NativeIdentity)
                        {
                            if (++selectionStable < StableFrames) return null;
                            return string.IsNullOrEmpty(selectionLabel) ? "Selected" : "Selected " + ServerText.Clean(selectionLabel);
                        }
                        selectionStable = 0;
                        return null;
                    }
                    if (ctx.ScrollToRowPending(Row) != 0 || !ctx.MatchesRowIdentity(Row, NativeIdentity))
                    { stable = 0; return null; }
                    if (ColumnKey == "Selected")
                    {
                        if (++stable < StableFrames) return null;
                        if (!ctx.SelectRow(Row, out selectionLabel)) return "That fixture is no longer available. Refresh.";
                        selectionIssued = true;
                        selectionStarted = Environment.TickCount64;
                        return null;
                    }
                    var el = ctx.RealizeControl(Row, ColumnKey);
                    if (!ctx.MatchesRowIdentity(Row, NativeIdentity)) { stable = 0; return null; }
                    if (el == null) return null; // control not re-bound yet — retry
                    var adapter = ElementBridge.CreateAdapter(el, ElementClassification.Button);
                    if (adapter == null || !adapter.CanActivate) return null;
                    // A just-scrolled-in row's control exists before layout; wait for a non-zero
                    // worldBound so cold-row clicks land, not no-op (same rule as CellHandle).
                    var rect = el.worldBound;
                    if (rect.width <= 0f) { stable = 0; return null; }
                    if (rect != settled) { settled = rect; stable = 1; return null; }
                    if (++stable < StableFrames) return null;
                    var ann = ServerText.Clean(adapter.GetAnnouncement());
                    adapter.Activate();
                    return "Activated " + ann;
                });
                if (result != null) return result;
                if (selectionIssued ? Environment.TickCount64 - selectionStarted >= SelectionConfirmBudgetMs
                    : Environment.TickCount64 - start >= RealizeBudgetMs)
                    return selectionIssued ? "The fixture selection could not be confirmed. Refresh."
                        : "That fixture is no longer available. Refresh.";
                System.Threading.Thread.Sleep(RealizeStepMs);
            }
        }

        // Re-realises the cell so the right-click aims at a live, laid-out element; the caller
        // (OpenContextMenu) retries until it settles.
        public override VisualElement ContextMenuElement() => RealizeCell();

        private VisualElement RealizeCell()
        {
            if (!TryValidate(out _)) return null;
            var ctx = new Contexts.FixtureScheduleContext(List, System.Array.Empty<string>());
            var cell = ctx.RealizeControl(Row, ColumnKey);
            return ctx.MatchesRowIdentity(Row, NativeIdentity) ? cell : null;
        }
    }

    // One control of a responsibility duty (or of a group, with Duty -1): its Take Control /
    // Delegate buttons, its assignee dropdown, or the group's two bulk buttons. The group list and
    // each group's duty list both virtualise, so the handle holds no element — it re-realises the
    // control by (group, duty, slot) at action time, waiting across frames for a recycled row's
    // controls to re-bind. Same protocol as LeagueRowHandle, driven through
    // ResponsibilitiesContext; the assignee slot opens its popup via TryGetDropdownAdapter.
    internal sealed class ResponsibilityRowHandle : Handle
    {
        public VisualElement List;  // the StreamedObjectList hosting the group list
        public int Group;           // the group's data index
        public int Duty;            // the duty's data index within the group, or -1 for the group itself
        public string Slot;         // ResponsibilitiesContext.Slot*
        public string GroupIdentity, DutyIdentity;

        public override bool DispatchesAcrossFrames => true;

        private const int RealizeBudgetMs = 400;
        private const int RealizeStepMs = 16;

        public override bool TryValidate(out string error)
        {
            error = null;
            if (List.panel == null || !ElementBridge.IsVisibleInHierarchy(List))
            {
                error = "That responsibilities list is no longer on screen. Refresh.";
                return false;
            }
            if (string.IsNullOrEmpty(GroupIdentity) || (Duty >= 0 && string.IsNullOrEmpty(DutyIdentity)))
            {
                error = "That responsibility's native identity could not be read. Refresh.";
                return false;
            }
            return true;
        }

        public override string Activate()
        {
            long start = Environment.TickCount64;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    var el = RealizeCell();
                    if (el == null) return null; // control not re-bound yet — retry
                    var adapter = ElementBridge.CreateAdapter(el, ElementClassification.Button);
                    if (adapter == null || !adapter.CanActivate) return null;
                    if (el.worldBound.width <= 0f) return null;
                    var ann = ServerText.Clean(adapter.GetAnnouncement());
                    adapter.Activate();
                    return "Activated " + ann;
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start >= RealizeBudgetMs)
                    return "That responsibility is no longer available. Refresh.";
                Thread.Sleep(RealizeStepMs);
            }
        }

        public override VisualElement ContextMenuElement() => RealizeCell();

        public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
        {
            // Re-realise the duty so the popup anchors to a live element, not a recycled one.
            var el = RealizeCell();
            if (el == null) { adapter = null; error = "That responsibility is no longer available. Refresh."; return false; }
            if (ElementBridge.CreateAdapter(el, ElementClassification.Dropdown) is Adapters.DropdownAdapter dd)
            {
                adapter = dd; error = null; return true;
            }
            return base.TryGetDropdownAdapter(out adapter, out error);
        }

        private VisualElement RealizeCell()
            => new Contexts.ResponsibilitiesContext(List).RealizeControl(Group, Duty, Slot, GroupIdentity, DutyIdentity);
    }

    // An Add/Remove Leagues cell control: a rule/setting dropdown or the remove button. The list
    // virtualises, so the handle holds no element — it re-realises the control by data index and
    // column key at action time, waiting across frames for a recycled row's controls to re-bind.
    // Same protocol as FixtureRowHandle, driven through LeagueTableContext. Dropdown columns
    // ("men-rule", "men-setting", "women-rule", "women-setting") open a popup via
    // TryGetDropdownAdapter; the "remove" column activates the delete button.
    internal sealed class LeagueRowHandle : Handle
    {
        public VisualElement List; // the StreamedObjectList of league rows
        public int Row;            // the nation's data index in the virtualised list
        public string ColumnKey;   // "men-rule", "men-setting", "women-rule", "women-setting", "remove"

        public override bool DispatchesAcrossFrames => true;

        private const int RealizeBudgetMs = 400;
        private const int RealizeStepMs = 16;

        public override bool TryValidate(out string error)
        {
            error = null;
            if (List.panel == null)
            {
                error = "That league list is no longer on screen. Refresh.";
                return false;
            }
            return true;
        }

        public override string Activate()
        {
            long start = Environment.TickCount64;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    var el = RealizeCell();
                    if (el == null) return null; // control not re-bound yet — retry
                    var adapter = ElementBridge.CreateAdapter(el, ElementClassification.Button);
                    if (adapter == null || !adapter.CanActivate) return null;
                    if (el.worldBound.width <= 0f) return null;
                    var ann = ServerText.Clean(adapter.GetAnnouncement());
                    adapter.Activate();
                    return "Activated " + ann;
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start >= RealizeBudgetMs)
                    return "That nation is no longer available. Refresh.";
                System.Threading.Thread.Sleep(RealizeStepMs);
            }
        }

        public override VisualElement ContextMenuElement() => RealizeCell();

        public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
        {
            // Re-realise (scroll in) the row so the popup anchors to a live element, not a recycled one.
            var el = RealizeCell();
            if (el == null) { adapter = null; error = "That nation is no longer available. Refresh."; return false; }
            if (ElementBridge.CreateAdapter(el, ElementClassification.Dropdown) is Adapters.DropdownAdapter dd)
            {
                adapter = dd; error = null; return true;
            }
            return base.TryGetDropdownAdapter(out adapter, out error);
        }

        private VisualElement RealizeCell()
            => new Contexts.LeagueTableContext(List).RealizeControl(Row, ColumnKey);
    }

    // One control of a Player Search condition row. The list virtualises, so the handle holds no
    // element — it re-realises the control by data index and slot key at action time, waiting
    // across frames for a recycled row's controls to re-bind. Same protocol as LeagueRowHandle,
    // driven through ConditionTableContext. "enabled"/"andor" toggle their checkbox, "up"/"down"/
    // "delete" click their button, "compare" and the dropdown value slots open a popup via
    // TryGetDropdownAdapter, and the combobox value slots step via Step.
    internal sealed class ConditionRowHandle : Handle
    {
        public VisualElement List; // the StreamedObjectList of condition rows
        public int Row;            // the condition's data index in the virtualised list
        public string ColumnKey;   // "enabled", "andor", "compare", "up", "down", "delete", or a value slot name
        public string NativeIdentity;
        public string RuleContext;
        public string ClientContext;

        public override bool DispatchesAcrossFrames => true;

        private const int RealizeBudgetMs = 400;
        private const int RealizeStepMs = 16;

        public override bool TryValidate(out string error)
        {
            if (!ValidateActionOwner(out error)) return false;
            error = null;
            if (List == null || List.panel == null || !ElementBridge.IsVisibleInHierarchy(List))
            {
                error = "That condition list is no longer on screen. Refresh.";
                return false;
            }
            if (string.IsNullOrEmpty(NativeIdentity) || string.IsNullOrEmpty(RuleContext))
            {
                error = "This search condition could not be identified. Refresh.";
                return false;
            }
            return true;
        }

        public override string Activate()
        {
            bool isCheckbox = ColumnKey is "enabled" or "andor";
            long start = Environment.TickCount64;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    var el = RealizeCell();
                    if (el == null) return null; // control not re-bound yet — retry
                    var adapter = ElementBridge.CreateAdapter(el,
                        isCheckbox ? ElementClassification.Checkbox : ElementClassification.Button);
                    if (adapter == null || !adapter.CanActivate) return null;
                    var ann = ServerText.Clean(adapter.GetAnnouncement());
                    adapter.Activate();
                    return "Activated " + ann;
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start >= RealizeBudgetMs)
                    return "That condition is no longer available. Refresh.";
                System.Threading.Thread.Sleep(RealizeStepMs);
            }
        }

        public override string SetText(string v)
        {
            long start = Environment.TickCount64;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    var el = RealizeCell();
                    if (el == null || el.GetIl2CppType().Name != "BindableTextEditBox") return null;
                    var tf = new Adapters.TextFieldAdapter(el);
                    return tf.SetText(v) ? "Set to " + v : "Could not set this field.";
                });
                if (result != null) return result;
                if (Environment.TickCount64 - start >= RealizeBudgetMs)
                    return "That condition is no longer available. Refresh.";
                System.Threading.Thread.Sleep(RealizeStepMs);
            }
        }

        public override VisualElement ContextMenuElement() => RealizeCell();

        public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
        {
            // Re-realise (scroll in) the row so the popup anchors to a live element, not a recycled one.
            var el = RealizeCell();
            if (el == null) { adapter = null; error = "That condition is no longer available. Refresh."; return false; }

            // Keep the native preset menu and the native +/- commands as distinct operations.
            // The preset list is not exhaustive: e.g. current attribute presets omit 16,
            // which the native step command can select and independently confirm.
            if (el.GetIl2CppType().Name == "FMCombobox")
            {
                try
                {
                    adapter = new Adapters.ComboboxAdapter(el).GetInternalDropdownAdapter();
                    error = null;
                    return true;
                }
                catch (System.Exception e)
                {
                    Speech.Degrade.Note($"condition combobox internal dropdown unavailable ({e.GetType().Name})");
                    adapter = null;
                    error = "That value list could not be opened. Refresh.";
                    return false;
                }
            }

            if (ElementBridge.CreateAdapter(el, ElementClassification.Dropdown) is Adapters.DropdownAdapter dd)
            {
                adapter = dd; error = null; return true;
            }
            return base.TryGetDropdownAdapter(out adapter, out error);
        }

        public override bool TryGetDateAdapter(out Adapters.DateSelectorAdapter adapter, out string error)
        {
            var element = RealizeCell();
            if (element == null || element.GetIl2CppType().Name != "DateSelectorWidget")
            {
                adapter = null;
                error = "That condition date is no longer available. Refresh.";
                return false;
            }
            adapter = new Adapters.DateSelectorAdapter(element);
            error = ServerText.IsDisabled(adapter) ? "That condition date is disabled." : null;
            return error == null;
        }

        internal Func<string> CaptureValueValidation()
        {
            var context = new Contexts.ConditionTableContext(List);
            string expected = Contexts.ConditionTableContext.AdjustmentContext(context.ReadRow(Row), ColumnKey);
            return () => !TryValidate(out var error) ? error
                : context.MatchesRowIdentity(Row, NativeIdentity)
                    && expected != null
                    && Contexts.ConditionTableContext.AdjustmentContext(context.ReadRow(Row), ColumnKey) == expected
                    ? null : "That search condition changed before the adjusted value could be confirmed. Refresh.";
        }

        private VisualElement RealizeCell()
        {
            if (!TryValidate(out _)) return null;
            var ctx = new Contexts.ConditionTableContext(List);
            var cell = ctx.RealizeControl(Row, ColumnKey, NativeIdentity, RuleContext);
            return ctx.MatchesRowContext(Row, NativeIdentity, RuleContext) ? cell : null;
        }

        internal string ValidateSelectionContext()
            => !TryValidate(out var error) ? error
                : new Contexts.ConditionTableContext(List).MatchesRowContext(Row, NativeIdentity, RuleContext)
                    ? null : "That search condition has changed. Refresh.";

        public override string Step(int direction)
        {
            long started = Environment.TickCount64, sentAt = 0;
            bool sent = false;
            string before = null, adjustmentContext = null;
            while (true)
            {
                var result = MainThreadDispatcher.Invoke(() =>
                {
                    if (!TryValidate(out var error)) return error;
                    var ctx = new Contexts.ConditionTableContext(List);
                    var element = sent ? ctx.RealizeControl(Row, ColumnKey, NativeIdentity) : RealizeCell();
                    if (element == null) return null;
                    if (element.GetIl2CppType().Name != "FMCombobox") return "That condition value is not adjustable.";
                    var row = ctx.ReadRow(Row);
                    var currentContext = Contexts.ConditionTableContext.AdjustmentContext(row, ColumnKey);
                    if (sent && currentContext != adjustmentContext)
                        return "That search condition changed before the adjusted value could be confirmed. Refresh.";
                    var combo = new Adapters.ComboboxAdapter(element);
                    if (sent)
                    {
                        string current = combo.GetCurrentValue();
                        return !string.IsNullOrEmpty(current) && current != before ? current : null;
                    }
                    if (ServerText.IsDisabled(combo)) return "That condition value is disabled.";
                    before = combo.GetCurrentValue();
                    if (direction > 0 ? !combo.IsIncrementEnabled() : !combo.IsDecrementEnabled())
                        return before + (direction > 0 ? ", maximum" : ", minimum");
                    if (element.worldBound.width <= 0) return null;
                    adjustmentContext = currentContext;
                    if (direction > 0) combo.Increment(); else combo.Decrement();
                    sent = true;
                    sentAt = Environment.TickCount64;
                    return null;
                });
                if (result != null) return result;
                if (sent ? Environment.TickCount64 - sentAt >= 2000 : Environment.TickCount64 - started >= RealizeBudgetMs)
                    return sent ? "Adjustment sent, but the updated condition value could not be confirmed. Refresh before trying again."
                        : "That condition value is no longer available. Refresh.";
                Thread.Sleep(RealizeStepMs);
            }
        }
    }

    // A column-header sort trigger. Dispatches the game's own header click (the sort), staying
    // silent so the refreshed table's aria-sort speaks the real new state rather than the cursor
    // surface's spoken "sorted". Headers are not virtualised, so the adapter holds a live button.
    internal sealed class HeaderHandle : Handle
    {
        // The sort-direction icon settles a frame or two after the click; an immediate read catches
        // a transient empty state, so the result is read after this wait.
        private const int SortSettleMs = 250;

        public Adapters.ColumnHeaderAdapter Header;

        public override bool TryValidate(out string error)
        {
            error = null;
            var be = ((Adapters.PluginAdapterBase)Header).BackingElement;
            if (be.panel == null || !ElementBridge.IsVisible(be))
            {
                error = "That column header is no longer on screen. Refresh.";
                return false;
            }
            return true;
        }

        // Headers are not virtualised, so the button reference held from the walk is still the live
        // element — nothing to realize. The manipulator itself sits on the header's TemplateContainer
        // (StreamedTable.Header.Root, the button's parent), which Resolve finds as an ancestor.
        public override VisualElement ContextMenuElement()
            => ((Adapters.PluginAdapterBase)Header).BackingElement;

        // The menu is the COLUMN's options, and its holder (the header TemplateContainer) has no text,
        // so the column name has to come from here for the view to read "Actions for Pas %".
        public override string MenuOwnerLabel() => ServerText.Clean(Header.GetLabel());

        public override string Activate()
        {
            // Whether the game actually re-sorts is its call (not every column sorts); the click is
            // always honest. The HTTP reply stays silent ("Activated ..."); the result is announced
            // after the icon settles, off the main thread, through the same Prism/NVDA channel
            // /speak uses, so the player hears it without any companion-side change. An unsorted
            // column reports "not sorted" rather than implying success.
            if (!Header.ClickHeader()) return "That column could not be activated. Refresh.";
            var label = ServerText.Clean(Header.GetLabel());
            var header = Header;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(SortSettleMs);
                    MainThreadDispatcher.Invoke(() =>
                    {
                        var state = header.GetState();
                        SpeechPipeline.SpeakInterrupt(string.IsNullOrEmpty(state) ? $"{label}, not sorted" : $"{label}, {state}");
                        return true;
                    });
                }
                catch (System.Exception ex)
                {
                    ServerText.LogBackground($"[HeaderHandle] sort announce failed: {ex.Message}");
                }
            });
            return "Activated " + label;
        }
    }

    // A paged table's Previous/Next page button. It touches no game element at all: it moves the mod's
    // own view onto the table (TablePages), and the refresh that follows re-reads the new page live
    // from the game. So there is nothing to validate against the screen — a table that vanished under
    // the click simply is not rendered by that refresh — and the reply is the player's only feedback
    // for a move whose button label does not change, which is why it states the new position.
    internal sealed class TablePageHandle : Handle
    {
        public string TablePath;
        // A step (-1 / +1) rather than the page it was rendered next to: the step is applied to the
        // page the table is on WHEN THE BUTTON IS PRESSED. A player who presses Next twice before
        // the first refresh has landed then moves two pages, where an absolute target would have
        // re-issued the same page and read as a control that stopped responding.
        public int Delta;
        public int PageCount;

        public override bool TryValidate(out string error)
        {
            error = null;
            return true;
        }

        public override string Activate()
        {
            int page = Navigation.TablePaging.Clamp(TablePages.Get(TablePath) + Delta, PageCount);
            TablePages.Set(TablePath, page);
            return Navigation.TablePaging.Label(page, PageCount);
        }
    }

    // The mod-authored Select all / Clear selection buttons above a multi-select table. Ticking
    // dozens of row checkboxes one at a time is the one selection gesture a screen reader user
    // cannot shortcut where a sighted player shift-clicks a region; both buttons drive the game's
    // own selection entries (TableContext.SelectAllRows / ClearRowSelection), so the Actions tools
    // see exactly what a manual selection produces. Holds the live table element and re-resolves
    // its list at press time - nothing about the selection is cached.
    internal sealed class TableSelectionHandle : Handle
    {
        public VisualElement Table;
        public bool Clear;
        public string PageTablePath;

        public override bool TryValidate(out string error)
        {
            if (Table.panel == null || !ElementBridge.IsVisible(Table))
            {
                error = "That table is no longer on screen. Refresh.";
                return false;
            }
            error = null;
            return true;
        }

        public override string Activate()
        {
            var ctx = new Contexts.TableContext(Table, new ClassificationCache());
            if (Clear) return ctx.ClearRowSelection();
            if (PageTablePath != null)
                return ctx.SelectPageRows(TablePages.Get(PageTablePath), StreamedTableLoader.PageRows);
            return ctx.SelectAllRows();
        }
    }

    // One menu-bearing sub-element of a composite tile (a calendar event's competition or opponent leaf):
    // a specific SIText link that carries both a left-click navigation (its <link>) and its own right-click
    // menu. The handle holds the live leaf, so its menu opens on exactly that sub-element — the basis for
    // per-sub-element targeting. Activation fires the link when the leaf has one; otherwise the leaf is
    // menu-only (right-click opens it, Enter does nothing actionable).
    internal sealed class LeafHandle : Handle
    {
        public VisualElement Leaf;
        public bool HasLink;

        public override bool TryValidate(out string error)
        {
            if (Leaf.panel == null || !ElementBridge.IsVisible(Leaf))
            {
                error = "That item is no longer on screen. Refresh.";
                return false;
            }
            error = null;
            return true;
        }

        public override VisualElement ContextMenuElement() => Leaf;

        public override string Activate()
        {
            if (!HasLink) return "Not activatable.";
            Adapters.SITextLink.Activate(Leaf);
            return "Activated";
        }
    }
}

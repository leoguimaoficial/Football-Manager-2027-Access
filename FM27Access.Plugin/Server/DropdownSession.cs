using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Server-side dropdown interaction session: the HTML surface's port of the speech
/// DropdownHandler's level stack. One session at a time. All game-object access runs
/// on the main thread via MainThreadDispatcher.Invoke; the request thread orchestrates
/// the multi-frame waits (popup visibility, async row binding, virtualized scrolling)
/// with sleeps between Invokes, standing in for the speech handler's frame counting.
///
/// Levels are addressed by a dot-joined path of parent choice indices ("" = root,
/// "2" = submenu opened from root choice 2, "2.0" = its first submenu, ...). Every
/// request validates the path against the live stack, so a stale page fragment can
/// never act on the wrong level.
///
/// The server serves each request on its own thread, so overlapping session requests are
/// possible (two opens racing, or the companion and the dev driver acting at once). Without
/// serialization the loser reads the winner's levels and silently returns the WRONG dropdown's
/// options (observed live). Every request-thread entry point therefore locks
/// <see cref="SessionGate"/> for its full orchestration, and a session is stamped with the
/// handle id that opened it: actions that carry an id are refused when the live session
/// belongs to a different owner (id-less calls, the dev driver's, act on whatever is open).
/// </summary>
internal static class DropdownSessionManager
{
    /// <summary>
    /// Serializes the request-thread entry points of BOTH popup session types (this manager and
    /// <see cref="SearchDropdownSession"/> — each opens by closing the other, so they must not
    /// interleave). Never taken on the main thread: CloseCurrentOnMainThread runs inside Invoke
    /// while a gated request thread may be blocked waiting for that same Invoke queue, so locking
    /// there would deadlock. Holders block on Invoke while holding the gate, which is safe for
    /// the same reason.
    /// </summary>
    internal static readonly object SessionGate = new();

    private const int HarvestCap = 200;
    private const int FrameMs = 16;
    private const int SettleMs = 80;           // ~5 frames at 60fps; the read-retry loop
                                               // (ReadRetries x ReadRetryMs) absorbs a late bind
    // Overall budget for the popup to surface, across ShowPopup re-invokes (see Open): a cell
    // dropdown's first ShowDropdown can be silently swallowed by the game (observed on the
    // opposition-instructions table: the first invoke never surfaces the popup no matter how
    // long the wait, while the immediate second invoke opens it in under half a second — the
    // same manual retry the player used to perform after hearing the old failure message). Each
    // invoke gets ShowWaitMs before the next try; a genuinely dead dropdown takes the full
    // budget to report, which is rarer and cheaper than failing a working one.
    private const int VisibleTimeoutMs = 2500;
    private const int ShowWaitMs = 700;
    private const int ReadRetries = 12;
    private const int ReadRetryMs = 50;
    private const int SelectionSettleMs = 150;
    private const int RealizeBudgetMs = 1000;  // Same native recycled-row budget as CellHandle.Activate.

    private sealed class Level
    {
        public DropdownAdapter Adapter;
        public List<string> Choices = new();
        public List<bool> Disabled = new();
        public bool[] HasChildren = Array.Empty<bool>();
        public int ParentIndex = -1; // choice index in the parent level that opened this one
        public int TotalCount;
        public int HarvestedTo;      // next un-harvested absolute index (scroll mode)
        public bool ScrollMode;
        public int SelectedIndex = -1;
        // Per-choice checkbox state for a multi-select toggle list ("Staff Interest (N)", "Quick
        // Filters"), null for an ordinary select-one dropdown. See DropdownAdapter.BuildIsCheckedArray.
        public bool[] Checked;
    }

    // Touched only on the main thread (inside Invoke) except for null checks on request
    // threads, which SessionGate serializes.
    private static List<Level> _levels;

    // Handle id the live session was opened for (the standalone dropdown's id, or the player
    // card's). Set with _levels, nulled with it in CloseCurrentOnMainThread.
    private static string _ownerId;
    private static string _ownerScreen;
    private static Func<string> _conditionGuard;
    private static string _ownerContext;

    /// <summary>
    /// The top level, or null when the session has been torn down while an operation was in flight.
    ///
    /// Every dropdown operation reaches the game through <see cref="Top"/>, and a session can be
    /// cleared between the dispatched actions that make one up — a timed-out operation's queued
    /// cleanup, or a select that nulled the stack. <see cref="Top"/> then dereferences a null list.
    /// The resulting NullReferenceException is not cheap here: thrown from a dispatched action inside
    /// the game's frame, it has been measured holding the main thread for 10-32 seconds, which the
    /// player experiences as the game freezing and the mod reports as the game not responding.
    ///
    /// So this path must not throw at all. Callers read this, find null, and degrade honestly.
    /// </summary>
    private static Level TopOrNull => _levels != null && _levels.Count > 0 ? _levels[_levels.Count - 1] : null;

    // Refuses an action aimed at a session this manager no longer holds: the page fragment the
    // user is browsing was built by an earlier session, and acting by index against the current
    // one would hit a different dropdown than the label they heard. Empty owner = an id-less
    // dev-driver call, which acts on whatever is open.
    private static string ValidateOwner(string owner, [CallerMemberName] string op = null)
    {
        if (string.IsNullOrEmpty(owner)) return null;
        if (_levels == null)
        {
            // Named because a leaf selection closes the session on purpose, so a request arriving just
            // after one is routine rather than wrong — and which request it is decides whether the
            // refusal should be spoken at all. Both this and the OpenPanel guard say the same sentence,
            // which is precisely what the log could not tell apart.
            Log($"{op} refused: no session open (owner={owner})");
            return SessionGoneMessage;
        }
        return owner == _ownerId ? null : "Stale dropdown state. Close and reopen the dropdown.";
    }

    // --- Entry points (request thread) ---

    public static string Open(string id, string expectedScreen = null, string expectedContext = null)
    {
        lock (SessionGate)
        try
        {
            // A virtualized cell's dropdown re-binds asynchronously after its recycled row is
            // scrolled back in, so the first resolve can transiently miss the control — or read
            // it disabled — mid-rebind (the same async re-bind CellHandle.Activate waits out).
            // For those handles, retry the resolve across frames before trusting a failure;
            // a standalone dropdown's first verdict is authoritative and returns immediately.
            // The across-frames flag rides the attempt's own dispatch (it is only consulted
            // after a failed attempt) rather than paying a whole frame to read it up front.
            long start = Environment.TickCount64;
            string err;
            bool retries;
            bool scrolled = false;
            while (true)
            {
                (err, retries) = MainThreadDispatcher.Invoke(() =>
                {
                    var stale = ServerHandles.ValidateScreen(expectedScreen);
                    if (stale != null) return (stale, false);
                    bool acrossFrames = ServerHandles.DispatchesAcrossFrames(id);
                    CloseCurrentOnMainThread();
                    // Scroll once. Re-scrolling every read restarts the native
                    // row's asynchronous bind and can keep its control disabled.
                    bool scroll = !scrolled;
                    scrolled = true;
                    var adapter = ServerHandles.ResolveDropdownHandle(id, out var error, expectedContext, scroll);
                    if (adapter == null) return (error, acrossFrames);
                    if (adapter.IsDisabled()) return ("This control is not available right now.", acrossFrames);
                    adapter.ShowPopup();
                    _levels = new List<Level> { new Level { Adapter = adapter } };
                    _ownerId = id;
                    _conditionGuard = ServerHandles.CaptureConditionValidation(id);
                    _ownerContext = expectedContext;
                    _ownerScreen = ServerText.ScreenToken(InteractivePanels.Current());
                    return ((string)null, acrossFrames);
                });
                if (err == null || !retries || Environment.TickCount64 - start >= RealizeBudgetMs)
                    break;
                Thread.Sleep(FrameMs);
            }
            if (err != null) return err;

            if (!WaitForPopupWithReshow())
                return FailAndClose("The dropdown did not open.");

            Thread.Sleep(SettleMs);
            return ReadTopLevelAndBuildFragment();
        }
        catch (Exception ex) { return FailFromException(ex); }
    }

    // Like Open, but returns the in-game player card's inline panel (info rows as text,
    // each selectable sub-menu as its own lazy select) instead of a single select fragment.
    // The root session stays open so the sub-selects can /dropdown/expand their children.
    public static string OpenPanel(string cardId)
    {
        lock (SessionGate)
        try
        {
            string err = MainThreadDispatcher.Invoke<string>(() =>
            {
                CloseCurrentOnMainThread();
                var adapter = ServerHandles.ResolveDropdownHandle(cardId, out var error);
                if (adapter == null) return error;
                adapter.ShowPopup();
                _levels = new List<Level> { new Level { Adapter = adapter } };
                _ownerId = cardId;
                _conditionGuard = null;
                _ownerContext = null;
                _ownerScreen = ServerText.ScreenToken(InteractivePanels.Current());
                return null;
            });
            if (err != null) return err;

            if (!WaitForPopupWithReshow())
                return FailAndClose("The menu did not open.");

            Thread.Sleep(SettleMs);

            bool ready = false, scrollMode = false;
            for (int attempt = 0; attempt < ReadRetries && !ready; attempt++)
            {
                // Scroll mode rides the capture's dispatch: the capture just computed it, and a
                // separate Invoke to read it back costs a whole frame.
                var r = MainThreadDispatcher.Invoke(() =>
                    Adapters.WalkBudget.Run("TryCaptureTopLevel", MenuReadMaxElements, MenuReadMaxMs,
                                            () =>
                                            {
                                                int s = TryCaptureTopLevel();
                                                return (State: s, Scroll: s == 1 && (TopOrNull?.ScrollMode ?? false));
                                            }));
                if (r.State == SessionGone) return SessionGoneMessage;
                if (r.State == 0) return FailAndClose("No options found.");
                ready = r.State == 1;
                scrollMode = r.Scroll;
                if (!ready) Thread.Sleep(ReadRetryMs);
            }
            if (!ready) return FailAndClose("Could not read the menu.");

            if (scrollMode) HarvestTop(HarvestCap);

            return MainThreadDispatcher.Invoke(() =>
                Adapters.WalkBudget.Run("BuildPlayerPanel", MenuReadMaxElements, MenuReadMaxMs, () =>
                {
                    var l = TopOrNull;
                    if (l == null) return SessionGoneMessage;
                    var isInfo = l.Adapter.BuildIsInfoIndicatorArray();
                    var isDisabled = l.Adapter.BuildIsDisabledArray();
                    return DropdownHtml.BuildPlayerPanel(cardId, l.Choices, l.HasChildren, isInfo, isDisabled);
                }));
        }
        catch (Exception ex) { return FailFromException(ex); }
    }

    public static string Expand(string path, int index, string owner = null)
    {
        lock (SessionGate)
        try
        {
            string err = ValidateOwner(owner) ?? MainThreadDispatcher.Invoke<string>(() =>
            {
                var level = ResolveAndPop(path, out var error);
                if (level == null) return error;
                if (index < 0 || index >= level.TotalCount)
                    return "That option is no longer available. Refresh the screen.";
                if (index >= level.HasChildren.Length || !level.HasChildren[index])
                    return "That option has no submenu.";
                // A greyed-out choice (e.g. an individual shout on cooldown after a team shout) has
                // nothing to bind, so showing its submenu NREs in the game's BindChildRoot. Refuse
                // before any open — this is what keeps the disabled-menu crash from ever happening.
                // Names the choice the request actually landed on. The server maps expand indices
                // correctly (verified four levels deep through the World dropdown), so when a drill
                // opens the wrong submenu the divergence is upstream — in which option the companion
                // read as focused. This line is what tells the two apart from a single repro.
                Log($"expand path=\"{path}\" index={index} -> \"{(index < level.Choices.Count ? level.Choices[index] : "?")}\"");
                return null;
            });
            if (err != null) return err;

            var preparation = PrepareRequestedChoice(path, index);
            if (preparation != null) return preparation;
            var openError = MainThreadDispatcher.Invoke(() =>
            {
                var level = ResolveAndPop(path, out var error);
                error ??= _conditionGuard?.Invoke();
                error ??= level == null ? SessionGoneMessage : ChoiceAvailabilityError(level, index);
                if (error != null) return error;
                level.Adapter.TriggerChildOpen(index);
                return (string)null;
            });
            if (openError != null) return openError;

            bool adopted = WaitFor(() =>
            {
                var top = TopOrNull;
                if (top == null) return false;
                var child = top.Adapter.GetVisibleChildAdapter(index);
                if (child == null) return false;
                _levels.Add(new Level { Adapter = child, ParentIndex = index });
                return true;
            }, VisibleTimeoutMs, "adoptChild");
            if (!adopted) return "The submenu did not open.";

            Thread.Sleep(SettleMs);
            return ReadTopLevelAndBuildFragment();
        }
        catch (Exception ex) { return FailFromException(ex); }
    }

    public static string Collapse(string path, string owner = null)
    {
        lock (SessionGate)
        try
        {
            return ValidateOwner(owner) ?? MainThreadDispatcher.Invoke(() =>
            {
                var level = ResolveAndPop(path, out var error);
                return level == null ? error : "Submenu closed.";
            });
        }
        catch (Exception ex) { return ReportException(ex); }
    }

    public static string Select(string path, int index, string owner = null)
    {
        lock (SessionGate)
        try
        {
            string stale = ValidateOwner(owner);
            if (stale != null) return stale;
            var preparation = PrepareRequestedChoice(path, index);
            if (preparation != null) return preparation;
            bool deferredHide = false;
            var result = MainThreadDispatcher.Invoke(() =>
            {
                var conditionError = _conditionGuard?.Invoke();
                if (conditionError != null)
                {
                    CloseCurrentOnMainThread();
                    return (Label: (string)null, Err: conditionError, Defer: false, Now: (bool?)null);
                }
                var level = ResolveAndPop(path, out var error);
                if (level == null) return (Label: (string)null, Err: error, Defer: false, Now: (bool?)null);
                if (index < 0 || index >= level.Choices.Count)
                    return (Label: (string)null, Err: "That option is no longer available. Refresh the screen.", Defer: false, Now: (bool?)null);
                var availabilityError = ChoiceAvailabilityError(level, index);
                if (availabilityError != null)
                    return (Label: (string)null, Err: availabilityError, Defer: false, Now: (bool?)null);

                string label = level.Choices[index];
                // On a toggle list the game flips this one rule and leaves the rest alone
                // (SIDropdown.SelectingSameItemDeselects), so the outcome is the negation of the
                // state just read — which is what the reply must announce, because "selected" would
                // claim a commit even when the click turned the rule OFF.
                bool? nowChecked = level.Checked != null && index < level.Checked.Length
                    ? !level.Checked[index] : null;
                if (_levels.Count == 1)
                {
                    bool committed = level.Adapter.ConfirmSelection(index);
                    // A refused commit never reached the code that hides the popup, so the session
                    // must stand it down itself before forgetting about it. Otherwise the popup is
                    // left open and unbound with no session pointing at it, and the NEXT open puts a
                    // second popup over the orphan — the corruption HideEntireHierarchy exists to
                    // prevent, which surfaces as unrelated dropdowns misbehaving afterwards.
                    if (!committed) level.Adapter.HideEntireHierarchy();
                    _levels = null;
                    _ownerId = null;
                    _ownerContext = null;
                    _conditionGuard = null;
                    _ownerContext = null;
                    // The reply is the player's only evidence that the pick landed, so a refused
                    // commit must not come back as the label they chose.
                    if (!committed)
                    {
                        Speech.Degrade.Note($"dropdown choice {index} ({label}) could not be committed");
                        return (Label: (string)null,
                                Err: "That option could not be applied. Nothing was changed.",
                                Defer: false, Now: (bool?)null);
                    }
                    return (Label: label, Err: (string)null, Defer: false, Now: nowChecked);
                }
                level.Adapter.ConfirmLeafSelection(index);
                return (Label: label, Err: (string)null, Defer: true, Now: nowChecked);
            });
            if (result.Err != null) return result.Err;

            if (result.Defer)
            {
                // Mirror the speech handler's deferred hide: the selection event needs
                // frames to process before the hierarchy is torn down.
                Thread.Sleep(2 * FrameMs + 10);
                MainThreadDispatcher.Invoke<object>(() =>
                {
                    // Guarded because the sleep above is a window in which the session can be torn
                    // down by another dispatched action; dereferencing a null stack here throws on
                    // the main thread, which is the expensive freeze this whole path now avoids.
                    var root = _levels != null && _levels.Count > 0 ? _levels[0] : null;
                    root?.Adapter.HideEntireHierarchy();
                    _levels = null;
                    _ownerId = null;
                    _ownerContext = null;
                    _conditionGuard = null;
                    _ownerContext = null;
                    return null;
                });
                deferredHide = true;
            }
            // The client refreshes the page as soon as this response arrives, but the game needs
            // frames to propagate the selection through its bindings into the dropdown's label —
            // refreshing earlier re-renders the OLD value. ~9 frames covers the typical few-frame
            // propagation; a straggler self-corrects within one values tick (LiveValues pushes
            // the select's live value over SSE).
            Thread.Sleep(SelectionSettleMs);
            Log("select" + (deferredHide ? " (leaf)" : "") + ": " + result.Label);
            string outcome = result.Now switch { true => " checked", false => " unchecked", _ => " selected" };
            return DropdownHtml.StripRichText(result.Label) + outcome;
        }
        catch (Exception ex) { return FailFromException(ex); }
    }

    /// <summary>
    /// Stand an open popup down before an unrelated control is activated. Request thread.
    ///
    /// The mod opens a popup by calling ShowPopup on the widget directly, which the widget's OWN
    /// open/closed state never learns about. Activating that widget while its popup is up therefore
    /// does not toggle it shut as a mouse click would — the game opens a second popup over the
    /// orphaned first, and tearing that down takes the process with it: an IL2CPP fail-fast, no
    /// exception, nothing in the log (see docs/reference/failure-handling.md). Reproduced by clicking
    /// the Tactics "Quick Pick" button while its own dropdown was opening.
    ///
    /// Taking <see cref="SessionGate"/> also serialises the activation against an Open still in
    /// flight, which is the case that actually crashed: the click landed between two of Open's
    /// main-thread steps. A click that waits for the session and finds the popup already closed is
    /// the safe ordering, verified live.
    /// </summary>
    public static void CloseBeforeActivation()
    {
        lock (SessionGate)
        {
            if (_levels == null) return;
            try
            {
                MainThreadDispatcher.Invoke(() => { CloseCurrentOnMainThread(); return true; });
            }
            catch (Exception ex)
            {
                // The activation still goes ahead: refusing to act on a control because a popup
                // could not be closed would strand the player on a page whose buttons do nothing.
                // Logged because it means the next activation is running the risk described above.
                ServerText.LogBackground($"[DropdownSession] could not close the open popup before an activation: {ex.Message}");
            }
        }
    }

    public static string Close(string owner = null)
    {
        lock (SessionGate)
        try
        {
            // A close aimed at a session that is no longer the live one must not tear down the
            // session that replaced it — the caller's popup already closed when the replacement
            // opened, so there is nothing left to do.
            if (!string.IsNullOrEmpty(owner) && _levels != null && owner != _ownerId)
                return "Dropdown closed.";
            return MainThreadDispatcher.Invoke(() =>
            {
                CloseCurrentOnMainThread();
                return "Dropdown closed.";
            });
        }
        catch (Exception ex) { return ReportException(ex); }
    }

    public static string More(string owner = null)
    {
        lock (SessionGate)
        try
        {
            string stale = ValidateOwner(owner);
            if (stale != null) return stale;
            if (_levels == null) return "No dropdown is open. Refresh the screen.";
            int before = MainThreadDispatcher.Invoke(() => TopOrNull?.Choices.Count ?? -1);
            if (before < 0) return SessionGoneMessage;
            HarvestTop(before + HarvestCap);
            return MainThreadDispatcher.Invoke(() =>
            {
                var l = TopOrNull;
                if (l == null) return SessionGoneMessage;
                return DropdownHtml.BuildMoreItems(l.Choices, before,
                    l.HasChildren, l.SelectedIndex, l.TotalCount, l.Checked, l.Disabled);
            });
        }
        catch (Exception ex) { return FailFromException(ex); }
    }

    /// <summary>
    /// Close any active session. MUST be called on the main thread (inside Invoke) —
    /// the server calls this at the start of every non-dropdown action and body build
    /// so the game is never left with an orphaned popup.
    /// </summary>
    public static void CloseCurrentOnMainThread()
    {
        // The two popup session types share one teardown chokepoint: every "any other action"
        // call site closes both, so the game can never be left with an orphaned search popup.
        SearchDropdownSession.CloseOnMainThread();
        if (_levels == null) return;
        try { _levels[0].Adapter.HideEntireHierarchy(); }
        catch (Exception ex) { Speech.Log.Warn($"[DropdownSession] Hide on close failed: {ex.Message}"); }
        _levels = null;
        _ownerId = null;
        _ownerScreen = null;
        _conditionGuard = null;
        _ownerContext = null;
    }

    // --- Level reading (request thread orchestration) ---

    private static string ReadTopLevelAndBuildFragment()
    {
        // Async row binding can lag the popup becoming visible; retry empty reads. For a fully
        // rendered list the fragment is built inside the capture's own dispatch — the state it
        // needs is in hand there, and a separate Invoke costs a whole frame. A virtualized list
        // still harvests between dispatches and builds in its own.
        for (int attempt = 0; attempt < ReadRetries; attempt++)
        {
            var r = MainThreadDispatcher.Invoke(() =>
            {
                var current = TopOrNull;
                if (current == null) return (State: SessionGone, Fragment: (string)null);
                // A recycled native anchor can close the popup AFTER its first
                // visible frame, while its option rows are still binding. Reuse
                // opening recovery within this existing bounded read loop. Resolve
                // the same current handle again; never reuse a recycled control or
                // reopen after a screen change/explicit session cancellation.
                if (!current.Adapter.IsPopupVisible())
                {
                    // The registered handle owns the root, not a child submenu.
                    // Never replace a vanished child with its parent's options.
                    if (_levels.Count != 1)
                        return (State: -2, Fragment: "The submenu closed before its options were ready. Close and reopen the dropdown.");
                    var stale = ServerHandles.ValidateScreen(_ownerScreen);
                    if (stale != null) return (State: -2, Fragment: stale);
                    var adapter = ServerHandles.ResolveDropdownHandle(_ownerId, out var error, _ownerContext);
                    if (adapter == null) return (State: -2, Fragment: error);
                    if (adapter.IsDisabled()) return (State: -2, Fragment: "This control is not available right now.");
                    current.Adapter = adapter;
                    adapter.ShowPopup();
                    Log("Root popup closed while options were binding; reopened the validated current control.");
                    return (State: 2, Fragment: (string)null);
                }
                int state = TryCaptureTopLevel();
                if (state != 1) return (State: state, Fragment: (string)null);
                var l = TopOrNull;
                if (l == null) return (State: SessionGone, Fragment: (string)null);
                if (l.ScrollMode) return (State: 1, Fragment: (string)null);
                return (State: 1, Fragment: DropdownHtml.BuildOptionsFragment(l.Choices,
                    l.HasChildren, l.SelectedIndex, l.TotalCount, l.Checked, l.Disabled));
            });
            if (r.State == -2) return FailAndClose(r.Fragment);
            if (r.State == SessionGone) return SessionGoneMessage;
            if (r.State == 0) return FailAndClose("No options found.");
            if (r.State == 1)
            {
                if (r.Fragment != null) return r.Fragment;
                HarvestTop(HarvestCap);
                return MainThreadDispatcher.Invoke(() =>
                {
                    var l = TopOrNull;
                    if (l == null) return SessionGoneMessage;
                    return DropdownHtml.BuildOptionsFragment(l.Choices,
                        l.HasChildren, l.SelectedIndex, l.TotalCount, l.Checked, l.Disabled);
                });
            }
            Thread.Sleep(ReadRetryMs);
        }
        try
        {
            MainThreadDispatcher.Invoke(() =>
            {
                var level = TopOrNull;
                if (level != null)
                {
                    var element = level.Adapter.BackingElement;
                    Log($"Unbound options: anchor={element.worldBound} panel={element.panel != null} visible={ElementBridge.IsVisibleInHierarchy(element)} popup={level.Adapter.IsPopupVisible()}\n{level.Adapter.DescribeActiveItems()}");
                }
                return true;
            });
        }
        catch (Exception ex) { Log("unbound-options diagnostic failed: " + ex.GetType().Name); }
        return FailAndClose("Could not read the dropdown options.");
    }

    // Main thread. Returns 0 = genuinely empty (fail), 1 = captured, 2 = not ready yet.
    // Which of the reads below is holding the main thread. A budget bounds work spread across many
    // steps but cannot interrupt one game call that blocks inside the game, so knowing whether the
    // time is distributed or concentrated is the difference between "cap the walk" and "do not make
    // this call here at all". Logged only past the threshold, so an ordinary capture stays quiet.
    private const long SlowCaptureMs = 250;

    /// <summary>TryCaptureTopLevel: the session was torn down under us. Distinct from "no options".</summary>
    private const int SessionGone = -1;

    // The same wording ValidateOwner already gives for acting on a session that is not there: the
    // player's menu is gone and the page they are reading is stale, whichever way it went.
    private const string SessionGoneMessage = "No dropdown is open. Refresh the screen.";

    // A player-card menu is a handful of rows of a few dozen elements each. The element ceiling sits
    // far above that so an ordinary read never approaches it; the time ceiling is the one that
    // matters, because it bounds the freeze in the units the player actually feels it in.
    private const int MenuReadMaxElements = 10000;
    private const int MenuReadMaxMs = 750;

    private static int TryCaptureTopLevel()
    {
        // Stage and exception are recorded independently of the timings. Two reproductions reported
        // every step as 0ms against a multi-second total — a self-contradiction that means the
        // arithmetic cannot be trusted to say where the time went. A counter bumped after each step
        // says how far execution actually got, and survives an exception thrown mid-step.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long tTop = 0, tCount = 0, tRead = 0, tChildren = 0, tSelected = 0, tRows = 0, tChecked = 0;
        int stage = 0;
        Exception failed = null;
        try
        {
        var l = TopOrNull;
        if (l == null) return SessionGone;
        stage = 1;
        tTop = watch.ElapsedMilliseconds;
        int total = l.Adapter.GetChoicesCount();
        stage = 2;
        tCount = watch.ElapsedMilliseconds - tTop;
        var choices = l.Adapter.ReadChoices();
        stage = 3;
        tRead = watch.ElapsedMilliseconds - tCount;
        if (choices.Count == 0)
            return total == 0 ? 0 : 2;

        l.TotalCount = total;
        l.HasChildren = l.Adapter.BuildHasChildrenArray();
        stage = 4;
        tChildren = watch.ElapsedMilliseconds - tCount - tRead;
        l.SelectedIndex = l.Adapter.GetSelectedIndex();
        stage = 5;
        tSelected = watch.ElapsedMilliseconds - tCount - tRead - tChildren;
        l.ScrollMode = total > choices.Count;
        // Some dropdowns (the tactics role pickers) leave SelectedIndex at -1 and mark the current
        // choice with a tick sprite in its row instead. Recover it from the rendered rows so the
        // active option still reads as selected. Non-virtualized lists only, where every row is
        // rendered and the row order matches the choices list.
        if (l.SelectedIndex < 0 && !l.ScrollMode)
            l.SelectedIndex = l.Adapter.GetSelectedIndexFromRows();
        stage = 6;
        tRows = watch.ElapsedMilliseconds - tCount - tRead - tChildren - tSelected;
        // Toggle-list state needs every row rendered to line up with the choices, so a virtualized
        // list declines it and reads as an ordinary dropdown rather than mislabelling a windowed
        // subset. Both known toggle lists are far too short to virtualize.
        if (!l.ScrollMode)
            l.Checked = l.Adapter.BuildIsCheckedArray();
        stage = 7;
        tChecked = watch.ElapsedMilliseconds - tCount - tRead - tChildren - tSelected - tRows;
        if (l.ScrollMode)
        {
            // Virtualized: the DOM rows are an unknown window. Discard them and
            // harvest by absolute index instead.
            l.Choices = new List<string>();
            l.Disabled = new List<bool>();
            l.HarvestedTo = 0;
        }
        else
        {
            l.Choices = choices;
            l.Disabled = new List<bool>(l.Adapter.BuildIsDisabledArray());
            if (l.Disabled.Count != choices.Count)
                throw new InvalidOperationException("Native dropdown labels and availability are not aligned.");
            l.HarvestedTo = choices.Count;
        }
        stage = 8;
        return 1;
        }
        catch (Exception ex) { failed = ex; throw; }
        finally
        {
            long total = watch.ElapsedMilliseconds;
            if (total >= SlowCaptureMs)
                Log($"SLOW capture {total}ms stage={stage} failed={failed?.GetType().Name ?? "-"}"
                  + $" ({failed?.Message ?? "no exception"}): top={tTop} count={tCount} readChoices={tRead}"
                  + $" hasChildren={tChildren} selected={tSelected} rowsTick={tRows} checked={tChecked}");
        }
    }

    // Harvests a virtualized list by scrolling the render window to the harvest frontier
    // and reading the WHOLE window each step, not one row at a time. A 100+ item list
    // (e.g. the 121-language picker) then costs ~one scroll-settle per window instead of
    // per row, dropping the open from seconds to well under one — which matters because
    // the server's single accept loop serializes every other request behind this work.
    private static void HarvestTop(int upTo)
    {
        int stalls = 0;
        while (true)
        {
            int frontier = MainThreadDispatcher.Invoke(() =>
            {
                var l = TopOrNull;
                if (l == null) return -1;
                if (!l.ScrollMode || l.HarvestedTo >= Math.Min(l.TotalCount, upTo)) return -1;
                // On a stall retry, over-scroll to the very end: the frontier scroll can be
                // clamped by a stale content-height estimate that refreshes as rows realize.
                l.Adapter.ScrollToChoice(stalls == 0 ? l.HarvestedTo : l.TotalCount - 1);
                return l.HarvestedTo;
            });
            if (frontier < 0) break;

            Thread.Sleep(stalls == 0 ? 2 * FrameMs + 10 : 8 * FrameMs);
            bool progressed = MainThreadDispatcher.Invoke(() =>
            {
                var l = TopOrNull;
                if (l == null) return true;
                var window = l.Adapter.ReadRealizedWindow(out var disabled);
                int added = DropdownHarvest.AppendContiguous(l.Choices, l.Disabled, window, disabled);
                l.HarvestedTo = l.Choices.Count;
                if (added == 0)
                    Log($"harvest stall {stalls} at index {l.HarvestedTo}: {l.Adapter.DescribeActiveItems()}");
                return added > 0;
            });

            // The list was just scrolled to the frontier, so the frontier row should be
            // realized. Persistent no-progress means it is unreachable or unreadable:
            // crash rather than stall forever or silently truncate the choices.
            if (progressed) { stalls = 0; continue; }
            if (++stalls >= 3)
            {
                var state = MainThreadDispatcher.Invoke(() =>
                {
                    var l = TopOrNull;
                    return l == null ? (Harvested: 0, Total: 0) : (Harvested: l.HarvestedTo, Total: l.TotalCount);
                });
                throw new InvalidOperationException($"Could not read option {state.Harvested + 1} of {state.Total}.");
            }
        }
    }

    // --- Path handling (main thread) ---

    private static string ChoiceAvailabilityError(Level level, int index)
    {
        if (!level.Adapter.TryReadChoiceDisabled(index, out var disabled))
            return "The option row is not ready. Close and reopen the dropdown.";
        return disabled ? "This option is not available right now." : null;
    }

    // Realize an offscreen option once, then observe binding across frames. Do
    // not restart its asynchronous binding by scrolling on every observation.
    // Only the caller's later guarded main-thread dispatch may invoke a command.
    private static string PrepareRequestedChoice(string path, int index)
    {
        string error = null;
        bool scrolled = false;
        bool ready = WaitFor(() =>
        {
            var level = ResolveAndPop(path, out error);
            error ??= _conditionGuard?.Invoke();
            if (level == null || error != null) return true;
            if (index < 0 || index >= level.Choices.Count)
            {
                error = "That option is no longer available. Refresh the screen.";
                return true;
            }
            if (level.Adapter.TryReadChoiceDisabled(index, out var disabled))
            {
                if (disabled) error = "This option is not available right now.";
                return true;
            }
            if (!scrolled)
            {
                scrolled = true;
                level.Adapter.ScrollToChoice(index);
            }
            return false;
        }, VisibleTimeoutMs, "realizeOptionRow");
        return error ?? (ready ? null : "The option row is not ready. Close and reopen the dropdown.");
    }

    // Resolves a level path against the live stack and pops any deeper levels
    // (hiding their popups). Returns the level, or null with an error message.
    private static Level ResolveAndPop(string path, out string error)
    {
        error = null;
        if (_levels == null)
        {
            error = "No dropdown is open. Refresh the screen.";
            return null;
        }

        var parts = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split('.');
        int target = parts.Length;
        if (target >= _levels.Count)
        {
            error = "Stale dropdown state. Close and reopen the dropdown.";
            return null;
        }
        for (int k = 0; k < parts.Length; k++)
        {
            if (!int.TryParse(parts[k], out var idx) || _levels[k + 1].ParentIndex != idx)
            {
                error = "Stale dropdown state. Close and reopen the dropdown.";
                return null;
            }
        }

        while (_levels.Count - 1 > target)
        {
            var child = _levels[_levels.Count - 1];
            _levels.RemoveAt(_levels.Count - 1);
            child.Adapter.HideEntireHierarchy();
        }
        return _levels[target];
    }

    // --- Helpers ---

    // Wait for the just-opened session's popup, re-invoking ShowPopup when a wait slice passes
    // with nothing visible (the swallowed-first-invoke case; see VisibleTimeoutMs). Re-invoking
    // only after a full quiet slice keeps a merely slow open from being double-shown.
    private static bool WaitForPopupWithReshow()
    {
        long start = Environment.TickCount64;
        if (WaitFor(() => TopOrNull?.Adapter.IsPopupVisible() ?? false, ShowWaitMs, "popupVisible")) return true;
        while (Environment.TickCount64 - start < VisibleTimeoutMs)
        {
            Log("popup not visible after ShowPopup, re-invoking");
            MainThreadDispatcher.Invoke<object>(() => { TopOrNull?.Adapter.ShowPopup(); return null; });
            if (WaitFor(() => TopOrNull?.Adapter.IsPopupVisible() ?? false, ShowWaitMs, "popupVisibleReshow")) return true;
        }
        return false;
    }

    // <paramref name="what"/> names the call site in the dispatcher's slow-action warning. All three
    // callers share the Invoke below, so the default caller/line label collapses them into one
    // indistinguishable "WaitFor:551" — which is exactly what a 31.7s main-thread hold reported, and
    // exactly what could not then be traced back to a condition. The level count rides along because
    // a stale session (a level stack outliving the panel that built it) is the leading explanation for
    // a condition that blocks instead of answering.
    private static bool WaitFor(Func<bool> conditionOnMainThread, int timeoutMs, string what)
    {
        // Wall-clock, not sleep-count: each Invoke costs a frame (more on a busy one) on top of
        // the sleep, so charging only the sleep stretched every nominal timeout to ~2x or worse —
        // which turned ShowWaitMs's "one quiet slice before the reshow" into multiple seconds.
        long start = Environment.TickCount64;
        while (true)
        {
            string label = $"{what}/L{_levels?.Count ?? -1}";
            if (MainThreadDispatcher.Invoke(conditionOnMainThread, caller: label)) return true;
            if (Environment.TickCount64 - start >= timeoutMs) return false;
            Thread.Sleep(FrameMs);
        }
    }

    // Dev probe: describe the open popup's realized rows (see /ddwindow).
    internal static string DescribeOpenTop()
    {
        lock (SessionGate)
            return MainThreadDispatcher.Invoke(() =>
            {
                var l = TopOrNull;
                return l == null ? "no open session\n" : l.Adapter.DescribeActiveItems();
            });
    }

    private static string FailAndClose(string message)
    {
        try
        {
            MainThreadDispatcher.Invoke<object>(() => { CloseCurrentOnMainThread(); return null; });
        }
        catch (Exception ex) { Log("close after failure also failed: " + ex.Message); }
        Log(message);
        return message;
    }

    // Logging is safe from any thread (Speech.Log marshals centrally), so these do not marshal.
    // Marshalling here was worse than redundant: a caller already on the main thread — anything
    // logging from inside a dispatched action — enqueued a message and then blocked waiting for the
    // main thread to drain it, which cannot happen while it is that thread. Every such call burned
    // the full Invoke timeout and then lost its message to the catch, so the code paths most worth
    // seeing were the ones that logged nothing.
    private static void Log(string msg) => Speech.Log.Info("[DropdownSession] " + msg);

    private static void LogError(string msg) => Speech.Log.Error("[DropdownSession] " + msg);

    // The game-side dropdown work runs through reflection (OnSelectionChangedInternal,
    // ShowDropdown), so a failure inside the game surfaces here as a TargetInvocationException
    // whose own Message is the useless "Exception has been thrown by the target of an
    // invocation." Walk the whole InnerException chain so the real failure (type, message,
    // and stack) is what lands in the log.
    private static string Diagnose(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append("\n --> ");
            sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            if (!string.IsNullOrEmpty(e.StackTrace)) sb.Append('\n').Append(e.StackTrace);
        }
        return sb.ToString();
    }

    // Catch-site helper for the operations that close on failure: logs the unwrapped exception
    // (tagged with the calling operation), tears down the session, returns a speakable message.
    private static string FailFromException(Exception ex, [CallerMemberName] string op = null)
    {
        LogError(op + " failed: " + Diagnose(ex));
        // A walk that ran out of budget did not fail to find the menu — it stopped part-way through
        // reading one, deliberately, so the read could not go on holding the main thread. The player
        // hears the same thing they hear for any unreadable menu; what makes this honest rather than
        // a guess is that the mod publishes nothing it did not finish reading.
        if (ex is Adapters.WalkBudgetExceededException || ex.InnerException is Adapters.WalkBudgetExceededException)
            return FailAndClose("Could not read the menu.");
        return FailAndClose("Something went wrong with the dropdown. Refresh the screen.");
    }

    // Catch-site helper for the operations that do not close the session (Collapse, Close):
    // logs the unwrapped exception and returns a speakable message.
    private static string ReportException(Exception ex, [CallerMemberName] string op = null)
    {
        LogError(op + " failed: " + Diagnose(ex));
        return "Something went wrong with the dropdown. Refresh the screen.";
    }
}

using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Per-screen registry of elements whose visible text is refreshed in place between body
/// renders (the generalised score-line pattern). Owns the single id pool for the screen —
/// shared with <see cref="ServerHandles"/> so interactive and read-only ids never collide —
/// and a map of id to the live element behind it.
///
/// Tracks leaf text and native controls with explicit local value patchers
/// (dropdowns, sliders and checked inputs). Non-leaf labels are not replaced.
/// Cleared and rebuilt on every body render.
/// </summary>
internal static class LiveValues
{
    private static readonly IdRegistry _ids = new();
    private static readonly Dictionary<string, IAccessibleElement> _sources = new();
    // These are managed adapter instances from THIS rendered snapshot, not
    // native object identities. Keep the original source for descriptions,
    // detachment checks and action ownership; tag only its display composition.
    private static readonly HashSet<IAccessibleElement> _contractValueSources = new();

    internal static void Clear()
    {
        _ids.Clear();
        _sources.Clear();
        _contractValueSources.Clear();
        _pendingSubjectHeals.Clear();
    }

    internal static string AllocateId(string key) => _ids.Allocate(key);

    // Marks an already-rendered element (with an existing id) as a live-value source.
    internal static void Track(string id, IAccessibleElement el) => _sources[id] = el;

    internal static void TrackContractValue(string id, IAccessibleElement el, bool valueNamed)
    {
        Track(id, el);
        if (valueNamed) _contractValueSources.Add(el);
    }

    // Read-only text/headings share the current snapshot's id pool but have no
    // action handle. Alt+I must still resolve their live native source.
    internal static bool TryGetSource(string id, out IAccessibleElement source)
        => _sources.TryGetValue(id, out source);

    // Allocates an id and tracks an element that has no action handle (heading, progress
    // bar, plain text). Returns the id to stamp on the rendered element.
    internal static string RegisterReadOnly(IAccessibleElement el)
    {
        var id = AllocateId(ServerHandles.KeyFor(el));
        Track(id, el);
        return id;
    }

    // Reads the current display text of every tracked element, live from the game. Main
    // thread only (DisplayText reads the live element). Consumed by the SSE loop, which
    // diffs successive snapshots via ValueDiff.
    //
    // Per-element fault isolation: a single unreadable element (destroyed/detached after it
    // was tracked at the last /body render) is dropped and untracked rather than aborting the
    // whole snapshot. The value channel is a best-effort overlay on the authoritative /body
    // render, so a dropped element keeps showing its last-rendered text (correct at render
    // time) until the next refresh; never speaking wrong data. Crucially this means a dead
    // element can no longer make this method throw, which is what previously discarded the
    // freshly read screen token and blinded navigation detection. Untracking stops the
    // per-tick re-failing; the map rebuilds from scratch on the next /body render.
    // A dropdown is rendered as a <select> whose shown value is its current SELECTION — GetState,
    // the same text the renderer stamps into the option — not the name-plus-state DisplayText.Of
    // returns for a button or heading. Reading the selection here lets the value channel push it
    // and the client patch the select in place when it changes on the same screen; otherwise a
    // committed dropdown value only appears after a full re-render (F5), because the value
    // fast-path patches text nodes and deliberately skips <select> elements. A searchable
    // dropdown is the same shape as an <input>: its GetState is the committed box text, pushed
    // so the client can patch the input's value in place.
    // Clean (strip Unity rich-text markup) but do NOT HTML-escape, matching DisplayText.Of and the
    // value channel's contract: the client sets the text as the option's textContent (literal). The
    // renderer instead EscapeText's the same state, so the displayed values still match for diffing.
    // Without the clean, a club-name dropdown leaks its raw markup
    // (<color=...><link=...><style=...>CF Montréal</style>...) into the select.
    // A button reads through HtmlRenderer.ButtonDisplay - DisplayText.Of plus the "locked" state
    // token - because that is the exact text AppendButton rendered. Reading bare DisplayText.Of
    // here made a late-populating padlock invisible to the diff (the token was appended only at
    // render time), so the tile showed unlocked until a manual full refresh.
    private static string ReadValue(IAccessibleElement el)
    {
        string role = ServerText.SafeRole(el);
        // History commands can restore a toggle after the action's body render.
        // Push its checked state locally, keeping the inherited input and focus.
        if (role is "checkbox" or "toggle" or "radio button") return ServerText.State(el);
        if (role is "dropdown" or "searchable dropdown")
        {
            var state = ServerText.State(el);
            if (_contractValueSources.Contains(el))
                state = ContractTermsText.ComposeValueOption(ServerText.Label(el), state);
            return ServerText.Clean(state);
        }
        if (role == "button") return HtmlRenderer.ButtonDisplay(el);
        if(role=="progress bar")
            return System.Text.Json.JsonSerializer.Serialize(new{
                label=ServerText.Clean(ServerText.RenameOr(el,ServerText.Label(el))),
                state=ServerText.State(el),text=DisplayText.Of(el)});
        if(el is Adapters.SliderAdapter slider)
        {
            var range=slider.GetRange();
            return System.Text.Json.JsonSerializer.Serialize(new{value=range.Value,min=range.Low,max=range.High,step=range.Step,text=slider.GetValueText()});
        }
        return DisplayText.Of(el);
    }

    internal static Dictionary<string, string> Snapshot()
    {
        var snap = LiveSnapshot.Build(_sources, ReadValue, out var failures);
        if (failures != null)
            foreach (var f in failures)
            {
                _sources.TryGetValue(f.Key, out var el);
                // An element that left the live tree (panel change, streamed-list recycling)
                // is routine teardown between renders — the map rebuilds on the next /body
                // render, so drop quietly. Only an ATTACHED element failing its read is a
                // defect worth a warning.
                if (IsDetached(el))
                    Log.Debug($"[LiveValues] Dropping tracked element '{f.Key}': left the live tree.");
                else
                {
                    var role = el != null ? ServerText.SafeRole(el) : "?";
                    Log.Warn($"[LiveValues] Dropping unreadable tracked element '{f.Key}' ({role}): {f.Error.Message}");
                }
                _sources.Remove(f.Key);
                _contractValueSources.Remove(el);
            }
        DropStaleMarkerSources(snap);
        NoteSubjectHeals(snap);
        return snap;
    }

    // Tracked message-subject cells still reading as the unreadable marker, by id, with the tick
    // they were first seen on it (since the last body render — Clear wipes this with the map).
    private static readonly Dictionary<string, long> _pendingSubjectHeals = new();
    private static int _subjectHealLogs;

    // The unreadable-subject field bug's heal (MessageSubjectValue pushing the real subject once
    // the game's bind lands) is otherwise silent, so a field Player.log could not distinguish
    // "healed late, quietly" from "the bind never landed". Log the first few marker-to-text
    // transitions with their latency; their ABSENCE in a log that shows the compose degrade is
    // itself the finding. Capped so a big inbox healing at once cannot spam.
    private static void NoteSubjectHeals(Dictionary<string, string> snap)
    {
        foreach (var pair in snap)
        {
            if (!_sources.TryGetValue(pair.Key, out var el) || el is not Adapters.MessageSubjectValue)
                continue;
            if (pair.Value == Degrade.Unreadable)
            {
                if (!_pendingSubjectHeals.ContainsKey(pair.Key))
                    _pendingSubjectHeals[pair.Key] = System.Environment.TickCount64;
            }
            else if (_pendingSubjectHeals.Remove(pair.Key, out var since) && _subjectHealLogs < 5)
            {
                _subjectHealLogs++;
                Log.Info($"[LiveValues] message subject heal landed {System.Environment.TickCount64 - since}ms after tracking ('{pair.Key}').");
            }
        }
    }

    // Sources whose native element left the tree or whose captured inline-link
    // content/record changed. Written on the main thread (Snapshot), consumed by
    // the SSE thread; reuse the same bounded recompose path for both cases.
    private static int _staleDrops;

    /// <summary>Number of stale sources dropped since the last call, and resets the count.</summary>
    internal static int TakeStaleDrops() => System.Threading.Interlocked.Exchange(ref _staleDrops, 0);

    // A tracked element that reads as the unlabeled degrade marker is either mid-bind (transient,
    // and the value channel's MarkerHold rides it out) or a corpse: a panel that rebuilds its
    // controls in place — the tunnel interview and press conference recreate their answer buttons
    // per question — leaves this map pointing at the elements it replaced. A corpse keeps reading
    // as the marker forever, so the channel pushed the marker over the page's correct labels and
    // never had anything better to push; only a fresh /body re-registered the live elements, which
    // is exactly why F5 was the only cure.
    //
    // Reading it is worse than useless, so drop and untrack it, and count the drop so the loop can
    // refetch — the map must be rebuilt against the live elements before this screen has honest
    // values again. Detachment is only probed for entries already reading as the marker (rare), so
    // an ordinary tick pays nothing extra.
    private static void DropStaleMarkerSources(Dictionary<string, string> snap)
    {
        List<string> stale = null;
        foreach (var pair in snap)
        {
            if (!_sources.TryGetValue(pair.Key, out var el)) continue;
            bool changedLink = el is Adapters.SITextLink.Part part && !part.IsCurrent
                || el is Adapters.SITextLink.PendingText pending && pending.IsReady;
            // Both degrade markers a tracked source can read as: the unlabeled control marker,
            // and the unreadable marker a mid-bind message subject cell is tracked with. A
            // detached source stuck on either can never push anything better, so both earn the
            // drop-and-refetch.
            if (!changedLink)
            {
                if (pair.Value != Adapters.ButtonAdapter.UnlabeledIconButton
                    && pair.Value != Degrade.Unreadable) continue;
                if (!IsDetached(el)) continue;
            }
            (stale ??= new List<string>()).Add(pair.Key);
        }
        if (stale == null) return;

        foreach (var key in stale)
        {
            snap.Remove(key);
            if (_sources.TryGetValue(key, out var source)) _contractValueSources.Remove(source);
            _sources.Remove(key);
        }
        System.Threading.Interlocked.Add(ref _staleDrops, stale.Count);
        Log.Debug($"[LiveValues] Dropped {stale.Count} stale source(s): the native elements or captured links changed; refetching.");
    }

    private static bool IsDetached(IAccessibleElement el)
    {
        if (el is not Adapters.PluginAdapterBase a) return false;
        // A throw here means the native element itself is gone — the strongest form of
        // detached; the caller logs the drop either way, so nothing is swallowed.
        try { return a.BackingElement.panel == null; }
        catch { return true; }
    }
}

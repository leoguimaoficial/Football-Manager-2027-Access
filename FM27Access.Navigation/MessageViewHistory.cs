using System.Collections.Generic;
using System.Linq;

namespace FM27Access.Navigation;

/// <summary>
/// The full-screen Message view as a real entry in the player's back/forward history.
/// See docs/message-view-history-plan.md.
///
/// An opened email is mod state the game's history knows nothing about, so without this, Back from a
/// screen opened out of an email lands on the bare inbox. The fix models the email as a virtual entry
/// pinned immediately above its own inbox in the game's stack, keeping the merged sequence linear:
///
///   … [N-1, 0] , [N, 0] = inbox , [N, 1] = email , [N+1, 0] = attachment …
///
/// This class holds the slots (one per game history index that has an email entry) and answers what
/// a Back or Forward press means. It is pure: the game-coupled parts — reading the stack, closing and
/// reopening the view, verifying the reading pane — live in the plugin, which drives this. Nothing
/// here is ever spoken or rendered: <see cref="Slot.Subject"/> is a comparison key checked against a
/// live read before the view reopens, never a substitute for reading the message.
///
/// Main thread only. Every mutation reaches this from a panel callback or from inside the server's
/// main-thread dispatch, so there is no lock.
/// </summary>
public sealed class MessageViewHistory
{
    /// <summary>The shared instance. Lives in this assembly, so slots survive a plugin /reload.
    /// Tests construct their own instances instead of resetting this one.</summary>
    public static MessageViewHistory Current { get; } = new MessageViewHistory();

    /// <summary>Backstop against unbounded growth if pruning ever misses: the oldest slot is
    /// dropped past this. A player is realistically one or two emails deep, never eight.</summary>
    private const int MaxSlots = 8;

    private const string ShapeSeparator = ", ";

    /// <summary>
    /// One email entry, pinned to the game history index whose inbox it belongs to.
    /// <see cref="WasOpen"/> records whether the player was still inside the email when they last
    /// left this index — the difference between "navigated away from the email" (Back should bring
    /// it back) and "stepped down to the inbox first" (the entry above is stale once they navigate
    /// on, exactly as a browser clears forward history).
    /// </summary>
    public readonly struct Slot
    {
        public Slot(int index, string subject, string shapePrefix, bool wasOpen)
        {
            Index = index;
            Subject = subject;
            ShapePrefix = shapePrefix;
            WasOpen = wasOpen;
        }

        public int Index { get; }

        /// <summary>The reading pane's subject when the view opened. A verification key only: the
        /// restore compares it against a live read and gives up honestly if it does not match.</summary>
        public string Subject { get; }

        /// <summary>The game's history-string segments up to and including <see cref="Index"/> as
        /// they were when the email opened. A mismatch means this stack position is no longer the
        /// screen the email belonged to, so the slot is dropped rather than trusted.</summary>
        public string ShapePrefix { get; }

        public bool WasOpen { get; }

        public Slot WithWasOpen(bool wasOpen) => new Slot(Index, Subject, ShapePrefix, wasOpen);
    }

    /// <summary>What a Back or Forward press means in the merged sequence.</summary>
    public enum Step
    {
        /// <summary>Walk the game's own history — click its Back/Forward button as before.</summary>
        GameMove,

        /// <summary>Step down from the email to its inbox. No game move: the email is a mod entry
        /// sitting above the same game node.</summary>
        CloseModal,

        /// <summary>Step up from the inbox into the email recorded at this index. No game move.</summary>
        RestoreModal,
    }

    private readonly Dictionary<int, Slot> _slots = new Dictionary<int, Slot>();

    // The stack as of the last Observe, for push detection. Back and forward leave the game's
    // history string byte-identical and only move the index, so any change to the string means a
    // node was added, truncated or cleared.
    private string _lastShape;
    private int _lastIndex = -1;

    public int SlotCount => _slots.Count;

    public Slot? SlotAt(int index)
        => _slots.TryGetValue(index, out var slot) ? slot : (Slot?)null;

    /// <summary>
    /// Fold the live stack in and prune slots that no longer describe it. Call this whenever a panel
    /// loads and before acting on a Back or Forward, so a stale entry can never survive into a
    /// restore. Pruning is the "browser clears forward history" rule plus two sanity gates:
    ///   - the history string changed (a node was pushed, truncated or cleared), so the slot at the
    ///     position we were on dies unless the player was still inside its email when they left;
    ///   - a slot outside the stack (including the index=-1, count=0 state a save load leaves) dies;
    ///   - a slot whose recorded shape prefix no longer matches the live stack dies.
    /// </summary>
    public void Observe(int index, int count, string shape)
    {
        shape = shape ?? "";

        if (_lastShape != null && shape != _lastShape && _slots.TryGetValue(_lastIndex, out var left) && !left.WasOpen)
            _slots.Remove(_lastIndex);

        if (_slots.Count > 0)
        {
            var dead = _slots.Values
                .Where(s => s.Index < 0 || s.Index >= count || PrefixOf(shape, s.Index) != s.ShapePrefix)
                .Select(s => s.Index)
                .ToList();
            foreach (var i in dead)
                _slots.Remove(i);
        }

        _lastShape = shape;
        _lastIndex = index;
    }

    /// <summary>The email view just opened at this stack position: record its entry.</summary>
    public void NoteOpened(int index, string shape, string subject)
    {
        var prefix = PrefixOf(shape ?? "", index);
        if (prefix == null) return;   // the index is not in the stack we were handed; record nothing

        _slots[index] = new Slot(index, subject ?? "", prefix, wasOpen: true);
        TrimToCap();
    }

    /// <summary>
    /// The player dismissed the view (Escape, or the "Close message" stop) rather than stepping back
    /// through history. A dismiss is not a history move, so the entry goes away and Forward will not
    /// resurrect it.
    /// </summary>
    public void NoteDismissed(int index) => _slots.Remove(index);

    /// <summary>The virtual Back landed on the inbox: the email entry above it still exists, but the
    /// player is no longer inside it.</summary>
    public void NoteSteppedToInbox(int index) => SetWasOpen(index, false);

    /// <summary>The virtual Forward (or a Back that restored the view) put the player back inside the
    /// email at this index.</summary>
    public void NoteSteppedToMessage(int index) => SetWasOpen(index, true);

    /// <summary>
    /// Drop the entry at this index: the restore could not be verified against the live reading pane.
    /// The player stays on the inbox and hears an honest gap rather than a message the mod cannot
    /// confirm it is showing.
    /// </summary>
    public void Drop(int index) => _slots.Remove(index);

    /// <summary>What Back means from the given position.</summary>
    public Step Back(int index, bool modalOpen) => modalOpen ? Step.CloseModal : Step.GameMove;

    /// <summary>
    /// What Forward means from the given position. Stepping up into the email only happens from its
    /// own inbox; with the view already open, Forward leaves the merged entry and walks the game's
    /// stack to whatever was opened out of the email.
    /// </summary>
    public Step Forward(int index, bool modalOpen)
        => !modalOpen && _slots.ContainsKey(index) ? Step.RestoreModal : Step.GameMove;

    /// <summary>
    /// The entry to restore after a game BACK landed on <paramref name="index"/>, or null for none.
    /// The landing index is read live rather than predicted: the game's Back does not always step
    /// exactly one node, so guessing could restore an email over the wrong screen.
    ///
    /// Back only. Entering a position from above lands on its topmost entry — the email; entering it
    /// from below (a game forward) lands on the inbox, and the email is one further press away. A
    /// forward move calls <see cref="Observe"/> alone.
    /// </summary>
    public Slot? RestoreAfterBack(int index, int count, string shape)
    {
        Observe(index, count, shape);
        return SlotAt(index);
    }

    /// <summary>One line for the /history dev dump.</summary>
    public string Describe()
    {
        if (_slots.Count == 0) return "none";
        return string.Join("; ", _slots.Values
            .OrderBy(s => s.Index)
            .Select(s => $"{s.Index}{(s.WasOpen ? "*" : "")}=\"{s.Subject}\""));
    }

    private void SetWasOpen(int index, bool wasOpen)
    {
        if (_slots.TryGetValue(index, out var slot))
            _slots[index] = slot.WithWasOpen(wasOpen);
    }

    private void TrimToCap()
    {
        while (_slots.Count > MaxSlots)
            _slots.Remove(_slots.Keys.Min());
    }

    // The history string's segments up to and including index, or null when the stack does not reach
    // that far — which the callers treat as "do not trust this position".
    private static string PrefixOf(string shape, int index)
    {
        if (index < 0) return null;
        var parts = shape.Split(new[] { ShapeSeparator }, System.StringSplitOptions.None);
        if (parts.Length <= index) return null;
        return string.Join(ShapeSeparator, parts.Take(index + 1));
    }
}

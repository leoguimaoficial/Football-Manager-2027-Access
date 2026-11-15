using System;
using System.Collections.Generic;
using System.Linq;
using FM27Access.Navigation;
using SI.Bindable;

namespace FM27Access.Plugin.Server;

// Main-thread native adapter for FM27's same-slot replacement. Native nodes are
// value types: retain their official identifiers, never their boxed Pointers.
// OpenPanel is a navigation command, not a replay of the offer/news business action.
internal static class ReplacedMessageHistory
{
    internal sealed class Entry
    {
        internal int Index;
        internal int SourceIndex;
        internal string SourceToken;
        internal int Count;
        internal string Prefix;
        internal string Subject;
        internal IntPtr Manager;
        internal IntPtr Owner;
        internal ulong Binding;
        internal PanelHistorySnapshot Inbox;
        internal PanelHistorySnapshot Destination;
        internal (uint Panel, uint Identifier)[] SourceKeys;
        internal bool Armed;
        internal readonly ReplacedMessageStep Steps = new();
    }

    internal sealed class PanelHistorySnapshot
    {
        internal FM.UI.PanelHistoryNode Node;
        internal uint Panel;
        internal uint Identifier;
        internal PanelHistorySnapshot(FM.UI.PanelHistoryNode node)
        { Node = node; Panel = node.PanelID; Identifier = node.Identifier; }
        internal bool Matches(FM.UI.PanelHistoryNode node)
            => node != null && node.PanelID == Panel && node.Identifier == Identifier;
        internal bool Matches(Panel panel)
            // Native replay can reuse a mounted instance with another runtime
            // Identifier. Node Identifier still guards the actual history slot;
            // foreground ownership additionally retains its Pointer/binding key.
            => panel != null && panel.PanelID?.ID == Panel;
    }

    private static readonly Dictionary<int, Entry> Entries = new();
    private static Entry Pending;
    private static Replay ActiveReplay;
    // An expired operation can still complete natively. Each callback retains
    // its OWN operation identity and stays rooted until that actual completion;
    // an older callback must never release a newer transition's guard.
    private static readonly HashSet<Replay> CallbackRoots = new();

    internal sealed class Replay
    {
        internal FM.UI.PanelHistoryManager Manager;
        internal PanelHistorySnapshot Target;
        internal bool PriorIgnore;
        internal bool Complete;
        internal bool Failed;
        internal long Until;
        internal Panel.TransitionCompleteCallback Callback;
    }

    private static string Prefix(string shape, int count)
        => string.Join(", ", (shape ?? "").Split(new[] { ", " }, StringSplitOptions.None).Take(count));

    internal static void Reset() { ReleaseReplay(); Pending = null; Entries.Clear(); }
    internal static void CancelPending() => Pending = null;

    private static void ReleaseReplay()
    {
        var replay = ActiveReplay;
        ActiveReplay = null;
        if (replay != null) replay.Manager.m_ignoreEvents = replay.PriorIgnore;
    }

    private static void OnTransitionComplete(Replay replay, Panel panel)
    {
        CallbackRoots.Remove(replay);
        try
        {
            // Native transition completion, not a panel-construction hook.
            // UI observation still waits for following Unity dispatches.
            replay.Complete = replay.Target.Matches(panel);
            replay.Failed = !replay.Complete;
        }
        catch (Exception error)
        {
            replay.Failed = true;
            Speech.Degrade.Note($"message history transition completion failed: {error.Message}");
        }
        finally
        {
            try { if (ReferenceEquals(ActiveReplay, replay)) ReleaseReplay(); }
            catch (Exception error)
            {
                replay.Failed = true;
                Speech.Degrade.Note($"message history recording-guard cleanup failed: {error.Message}");
            }
        }
    }

    // Called only after the real pane identity has been verified by OpenNow.
    // true means this is our same-slot virtual message, not a normal FM26 slot.
    internal static bool NoteOpened(string subject, bool restoring)
    {
        Observe();
        Pending = null;
        var h = GameHistory.TryRead();
        if (h == null) return false;
        if (Entries.TryGetValue(h.Value.Index, out var existing))
        {
            if (!existing.Inbox.Matches(FM27PanelCatalog.GetForegroundPanel())) return false;
            if (!restoring || MessageVerificationKey.Id(existing.Subject) != MessageVerificationKey.Id(subject))
            {
                existing.SourceIndex = h.Value.Index;
                existing.SourceToken = ServerText.ScreenToken(InteractivePanels.Current());
            }
            existing.Subject = subject;
            RememberOwner(existing, FM27PanelCatalog.GetForegroundPanel());
            existing.Steps.OpenMessage(restoring);
            return true;
        }
        CapturePending(subject, armed: false);
        return false;
    }

    // Only a validated command INSIDE the current native reading pane arms a
    // replacement/rewind. Opening a message or pressing history is not such an
    // action: an ordinary native Back must never turn Portal into its attachment.
    internal static void BeforeContentAction(string subject) => CapturePending(subject, armed: true);

    private static void CapturePending(string subject, bool armed)
    {
        Pending = null;
        var h = GameHistory.TryRead();
        if (h == null) return;
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        var nodes = manager?.m_history;
        var panel = FM27PanelCatalog.GetForegroundPanel();
        if (nodes == null || h.Value.Index >= nodes.Count || panel?.name != "PortalMessagesTool") return;
        var snapshot = new PanelHistorySnapshot(nodes[h.Value.Index]);
        if (!snapshot.Matches(panel))
        {
            if (!Entries.TryGetValue(h.Value.Index, out var existing)
                || !existing.Inbox.Matches(panel) || !SameOwner(existing, panel)) return;
            snapshot = existing.Inbox;
        }
        if (!MessageVerificationKey.Id(subject).HasValue) return;
        Pending = new Entry { Index = h.Value.Index, SourceIndex = h.Value.Index, Count = h.Value.Count,
            SourceToken = ServerText.ScreenToken(InteractivePanels.Current()),
            Prefix = Prefix(h.Value.Shape, h.Value.Index + 1), Subject = subject,
            Manager = manager.Pointer, Inbox = snapshot, Armed = armed };
        if (armed)
        {
            Pending.SourceKeys = new (uint, uint)[h.Value.Index + 1];
            for (int i = 0; i <= h.Value.Index; i++)
                Pending.SourceKeys[i] = (nodes[i].PanelID, nodes[i].Identifier);
        }
        RememberOwner(Pending, panel);
    }

    internal static void Observe()
    {
        if (ActiveReplay != null && Environment.TickCount64 >= ActiveReplay.Until)
            ReleaseReplay();
        var h = GameHistory.TryRead();
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        var nodes = manager?.m_history;
        if (h == null || nodes == null) { Reset(); return; }
        foreach (var e in Entries.Values.ToArray())
        {
            if (manager.Pointer != e.Manager || e.Index >= nodes.Count
                || !e.Destination.Matches(nodes[e.Index])
                || Prefix(h.Value.Shape, e.Index + 1) != e.Prefix)
                Entries.Remove(e.Index);
            else if (h.Value.Index > e.Index) e.Steps.LeftForNativeDestination();
        }
        var pending = Pending;
        if (pending == null || !pending.Armed) return;
        bool sameNativePrefix = false;
        if (h.Value.Index >= 0 && h.Value.Index < pending.Index && pending.SourceKeys != null
            && h.Value.Index < pending.SourceKeys.Length && h.Value.Index < nodes.Count)
        {
            var currentKeys = new (uint Panel, uint Identifier)[h.Value.Index + 1];
            for (int i = 0; i <= h.Value.Index; i++)
                currentKeys[i] = (nodes[i].PanelID, nodes[i].Identifier);
            sameNativePrefix = ReplacedMessageStep.EarlierPrefixMatches(pending.SourceKeys, currentKeys, h.Value.Index);
        }
        bool reused = ReplacedMessageStep.CanReuseEarlierDestination(pending.Index, pending.Count,
            pending.Prefix, h.Value.Index, h.Value.Count, h.Value.Shape, sameNativePrefix);
        if (manager.Pointer != pending.Manager || h.Value.Index >= nodes.Count
            || !(reused || ReplacedMessageStep.CanReplaceSource(pending.Index, pending.Count, pending.Prefix,
                h.Value.Index, h.Value.Count, h.Value.Shape)))
        { Pending = null; return; }
        var node = nodes[h.Value.Index];
        if (pending.Inbox.Matches(node))
        {
            var source = FM27PanelCatalog.GetForegroundPanel();
            if (pending.Inbox.Matches(source) && !SameOwner(pending, source)) Pending = null;
            return;
        }
        var destination = new PanelHistorySnapshot(node);
        // A new binding of the same Inbox is not an attachment destination.
        if (destination.Panel == pending.Inbox.Panel) { Pending = null; return; }
        if (!destination.Matches(FM27PanelCatalog.GetForegroundPanel())) return;
        int sourceIndex = pending.Index;
        if (reused)
            Speech.Log.Info($"[ReplacedMessageHistory] Native destination reused an earlier slot: {sourceIndex}->{h.Value.Index}, count {pending.Count}->{h.Value.Count}.");
        pending.Index = h.Value.Index;
        pending.Count = h.Value.Count;
        pending.SourceKeys = null;
        pending.Destination = destination;
        pending.Prefix = Prefix(h.Value.Shape, pending.Index + 1);
        pending.Steps.Arrive(ReplacedMessageStep.Position.Destination);
        RememberOwner(pending, FM27PanelCatalog.GetForegroundPanel());
        Entries[pending.Index] = pending;
        MessageViewHistory.Current.Drop(sourceIndex);
        Pending = null;
        while (Entries.Count > 8) Entries.Remove(Entries.Keys.Min());
    }

    internal static void Dismiss(int index)
    {
        Pending = null;
        if (Entries.TryGetValue(index, out var entry)) entry.Steps.DismissMessage();
    }

    internal static bool HasVirtualMove(string direction)
    {
        Observe();
        var h = GameHistory.TryRead();
        return h != null && Entries.TryGetValue(h.Value.Index, out var entry)
            && IsAtExpectedPanel(entry)
            && entry.Steps.Plan(direction == "back") != ReplacedMessageStep.Move.Game;
    }

    // Cursor provenance only, never an action destination. The original Inbox
    // slot can disappear when a native attachment reuses an earlier slot.
    internal static string CursorOrigin()
    {
        Observe();
        var h = GameHistory.TryRead();
        return h != null && Entries.TryGetValue(h.Value.Index, out var entry)
            && MessageVerificationKey.Id(entry.Subject) is int messageId && IsAtExpectedPanel(entry)
                ? System.Text.Json.JsonSerializer.Serialize(new {
                    index = entry.SourceIndex, token = entry.SourceToken, message = messageId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }) : null;
    }

    private static bool IsAtExpectedPanel(Entry entry)
    {
        var panel = FM27PanelCatalog.GetForegroundPanel();
        return (entry.Steps.Current == ReplacedMessageStep.Position.Destination
            ? entry.Destination : entry.Inbox).Matches(panel) && SameOwner(entry, panel);
    }

    private static ulong Binding(Panel panel)
        => panel?.m_bindingRoot?.TryCast<BindingRoot>()?.Key.m_key ?? 0;
    private static bool SameOwner(Entry entry, Panel panel)
        => panel != null && panel.Pointer == entry.Owner && Binding(panel) == entry.Binding;
    private static void RememberOwner(Entry entry, Panel panel)
    { entry.Owner = panel.Pointer; entry.Binding = Binding(panel); }

    // Null means ordinary native/FM26 history. Non-null plans have issued at
    // most ONE native navigation command and retain the same press deadline.
    internal static MoveRequest Begin(bool back)
    {
        Observe();
        Pending = null;
        var h = GameHistory.TryRead();
        if (h == null || !Entries.TryGetValue(h.Value.Index, out var entry) || !IsAtExpectedPanel(entry)) return null;
        var move = entry.Steps.Plan(back);
        if (move == ReplacedMessageStep.Move.Game) return null;
        MessageViewSurface.CloseIfOpen();
        var target = move == ReplacedMessageStep.Move.Destination ? entry.Destination : entry.Inbox;
        var replay = !target.Matches(FM27PanelCatalog.GetForegroundPanel()) ? Open(entry, target) : null;
        return new MoveRequest(entry, move, target, replay);
    }

    // A native Forward enters this composite node from below at its Inbox;
    // a native Back enters it from above at its destination, as in FM26.
    internal static MoveRequest Land(int index, bool back)
    {
        Observe();
        if (!Entries.TryGetValue(index, out var entry)) return null;
        var previous = entry.Steps.Current;
        entry.Steps.Arrive(ReplacedMessageStep.Position.Destination);
        if (back && entry.Steps.HasDestination && previous != ReplacedMessageStep.Position.Message)
        { RememberOwner(entry, FM27PanelCatalog.GetForegroundPanel()); return null; }
        var replay = Open(entry, entry.Inbox);
        return new MoveRequest(entry, back && entry.Steps.HasMessage
            ? ReplacedMessageStep.Move.Message : ReplacedMessageStep.Move.Inbox, entry.Inbox, replay);
    }

    internal static void LeaveForNativeMove(string direction)
    {
        Observe();
        Pending = null;
        var h = GameHistory.TryRead();
        if (h == null || !(direction == "back" ? h.Value.CanBack : h.Value.CanForward)
            || !Entries.TryGetValue(h.Value.Index, out var entry) || !IsAtExpectedPanel(entry)
            || entry.Steps.Current == ReplacedMessageStep.Position.Destination) return;
        var panel = FM27PanelCatalog.GetForegroundPanel();
        if (!entry.Inbox.Matches(panel)) return;
        var manager = FM.UI.PanelHistoryManagerModule.Instance.PanelHistoryManager;
        bool ignored = manager.m_ignoreEvents;
        // The temporary Inbox is not the native node at this index. Native
        // Back otherwise closes the recorded offer and leaves the Inbox covering
        // its real parent. Close ONLY our exact foreground Inbox first.
        try { manager.m_ignoreEvents = true; PanelManager.Instance.Close(panel); }
        finally { manager.m_ignoreEvents = ignored; }
    }

    private static Replay Open(Entry entry, PanelHistorySnapshot target)
    {
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        if (manager == null || manager.Pointer != entry.Manager) throw new InvalidOperationException("Message history owner changed.");
        // Use the SAME native event-recording guard as native history replay.
        // Opening is ASYNCHRONOUS: a call-only scope passed retained-Inbox probes
        // but failed a real recreated offer. Hold the native guard until its
        // actual transition completion, with one bounded lease and rooted
        // callback. Timeout, supersession, load and shutdown release it too.
        // ForceIgnorePanelForHistory is NOT equivalent: live probes showed it
        // can replace/push a node. Never write native history entries/indices.
        if (ActiveReplay != null || manager.m_ignoreEvents)
            throw new InvalidOperationException("A native history transition is still opening. Wait for it to finish.");
        if (CallbackRoots.Count >= 8)
            throw new InvalidOperationException("Earlier message transitions have not completed. Restart the game before navigating again.");
        var replay = new Replay { Manager = manager, Target = target,
            PriorIgnore = manager.m_ignoreEvents, Until = Environment.TickCount64 + 5000 };
        replay.Callback = (Panel.TransitionCompleteCallback)(Action<Panel>)(panel => OnTransitionComplete(replay, panel));
        CallbackRoots.Add(replay);
        ActiveReplay = replay;
        try { manager.m_ignoreEvents = true; manager.OpenPanel(target.Node, replay.Callback); }
        catch
        {
            ReleaseReplay();
            throw;
        }
        return replay;
    }

    internal sealed class MoveRequest
    {
        private readonly Entry _entry;
        private readonly ReplacedMessageStep.Move _move;
        private readonly PanelHistorySnapshot _target;
        private readonly Replay _replay;
        internal MoveRequest(Entry entry, ReplacedMessageStep.Move move, PanelHistorySnapshot target, Replay replay)
        { _entry = entry; _move = move; _target = target; _replay = replay; }

        // Request thread. Every readiness/validity check is on Unity's thread.
        internal string Wait(long deadline, Func<bool> superseded)
        {
            try
            {
            while (!superseded())
            {
                int state = MainThreadDispatcher.Invoke(() =>
                {
                    Observe();
                    if (!Entries.TryGetValue(_entry.Index, out var current) || !ReferenceEquals(current, _entry)
                        || GameHistory.TryRead()?.Index != _entry.Index) return -1;
                    if (_replay?.Failed == true) return -1;
                    if (_replay != null && !_replay.Complete) return 0;
                    if (!_target.Matches(FM27PanelCatalog.GetForegroundPanel())) return 0;
                    RememberOwner(_entry, FM27PanelCatalog.GetForegroundPanel());
                    _entry.Steps.Arrive(_move switch {
                        ReplacedMessageStep.Move.Message => ReplacedMessageStep.Position.Message,
                        ReplacedMessageStep.Move.Destination => ReplacedMessageStep.Position.Destination,
                        _ => ReplacedMessageStep.Position.Inbox });
                    return 1;
                });
                if (state < 0) return "Message history changed. Refresh the screen.";
                if (state > 0) return _move == ReplacedMessageStep.Move.Message
                    ? MessageViewSurface.Restore(_entry.Index, _entry.Subject, deadline, superseded) : "";
                if (Environment.TickCount64 >= deadline) return "Message destination did not open in time.";
                System.Threading.Thread.Sleep(60);
            }
            return "";
            }
            finally
            {
                if (_replay != null) MainThreadDispatcher.Invoke(() =>
                {
                    // Supersession cannot undo a native command already sent.
                    // Its guard lasts until that own callback or bounded expiry,
                    // rather than letting a late open corrupt native history.
                    if (ReferenceEquals(ActiveReplay, _replay)
                        && (_replay.Complete || Environment.TickCount64 >= _replay.Until)) ReleaseReplay();
                    return true;
                });
            }
        }
    }
}

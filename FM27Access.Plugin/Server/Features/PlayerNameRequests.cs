using System;
using System.Collections.Generic;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Names for database entities a screen holds only as references (player scatters and flag links),
/// asked of the game's own binding system: <c>SI.Bindable.BindingUtils.Request</c> with the path
/// <c>"Name"</c> on the person, the same property request a template makes when it shows a name.
///
/// Verified live 2026-10-04: the request is one-shot and asynchronous — it parks a temporary node
/// under the given parent, the value arrives on the callback about a second later as the game's
/// entity link, and the game removes the node itself (its key is dead afterwards, so it is never
/// read or removed here). Repeated requests left no nodes behind.
///
/// Accuracy: a name is kept only under the record reference its own link names
/// (<see cref="EntityLinkName"/>), and a row asks for the name of its own person's reference, so a
/// name can only ever reach the player it belongs to. No reference, no name.
///
/// Lifetime: names live for the current native panel-set/root-binding scope and language, shared
/// by simultaneous cards. A different native scope drops the set and requests fresh answers;
/// one card cannot invalidate another card's pending names. A request is made at most once per
/// player per scope; one that has not answered
/// after <see cref="AnswerTimeoutMs"/> leaves that row unnamed and is logged once. The callback is a static readonly field: the game holds a raw pointer to
/// it (feedback_il2cpp_delegate_lifetime). Main thread only, like every binding call.
/// </summary>
internal static class PlayerNameRequests
{
    private const string NamePath = "Name";

    private static readonly SI.Bindable.Bindings.ValueChangedCallback Callback =
        new Action<SI.Bindable.Bindings.Key, SI.Core.TypedValue>(OnName);

    // Session state. Guarded by the lock because the signature is read from the SSE path.
    private static readonly object Gate = new();
    private sealed class SessionState
    {
        internal string Session;
        internal readonly Dictionary<int, string> Names = new();
        internal readonly Dictionary<int, long> Asked = new();
        internal readonly HashSet<int> Unanswered = new();
    }
    // Flag links and scatter points can coexist; one must not reset the other's requests.
    private static readonly SessionState Players = new();
    private static readonly SessionState Entities = new();
    private static readonly Dictionary<ulong, (SessionState State, string Session, int Reference)> Pending = new();
    // A cached native property can call back inside Request, before it returns
    // the temporary key. Unity calls and these requests run on the main thread.
    private static (SessionState State, string Session, int Reference)? Issuing;
    private const long AnswerTimeoutMs = 5000;
    private static int _received;

    /// <summary>
    /// The verified name for this person, or null. Asks the game the first time a person is seen in
    /// the current native scope; the answer arrives on a later render (the screen token moves,
    /// so the page re-renders by itself). The caller's card-session argument is retained for the
    /// existing reader interface; it must not partition simultaneous native cards.
    /// </summary>
    internal static string NameFor(string session, SI.Bindable.Bindings.Key parent, SI.Core.TypedValue person)
    {
        var reference = BindingRecords.PersonOf(person);
        if (reference == null) return null;
        // Several scatter/pass-map cards are composed together. Per-card
        // sessions reset each other's asynchronous answers on every walk.
        // Share the native panel-set context, still caching only verified
        // answers under their own exact database-person reference.
        return NameFor(Players, NativeScope("native-players:"), parent, person, reference.CombinedIndexAndType, person);
    }

    // FM27 flag-only links hold the same database record identity as player names.
    // Keep the native localized entity-link answer and reference verification contract.
    internal static string NameForEntity(SI.Bindable.Bindings.Key parent,
        SI.Core.TypedValue entity, Il2CppSystem.Object owner)
    {
        var reference = entity?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
        if (reference == null) return null;
        // A composed screen can contain entity links in both its title bar
        // and body. Per-panel sessions reset each other on every walk and
        // continuously request the same names. Share the actual panel-set
        // context, including native rebindings, without the name-version token.
        return NameFor(Entities, NativeScope("native-entities:"), parent, entity, reference.CombinedIndexAndType, owner);
    }

    private static string NativeScope(string prefix)
    {
        var scope = new System.Text.StringBuilder(prefix);
        foreach (var view in InteractivePanels.Current())
        {
            var panel = view.Panel;
            var root = panel.m_bindingRoot?.TryCast<SI.Bindable.BindingRoot>();
            scope.Append(panel.Pointer).Append(':').Append(root?.Key.m_key ?? 0).Append('|');
        }
        return scope.ToString();
    }

    private static string NameFor(SessionState state, string session, SI.Bindable.Bindings.Key parent,
        SI.Core.TypedValue entity, int id, Il2CppSystem.Object owner)
    {
        session += ":" + SI.Translation.TranslationManager.CurrentLanguage;

        lock (Gate)
        {
            if (session != state.Session)
            {
                state.Session = session;
                state.Names.Clear();
                state.Asked.Clear();
                state.Unanswered.Clear();
                foreach (var key in new List<ulong>(Pending.Keys))
                    if (ReferenceEquals(Pending[key].State, state)) Pending.Remove(key);
            }
            if (state.Names.TryGetValue(id, out var name)) return name;
            if (state.Asked.TryGetValue(id, out long askedAt))
            {
                if (Environment.TickCount64 - askedAt > AnswerTimeoutMs && state.Unanswered.Add(id))
                    Degrade.Note($"[PlayerNameRequests] no name answer for reference 0x{id:x8} after {AnswerTimeoutMs}ms; row stays unnamed");
                return null;
            }
            state.Asked[id] = Environment.TickCount64;
        }

        var previous = Issuing;
        try
        {
            Issuing = (state, session, id);
            var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            // FM27's SubscriptionOwnerTable rejects a null owner. Use the live native
            // source/control, rather than changing the property or showing a tooltip.
            var request = SI.Bindable.BindingUtils.Request(bindings, parent, entity, NamePath, owner, Callback);
            // Request returns its temporary source root; the callback identifies
            // the Name child created by BindOneShot, not that source root.
            var nameNode = bindings.TryGetNode(ref request, out var requestNode)
                ? BindingRecords.FindNode(requestNode, NamePath, 0) : null;
            if (nameNode != null)
                lock (Gate)
                    if (!state.Names.ContainsKey(id))
                        Pending[nameNode.Key.m_key] = (state, session, id);
        }
        catch (Exception ex)
        {
            Degrade.Note($"[PlayerNameRequests] name request failed, row stays unnamed: {ex.GetType().Name}: {ex.Message}");
        }
        finally { Issuing = previous; }
        return null;
    }

    /// <summary>Folded into the screen token: moves when a name arrives, so the page re-renders.</summary>
    internal static string Signature()
    {
        lock (Gate) return _received == 0 ? "" : "names:" + _received;
    }

    private static void OnName(SI.Bindable.Bindings.Key key, SI.Core.TypedValue value)
    {
        try
        {
            (SessionState State, string Session, int Reference) request;
            lock (Gate)
            {
                if (!Pending.Remove(key.m_key, out request))
                {
                    if (!Issuing.HasValue) return;
                    request = Issuing.Value;
                }
                if (request.State.Session != request.Session) return;
            }
            string link = value?.AsString();
            if (!EntityLinkName.TryParse(link, out int reference, out string name))
            {
                Degrade.Note("[PlayerNameRequests] a name answer did not parse as a single entity link; ignored");
                return;
            }
            string clean = SyntheticTables.CleanOrNull(name);
            if (clean == null || reference != request.Reference)
            {
                Degrade.Note($"[PlayerNameRequests] name answer reference 0x{reference:x8} does not identify request 0x{request.Reference:x8}; ignored");
                return;
            }
            lock (Gate)
            {
                // Both the one-shot request and its answer identify this exact record/session.
                if (request.State.Session != request.Session) return;
                request.State.Names[reference] = clean;
                _received++;
            }
        }
        catch (Exception ex)
        {
            Degrade.Note($"[PlayerNameRequests] name answer unreadable: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

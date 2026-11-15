#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace FM27Access.Plugin.Server;

// FM27 can replace the Inbox node with a message's destination at the SAME
// history index. Extend FM26's virtual message entry without rewriting that
// native node: Inbox -> verified message -> actual native destination.
internal sealed class ReplacedMessageStep
{
    internal static bool EarlierPrefixMatches(IReadOnlyList<(uint Panel, uint Identifier)> source,
        IReadOnlyList<(uint Panel, uint Identifier)> current, int destination)
    {
        if (destination < 0 || destination >= source.Count || destination >= current.Count) return false;
        for (int i = 0; i <= destination; i++)
        {
            if (source[i].Panel != current[i].Panel) return false;
            // The native action renews the destination's visit Identifier while
            // reusing its slot. Preserve EXACT preceding visits; the result's
            // new Identifier becomes the captured destination identity afterward.
            if (i < destination && source[i].Identifier != current[i].Identifier) return false;
        }
        return true;
    }
    internal static bool CanReplaceSource(int sourceIndex, int sourceCount, string sourcePrefix,
        int index, int count, string shape)
    {
        if (sourceIndex < 0 || sourceIndex >= sourceCount || index != sourceIndex
            || count <= index || count > sourceCount) return false;
        // A native replacement can clear its previous Forward branch. Retain
        // the actual preceding nodes, not an obsolete total count.
        static string Before(string value, int at) => string.Join(", ",
            value.Split(new[] { ", " }, StringSplitOptions.None).Take(at));
        return Before(sourcePrefix, sourceIndex) == Before(shape, index);
    }

    internal static bool CanReuseEarlierDestination(int sourceIndex, int sourceCount,
        string sourcePrefix, int index, int count, string shape, bool sameNativePrefix)
    {
        if (!sameNativePrefix || index < 0 || index >= sourceIndex
            || sourceIndex >= sourceCount || count <= index || count > sourceCount) return false;
        // Native OpenPanel may find an existing destination BEFORE the Inbox
        // and rewind to it. Names alone are insufficient: the caller must also
        // compare exact preceding visits and the reused destination panel kind.
        static string Through(string value, int at) => string.Join(", ",
            value.Split(new[] { ", " }, StringSplitOptions.None).Take(at + 1));
        return Through(sourcePrefix, index) == Through(shape, index);
    }

    internal enum Position { Inbox, Message, Destination }
    internal enum Move { Game, Inbox, Message, Destination }
    internal Position Current { get; private set; } = Position.Destination;
    internal bool HasMessage { get; private set; } = true;
    internal bool HasDestination { get; private set; } = true;

    internal Move Plan(bool back) => (back, Current) switch
    {
        (true, Position.Destination) => HasMessage ? Move.Message : Move.Inbox,
        (true, Position.Message) => Move.Inbox,
        (false, Position.Inbox) => HasMessage ? Move.Message : HasDestination ? Move.Destination : Move.Game,
        (false, Position.Message) => HasDestination ? Move.Destination : Move.Game,
        _ => Move.Game,
    };
    internal void Arrive(Position position) => Current = position;
    internal void OpenMessage(bool restoring)
    {
        // An explicit new selection creates a branch, like FM26's forward
        // truncation. A history restoration preserves the actual destination.
        if (!restoring) HasDestination = false;
        HasMessage = true;
        Current = Position.Message;
    }
    internal void DismissMessage() { HasMessage = false; Current = Position.Inbox; }
    internal void LeftForNativeDestination()
    {
        if (Current == Position.Destination) return;
        HasDestination = false;
        if (Current == Position.Inbox) HasMessage = false;
    }
}

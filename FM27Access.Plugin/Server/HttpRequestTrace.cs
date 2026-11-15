#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FM27Access.Plugin.Server;

// Bounded, in-memory transport diagnostics only. Callers supply the existing
// sanitized route label; never retain URL queries, headers, cookies or bodies.
internal sealed class HttpRequestTrace
{
    internal sealed class Entry
    {
        public long Id { get; init; }
        public DateTime StartedUtc { get; init; }
        public int PeerPort { get; init; }
        public string Route { get; set; } = "before headers";
        public string Phase { get; set; } = "reading";
        public double ElapsedMs { get; set; }
        public string? ErrorType { get; set; }
        internal long Stamp;
    }

    private readonly object _gate=new();
    private readonly Queue<Entry> _entries=new();
    private readonly int _capacity;
    private long _next;

    internal HttpRequestTrace(int capacity=512)
    {
        if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity=capacity;
    }

    internal Entry Begin(int peerPort)
    {
        lock(_gate)
        {
            var entry=new Entry{Id=++_next,StartedUtc=DateTime.UtcNow,PeerPort=peerPort,
                Route="before headers",Phase="reading",Stamp=Stopwatch.GetTimestamp()};
            _entries.Enqueue(entry);
            while(_entries.Count>_capacity)_entries.Dequeue();
            return entry;
        }
    }

    internal void Mark(Entry entry,string phase,string? route=null,string? errorType=null)
    {
        lock(_gate)
        {
            if(route!=null)entry.Route=route;
            entry.Phase=phase;
            entry.ErrorType=errorType;
            entry.ElapsedMs=(Stopwatch.GetTimestamp()-entry.Stamp)*1000.0/Stopwatch.Frequency;
        }
    }

    internal Entry[] Snapshot()
    {
        lock(_gate)return _entries.Select(e=>new Entry{Id=e.Id,StartedUtc=e.StartedUtc,
            PeerPort=e.PeerPort,Route=e.Route,Phase=e.Phase,ErrorType=e.ErrorType,
            ElapsedMs=e.Phase is "accepted" or "reading" or "handling"
                ?(Stopwatch.GetTimestamp()-e.Stamp)*1000.0/Stopwatch.Frequency:e.ElapsedMs}).ToArray();
    }
}

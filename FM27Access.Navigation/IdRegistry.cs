using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Allocates stable, unique element ids for one rendered screen. An id is the FNV-1a hash
/// of a caller-supplied key (typically an element's identity path), so the same element
/// yields the same id on every render — which lets the HTML surface restore focus and patch
/// values by id. Identical keys (e.g. unnamed repeated subtrees) get a numeric suffix so
/// every element stays addressable.
///
/// One instance is the single id pool for a screen: both interactive handles and read-only
/// live-value elements draw from it, so their ids can never collide. Cleared per render.
/// Pure and single-threaded (used on the render/main thread).
/// </summary>
public sealed class IdRegistry
{
    private readonly HashSet<string> _used = new();

    public void Clear() => _used.Clear();

    public string Allocate(string key)
    {
        var id = "e" + Fnv1a(key).ToString("x8");
        if (_used.Contains(id))
        {
            int n = 2;
            while (_used.Contains(id + "-" + n)) n++;
            id = id + "-" + n;
        }
        _used.Add(id);
        return id;
    }

    private static uint Fnv1a(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}

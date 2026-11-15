using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The mod-owned bookmark store: up to 12 destinations from the Navigator's menu tree, slot
/// order = the companion's Ctrl chords (1..9, 0 for slot 10, minus for 11, equals for 12).
/// Replaces the game-bookmark-backed Ctrl chords: the game's catalog covers only ~29 tool
/// destinations, while the menu tree exposes every live screen. Independent of the game's own
/// bookmark system — the in-game strip and Shift+N shortcuts are untouched.
///
/// This is the mod's first persisted user data: bookmarks.json beside the plugin. Labels and
/// paths are captured at add time for DISPLAY only; every activation re-resolves the
/// (uid, obj) address live through NavigatorIndex.Go, so an entry the current save cannot
/// satisfy (an entity-bound page from another save, a patched-away panel) fails honestly and
/// never opens a guessed screen. All access is lock-guarded: edits run on the main thread
/// (editor adapters), slot reads on server request threads.
/// </summary>
internal static class ModBookmarks
{
    internal const int MaxSlots = 12;

    internal sealed class Entry
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public uint Uid { get; set; }
        public int Obj { get; set; } = -1;
    }

    private static readonly object Gate = new();
    private static List<Entry> _entries;

    private static readonly string StorePath =
        System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", "bookmarks.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The player's spoken name for a slot's chord ("Ctrl 3", "Ctrl 0", "Ctrl minus").</summary>
    internal static string ChordName(int slot) => slot switch
    {
        10 => "Ctrl 0",
        11 => "Ctrl minus",
        12 => "Ctrl equals",
        _ => "Ctrl " + slot,
    };

    internal static List<Entry> Snapshot()
    {
        lock (Gate) { Load(); return new List<Entry>(_entries); }
    }

    internal static int Count
    {
        get { lock (Gate) { Load(); return _entries.Count; } }
    }

    /// <summary>The 1-based slot's entry and the total count; entry null when the slot is empty.</summary>
    internal static (Entry Entry, int Count) Slot(int slot)
    {
        lock (Gate)
        {
            Load();
            var entry = slot >= 1 && slot <= _entries.Count ? _entries[slot - 1] : null;
            return (entry, _entries.Count);
        }
    }

    /// <summary>Add a destination. Null on success, else the honest refusal to speak.</summary>
    internal static string Add(string label, string path, uint uid, int obj)
    {
        lock (Gate)
        {
            Load();
            if (_entries.Count >= MaxSlots)
                return $"Bookmarks full, {MaxSlots} of {MaxSlots} set.";
            foreach (var e in _entries)
                if (e.Uid == uid && e.Obj == obj)
                    return label + " is already bookmarked.";
            _entries.Add(new Entry { Label = label, Path = path, Uid = uid, Obj = obj });
            Save();
            return null;
        }
    }

    internal static bool Remove(uint uid, int obj)
    {
        lock (Gate)
        {
            Load();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Uid != uid || _entries[i].Obj != obj) continue;
                _entries.RemoveAt(i);
                Save();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Move an entry one slot up (delta -1) or down (+1). The new 1-based slot on success,
    /// -1 when the entry is gone or already at that edge (the caller speaks the truth either way).
    /// </summary>
    internal static int Move(uint uid, int obj, int delta)
    {
        lock (Gate)
        {
            Load();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Uid != uid || _entries[i].Obj != obj) continue;
                int target = i + delta;
                if (target < 0 || target >= _entries.Count) return -1;
                (_entries[i], _entries[target]) = (_entries[target], _entries[i]);
                Save();
                return target + 1;
            }
            return -1;
        }
    }

    private static void Load()
    {
        if (_entries != null) return;
        try
        {
            if (!File.Exists(StorePath)) { _entries = new List<Entry>(); return; }
            var parsed = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(StorePath), JsonOptions);
            _entries = parsed ?? new List<Entry>();
            if (_entries.Count > MaxSlots)
                _entries.RemoveRange(MaxSlots, _entries.Count - MaxSlots);
        }
        catch (Exception e)
        {
            // A corrupt store must neither crash the surface nor be silently overwritten by the
            // next save: set it aside under a .bad name (the player's data stays recoverable),
            // note it, and start empty.
            Degrade.Note($"bookmark store unreadable, starting empty: {e.GetType().Name}: {e.Message}");
            try { File.Move(StorePath, StorePath + ".bad", overwrite: true); }
            catch (Exception m) { Log.Warn($"[ModBookmarks] could not set corrupt store aside: {m.Message}"); }
            _entries = new List<Entry>();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath));
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_entries, JsonOptions));
        }
        catch (Exception e)
        {
            Degrade.Note($"bookmark store save failed, edits kept in memory: {e.GetType().Name}: {e.Message}");
        }
    }
}

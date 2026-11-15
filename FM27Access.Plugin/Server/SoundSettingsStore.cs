using System;
using System.IO;
using System.Text;
using System.Text.Json;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Persistence for the player's sound-cue levels and the bed switches. Same shape as
/// <see cref="ModBookmarks"/>: a JSON file beside the plugin, loaded lazily, written on every change,
/// guarded by one lock because the HTTP thread and the main thread both reach it.
///
/// The companion owns the audio graph, so nothing here is in the audio path: the page writes a change
/// through <c>/soundsetting</c>, applies it to the live graph client-side straight away, and this only
/// decides what the NEXT session starts with. That split is deliberate — a slider must not wait on a
/// disk write to be heard, and a failed write must not stop the sound changing.
///
/// A save failure degrades rather than halting: the setting still applies for this session and the
/// note says the value will not survive a restart, which is honest and leaves the match playable.
/// </summary>
internal static class SoundSettingsStore
{
    private static readonly object Gate = new();
    private static SoundSettings _settings;
    private static string _persistenceNotice;
    private const string SaveFailureNotice = "The sound setting applies to this session, but could not be saved for the next session.";

    internal static string PersistenceNotice
    {
        get { lock (Gate) return _persistenceNotice; }
    }

    private static readonly string StorePath =
        System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", "soundsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The current settings, as a copy the caller may not mutate into the store.</summary>
    internal static SoundSettings Current()
    {
        lock (Gate) { Load(); return _settings.Copy(); }
    }

    /// <summary>
    /// Apply one setting, whichever kind it is. The settings object itself decides: a key it accepts as a
    /// switch is a switch, otherwise it is tried as a level. Deliberately NOT a list of toggle keys held
    /// somewhere else — the route used to name the wing-drone key specifically to pick the switch path, so
    /// the press indicator's switch was routed to the level path, rejected as an unknown level, and never
    /// saved. It applied client-side and reverted on the next render, which looks exactly like a setting
    /// that does not stick.
    ///
    /// Switches are tried first because the two key sets are disjoint and a switch value ("1"/"0") would
    /// also parse as a level.
    ///
    /// Returns "ok", or a message naming the fault — an unknown key is a caller bug worth surfacing
    /// rather than a silent no-op that looks like a dead control.
    /// </summary>
    internal static string Set(string key, string rawValue)
    {
        if (string.IsNullOrEmpty(key)) return "sound setting: no key given";

        lock (Gate)
        {
            Load();
            bool on = rawValue == "1" || string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
            if (_settings.TrySetToggle(key, on)) return Save();

            if (!int.TryParse(rawValue, out int level))
                return $"sound setting {key}: value '{rawValue}' is not a number";
            if (!_settings.TrySetLevel(key, level)) return $"sound setting: unknown setting '{key}'";
            return Save();
        }
    }

    /// <summary>
    /// The settings as JSON for the companion to apply at audio init. Hand-built rather than
    /// serialized so the wire keys are exactly the ones the page and app.js already use, and cannot
    /// drift with a property rename.
    /// </summary>
    internal static string ToJson()
    {
        var s = Current();
        var sb = new StringBuilder();
        sb.Append('{')
          .Append("\"").Append(SoundSettings.MasterKey).Append("\":").Append(s.Master).Append(',')
          .Append("\"").Append(SoundSettings.PossessionKey).Append("\":").Append(s.Possession).Append(',')
          .Append("\"").Append(SoundSettings.PlaybackKey).Append("\":").Append(s.Playback).Append(',')
          .Append("\"").Append(SoundSettings.EventsKey).Append("\":").Append(s.Events).Append(',')
          .Append("\"").Append(SoundSettings.DronesKey).Append("\":").Append(s.Drones).Append(',')
          .Append("\"").Append(SoundSettings.PressureKey).Append("\":").Append(s.Pressure).Append(',')
          .Append("\"").Append(SoundSettings.WingDronesKey).Append("\":")
          .Append(s.WingDronesEnabled ? "true" : "false").Append(',')
          .Append("\"").Append(SoundSettings.PressureEnabledKey).Append("\":")
          .Append(s.PressureEnabled ? "true" : "false")
          .Append('}');
        return sb.ToString();
    }

    private static void Load()
    {
        if (_settings != null) return;
        try
        {
            if (!File.Exists(StorePath)) { _settings = new SoundSettings(); return; }
            var parsed = JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(StorePath), JsonOptions);
            _settings = parsed ?? new SoundSettings();
            // Deserialisation sets properties directly, so nothing has clamped these yet.
            _settings.Normalise();
        }
        catch (Exception e)
        {
            // Same contract as the bookmark store: a corrupt file is set aside rather than
            // overwritten, so the player's settings stay recoverable, and we start from defaults —
            // which means the drones come back off, the safe direction.
            Degrade.Note($"sound settings unreadable, starting from defaults: {e.GetType().Name}: {e.Message}");
            try { File.Move(StorePath, StorePath + ".bad." + Guid.NewGuid().ToString("N")); }
            catch (Exception m) { Log.Warn($"[SoundSettingsStore] could not set corrupt file aside: {m.Message}"); }
            _settings = new SoundSettings();
        }
    }

    private static string Save()
    {
        try
        {
            SettingsFileWriter.Write(StorePath, JsonSerializer.Serialize(_settings, JsonOptions),
                error => Log.Warn($"[SoundSettingsStore] Temporary settings cleanup failed: {error.GetType().Name}: {error.Message}"));
            _persistenceNotice = null;
            return "ok";
        }
        catch (Exception e)
        {
            Degrade.Note($"sound settings save failed, change applies this session only: "
                + $"{e.GetType().Name}: {e.Message}");
            return _persistenceNotice = SaveFailureNotice;
        }
    }
}

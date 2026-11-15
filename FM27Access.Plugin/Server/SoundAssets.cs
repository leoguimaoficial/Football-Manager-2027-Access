using System.Collections.Generic;
using System.IO;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The match audio-cue sound files, embedded in the assembly at build time and served as static
/// binary assets to the companion's WebView2 (GET /sound?name=...). The browser decodes each into a
/// Web Audio buffer once and plays them as pitch-tick cues during a match. These are static assets,
/// not game state, so reading each once and holding the bytes does not violate the no-cache rule.
///
/// A missing sound degrades in place: it fails only that one cue (the browser hears silence for it)
/// and is logged, never crashing the mod or muting the rest. Served on the background request thread,
/// so it logs via the background-safe file log rather than UnityEngine.Debug.
/// </summary>
internal static class SoundAssets
{
    // The sound names the companion may request. The medium tick drives the reference team's possession
    // metronome and the opptick the opposition's (an identical, parallel metronome); low/high remain
    // embedded but currently unused (the stereo pan carries the position the tick pitch used to). The
    // one-shots fire on match events (ball out of play over each line, the ball put back into play, a
    // goal for either team, a good defensive play by your team or the opposition, and an attempt on goal
    // by either team — shotattempt, or headerattempt when it was headed, both landing on the strike so the
    // outcome sounds resolve them). dronenear/dronefar are the two touchline drones, looped continuously
    // and varied only in level to say how near the ball is to each physical touchline. fastforwarding
    // loops for as long as the game is skipping ahead between highlights; replaystart/replayend mark
    // a replay beginning and ending. pressurelow/pressuremed/pressurehigh are the three carrier-pressure
    // stems, identical in length and run as phase-locked loops that are unmuted cumulatively as the man on
    // the ball is closed down. An unknown name is a 404, never a resource probe.
    private static readonly HashSet<string> Known =
        new() { "low", "medium", "high", "opptick",
                "touchline", "goalkick", "cornerfor", "corneragainst", "putinplay",
                "goalfor", "goalagainst", "gooddefence", "oppgooddefence",
                "shotattempt", "headerattempt", "contested", "air", "dronenear", "dronefar",
                "fastforwarding", "replaystart", "replayend",
                "pressurelow", "pressuremed", "pressurehigh" };

    // Lazily-read bytes per name. A cached null means "known name but its resource is missing" so a
    // broken build is not re-probed on every request.
    private static readonly Dictionary<string, byte[]> Cache = new();
    private static readonly object Gate = new();

    /// <summary>
    /// The bytes of the named cue sound, or false when the name is unknown or its embedded resource is
    /// missing. The bytes are read once and cached (static assets).
    /// </summary>
    public static bool TryGet(string name, out byte[] bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(name) || !Known.Contains(name)) return false;
        lock (Gate)
        {
            if (!Cache.TryGetValue(name, out bytes))
            {
                bytes = Load(name);
                Cache[name] = bytes;
            }
            return bytes != null;
        }
    }

    private static byte[] Load(string name)
    {
        var resourceName = "FM27Access.Plugin.Sounds." + name + ".wav";
        using var stream = typeof(SoundAssets).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // A build-time invariant (the wav is an EmbeddedResource). It breaks only this cue's audio,
            // not speech or the rest of the surface, so log and return null (the route answers 404).
            ServerText.LogBackground($"[SoundAssets] Embedded sound '{resourceName}' not found in assembly.");
            return null;
        }
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

using System;
using UnityEngine;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Mutes the GAME's own audio (crowd ambience, pitch sound effects — everything the game plays
/// through Unity audio) while match playback is actively fast-forwarding between highlights, and
/// restores it the moment real-time play resumes. The mod's own sounds are unaffected: they play
/// in the companion's WebView2 process and speech goes through out-of-process Prism, neither of
/// which routes through this process's <see cref="AudioListener"/>.
///
/// "Actively fast-forwarding" is the same judgement the SSE loop makes for the fast-forwarding
/// loop sound: the playback mode is BetweenHighlights* AND the playback slice moved since the
/// last check — a match paused mid-fast-forward keeps its mode, but nothing is advancing, so the
/// crowd comes back with the pause like every other sound.
///
/// The mute is <see cref="AudioListener.volume"/> = 0: process-wide, never persisted anywhere,
/// and nothing in the game's code writes that property (the game mixes through its AudioMixer),
/// so it cannot be fought over or saved into the player's settings. The pre-mute volume is
/// captured and restored exactly.
///
/// Failure doctrine: this is non-speech supplementary output, so every failure degrades toward
/// AUDIBLE — a read error while muted restores the game's sound and logs, never the reverse. A
/// stuck mute would silently rob the player of the game's soundscape; stuck sound during a
/// fast-forward is only the old noise. <see cref="Restore"/> runs from CoreLoop.Shutdown so a hot
/// reload or a Kill can never strand the game muted.
///
/// Main thread only: ticked every frame by CoreLoop.TickDone, internally throttled.
/// </summary>
internal static class GameAudioMute
{
    private const int IntervalMs = 250;

    private static long _nextCheckMs;
    private static int _lastSlice = int.MinValue;
    private static bool _muted;
    private static float _restoreVolume = 1f;
    private static bool _warned;

    /// <summary>Per-frame entry point (main thread); does its own ~4 Hz throttling.</summary>
    public static void Tick()
    {
        long now = Environment.TickCount64;
        if (now < _nextCheckMs) return;
        _nextCheckMs = now + IntervalMs;

        try
        {
            var (fastForward, slice) = MatchBallLocation.ReadPlaybackMotion();
            bool advancing = slice >= 0 && slice != _lastSlice;
            _lastSlice = slice;
            Apply(fastForward && advancing);
            _warned = false;
        }
        catch (Exception ex)
        {
            // Degrade toward audible: whatever broke, the player keeps the game's sound.
            Apply(false);
            if (!_warned)
            {
                _warned = true;
                Speech.Log.Warn($"[GameAudioMute] fast-forward check failed; game audio left on: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Unconditional restore for teardown paths (hot reload, Kill): a discarded core must never
    /// leave the game silent.
    /// </summary>
    public static void Restore() => Apply(false);

    private static void Apply(bool mute)
    {
        if (mute == _muted) return;
        if (mute)
        {
            _restoreVolume = AudioListener.volume;
            AudioListener.volume = 0f;
        }
        else
        {
            AudioListener.volume = _restoreVolume;
        }
        _muted = mute;
        EventLog.Record("gameaudio", mute ? "muted" : "restored");
    }
}

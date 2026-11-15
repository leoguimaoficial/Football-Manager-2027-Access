using System;
using System.Text;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the animation layer's ball-contact stream: GET /contactsprobe. Read-only apart
/// from the <c>GetBallContacts</c> call itself, whose consume semantics are one of the unknowns.
///
/// <c>SI.Match.Anim.IMatches.GetBallContacts(MatchID, NativeArray&lt;BallContact&gt;)</c> looks like the
/// "every touch of the ball, in order" source a contact-sound layer wants — each entry is a time, a 3D
/// pitch position, and a contact type (Kick, Header, Save, Post, NetInside...). But its only in-game
/// consumer (<c>MatchAudioPitchSounds</c>) polls it into a 12-slot buffer per tick, and it lives beside
/// the rendered 3D scene, so the decompiled source cannot answer:
///   - WHEN it is populated: during a rendered highlight only, or between highlights too (the
///     playback-interface ball reads work everywhere; the rendered Ball object does not),
///   - what a call returns: the recent window every time, or only contacts since the last call
///     (i.e. does polling it consume events the game's own audio would have played),
///   - what the time field is relative to (match seconds vs the playback time slice),
///   - whether unused slots read as id None with zeroed fields.
/// Alongside each dump it prints the current time slice, playback mode, and whether a rendered Ball
/// object exists, so repeated calls in different playback states map out the population conditions.
///
/// Each section is independently guarded: one broken hop prints its own error and the rest still dump.
/// </summary>
internal static class BallContactsProbe
{
    private const int BufferSlots = 12;   // the game's own MaxNumBallContacts

    internal static string Run()
    {
        var sb = new StringBuilder();
        DumpContext(sb);
        DumpConsumer(sb);
        DumpContacts(sb);
        return sb.ToString();
    }

    /// <summary>
    /// GET /contactsprobe?scan=1[&amp;from=SECONDS&amp;to=SECONDS&amp;step=SECONDS]: walk
    /// <c>GetBallVelocity</c> over a time range and report every discontinuity (incoming != outgoing
    /// velocity), which should be the ball's touch events — plus the ball position at that moment via
    /// <c>GetBallPosition</c> at 4 slices per second (confirmed live: discontinuity clusters sit at
    /// exactly slice/4 across successive reads). Defaults to the 30
    /// seconds up to the current playback slice. Establishes whether the velocity history is a usable
    /// touch stream and how its float time maps to the playback slice.
    /// </summary>
    internal static string Scan(float from, float to, float step, bool raw = false)
    {
        var sb = new StringBuilder();
        try
        {
            var ctrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
            var match = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
            if (ctrl == null || match == null) return "not in a match\n";
            var matchID = ctrl.CurrentMatchID;
            if (matchID == SI.Match.Anim.MatchID.None) return "MatchID is None\n";
            var matches = ResolveMatches(sb);
            if (matches == null) return sb.ToString();

            int slice = match.CurrentTimeSlice;
            if (step <= 0f) step = 0.1f;
            if (to <= 0f) to = slice * 0.25f;         // 4 slices/second, confirmed live
            if (from <= 0f) from = Math.Max(0f, to - 30f);
            int steps = (int)((to - from) / step);
            if (steps > 1200) return "range too wide: " + steps + " steps (cap 1200)\n";

            sb.Append("slice=").Append(slice).Append(" scanning t=").Append(from.ToString("F1"))
              .Append("..").Append(to.ToString("F1")).Append(" step=").Append(step.ToString("F2")).Append('\n');
            int touches = 0;
            var playback = ctrl.PlaybackInterface;
            for (float t = from; t <= to; t += step)
            {
                var v = matches.GetBallVelocity(matchID, t);
                float dx = v.incoming.x - v.outgoing.x;
                float dy = v.incoming.y - v.outgoing.y;
                float dz = v.incoming.z - v.outgoing.z;
                float delta = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                // raw: print every sample's speeds, not just the discontinuities. A window with no
                // discontinuities is otherwise ambiguous — a steady ball and a span the rolling history
                // no longer covers both print nothing — and that difference decides whether a replayed
                // span can be scanned for touches at all.
                if (raw)
                {
                    float inSpd = (float)Math.Sqrt(v.incoming.x * v.incoming.x + v.incoming.y * v.incoming.y + v.incoming.z * v.incoming.z);
                    float outSpd = (float)Math.Sqrt(v.outgoing.x * v.outgoing.x + v.outgoing.y * v.outgoing.y + v.outgoing.z * v.outgoing.z);
                    var rp = playback.GetBallPosition((int)(t * 4f));
                    sb.Append("  t=").Append(t.ToString("F1"))
                      .Append(" |in|=").Append(inSpd.ToString("F2"))
                      .Append(" |out|=").Append(outSpd.ToString("F2"))
                      .Append(" delta=").Append(delta.ToString("F2"))
                      .Append(" ball=(").Append(rp.x.ToString("F1")).Append(',')
                      .Append(rp.z.ToString("F1")).Append(")\n");
                }
                if (delta < 0.05f) continue;
                touches++;
                var pos = playback.GetBallPosition((int)(t * 4f));
                sb.Append("  t=").Append(t.ToString("F1"))
                  .Append(" delta=").Append(delta.ToString("F2"))
                  .Append(" in=(").Append(v.incoming.x.ToString("F1")).Append(',')
                  .Append(v.incoming.y.ToString("F1")).Append(',')
                  .Append(v.incoming.z.ToString("F1"))
                  .Append(") out=(").Append(v.outgoing.x.ToString("F1")).Append(',')
                  .Append(v.outgoing.y.ToString("F1")).Append(',')
                  .Append(v.outgoing.z.ToString("F1"))
                  .Append(") ballAtSlice=(").Append(pos.x.ToString("F1")).Append(',')
                  .Append(pos.y.ToString("F1")).Append(',')
                  .Append(pos.z.ToString("F1")).Append(")\n");
            }
            sb.Append(touches).Append(" discontinuities\n");
        }
        catch (Exception e)
        {
            sb.Append("FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
        return sb.ToString();
    }

    // The game's own consumer. If it is alive and ticking, an always-empty buffer from our poll means
    // it drains the contacts first (consumed-on-read); if it is absent or disabled, empty means the
    // native side is not recording contacts at all right now.
    private static void DumpConsumer(StringBuilder sb)
    {
        sb.Append("========== CONSUMER (MatchAudioComponent) ==========\n");
        try
        {
            var audio = UnityEngine.Object.FindObjectOfType<FM.Match.Audio.MatchAudioComponent>();
            if (audio == null) { sb.Append("  absent\n"); return; }
            sb.Append("  present, isActiveAndEnabled = ").Append(audio.isActiveAndEnabled).Append('\n');
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // The correlating state: playback slice/mode and whether a highlight is actually rendering.
    private static void DumpContext(StringBuilder sb)
    {
        sb.Append("========== CONTEXT ==========\n");
        try
        {
            var match = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
            if (match == null) { sb.Append("  no FM.Match.Match in the scene (not in a match)\n"); return; }
            sb.Append("  CurrentTimeSlice = ").Append(match.CurrentTimeSlice).Append('\n');
            sb.Append("  CurrentMatchPlaybackMode = ").Append(match.CurrentMatchPlaybackMode).Append('\n');
            var ball = UnityEngine.Object.FindObjectOfType<FM.Match.Ball>();
            sb.Append("  rendered Ball object = ").Append(ball == null ? "absent" : "present").Append('\n');
            var state = HarmonyLib.AccessTools.Property(typeof(FM.Match.Match), "m_matchState")?.GetValue(match);
            if (state != null)
            {
                var time = state.GetType().GetProperty("Time")?.GetValue(state);
                sb.Append("  MatchState.Time = ").Append(time ?? "<unreadable>").Append('\n');
            }
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    private static void DumpContacts(StringBuilder sb)
    {
        sb.Append("========== BALL CONTACTS ==========\n");
        try
        {
            var ctrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
            if (ctrl == null) { sb.Append("  no MatchPlaybackController (not in a match)\n"); return; }
            var matchID = ctrl.CurrentMatchID;
            sb.Append("  CurrentMatchID = ").Append((byte)matchID).Append('\n');
            if (matchID == SI.Match.Anim.MatchID.None)
            {
                sb.Append("  MatchID is None: no animation-layer match instance right now\n");
                return;
            }

            var matches = ResolveMatches(sb);
            if (matches == null) return;

            var contacts = new Unity.Collections.NativeArray<SI.Match.Anim.BallContact>(
                BufferSlots, Unity.Collections.Allocator.Temp);
            try
            {
                matches.GetBallContacts(matchID, contacts);
                for (int i = 0; i < contacts.Length; i++)
                {
                    var c = contacts[i];
                    sb.Append("  [").Append(i).Append("] id=").Append(c.id)
                      .Append(" time=").Append(c.time.ToString("F3"))
                      .Append(" pos=(").Append(c.position.x.ToString("F2"))
                      .Append(", ").Append(c.position.y.ToString("F2"))
                      .Append(", ").Append(c.position.z.ToString("F2")).Append(")\n");
                }
            }
            finally
            {
                contacts.Dispose();
            }

            // Timebase cross-check: if velocity comes back non-zero for some interpretation of "time",
            // the native side is tracking ball physics for this MatchID and the empty contact buffer is
            // a consumption/window question, not a dead subsystem.
            var match = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
            if (match != null)
            {
                int slice = match.CurrentTimeSlice;
                foreach (var t in new[] { slice * 1f, slice * 0.5f, slice * 0.25f, slice * 0.1f })
                {
                    var v = matches.GetBallVelocity(matchID, t);
                    sb.Append("  velocity(t=").Append(t.ToString("F1"))
                      .Append(") in=(").Append(v.incoming.x.ToString("F2")).Append(", ")
                      .Append(v.incoming.y.ToString("F2")).Append(", ")
                      .Append(v.incoming.z.ToString("F2"))
                      .Append(") out=(").Append(v.outgoing.x.ToString("F2")).Append(", ")
                      .Append(v.outgoing.y.ToString("F2")).Append(", ")
                      .Append(v.outgoing.z.ToString("F2")).Append(")\n");
                }
            }
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // The same native IMatches instance the game's animation and audio components hold. Preferred
    // source is the live Match3DAnimation's own reference; the wrapper's static GetInstance is the
    // fallback (same native singleton, but exercising the game's copy proves the instance is live).
    private static SI.Match.Anim.IMatches ResolveMatches(StringBuilder sb)
    {
        var anim = UnityEngine.Object.FindObjectOfType<FM.Match.Animation.Match3DAnimation>();
        if (anim != null)
        {
            var held = anim.m_sianimMatches;
            sb.Append("  Match3DAnimation.m_sianimMatches = ").Append(held == null ? "null" : "present").Append('\n');
            if (held != null) return held;
        }
        else
        {
            sb.Append("  no Match3DAnimation in the scene\n");
        }

        var instance = FM.GamePlugin.Bindings.MarshallingWrappers.SI_Match_Anim_IMatches_NativeWrapper.GetInstance();
        sb.Append("  static GetInstance() = ").Append(instance == null ? "null" : "present").Append('\n');
        if (instance == null)
        {
            sb.Append("  NO IMatches SOURCE AVAILABLE\n");
            return null;
        }
        var wrapper = ((Il2CppSystem.Object)instance).TryCast<SI.Match.Anim.IMatches>();
        if (wrapper == null) sb.Append("  GetInstance() result did not cast to IMatches\n");
        return wrapper;
    }
}
